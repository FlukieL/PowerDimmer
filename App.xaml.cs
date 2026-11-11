using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Config.Net;

namespace PowerDimmer
{
    public partial class App : Application
    {
        private IntPtr curFgHwnd;
        private bool dimmingPaused;
        private ISettings settings;
        private List<DimWindow> dimWindows { get; } = new();
        private List<WindowShade> shadeWindows { get; } = new();
        private DimmingExceptions exceptions = null!;
        private TitleBarShade? titleBarShade;
        static Func<int, double> brightnessToOpacity = (b) => 1 - (b / 100.0);

        static GCHandle GCSafetyHandleForActive;
        static GCHandle GCSafetyHandleForClose;
        private IntPtr foregroundHook;
        private IntPtr closeHook;
        private IntPtr showHook;
        private Win32.WinEventDelegate? showCallback;
        private HotkeyService? hotkeys;

        public App()
        {
            // Surface any unhandled exception as a message box so the app never
            // silently disappears.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.Windows.MessageBox.Show(
                    e.ExceptionObject?.ToString(),
                    "PowerDimmer – Unhandled Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            };
            DispatcherUnhandledException += (s, e) =>
            {
                System.Windows.MessageBox.Show(
                    e.Exception?.ToString(),
                    "PowerDimmer – Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                e.Handled = true;
            };

            // Store settings.json in %APPDATA%\PowerDimmer\ so it always
            // lives on a local NTFS volume. Config.Net.Json uses File.Replace()
            // for atomic writes, which fails on network/virtual/mapped drives.
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PowerDimmer");
            System.IO.Directory.CreateDirectory(appDataDir);
            var settingsPath = System.IO.Path.Combine(appDataDir, "settings.json");

            // Config.Net.Json uses File.Replace() for atomic writes, which
            // requires the destination file to already exist. Pre-create it
            // with an empty JSON object so the first write doesn't throw.
            if (!System.IO.File.Exists(settingsPath))
            {
                System.IO.File.WriteAllText(settingsPath, "{}");
            }

            settings = new ConfigurationBuilder<ISettings>().UseJsonFile(settingsPath).Build();
            exceptions = new DimmingExceptions(settings);
            exceptions.Changed += RefreshExceptions;
            settings.DimmingEnabled = settings.ActiveOnLaunch;
            settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(settings.Brightness))
                {
                    dimWindows.ForEach(w => w.Opacity = brightnessToOpacity(settings.Brightness));
                }
                else if (e.PropertyName == nameof(settings.DimmingEnabled))
                {
                    if (settings.DimmingEnabled)
                    {
                        dimOn(curFgHwnd);
                    }
                    else
                    {
                        dimOff();
                    }
                }
                else if (e.PropertyName == nameof(settings.MultiMonitorDimming))
                {
                    if (settings.DimmingEnabled)
                    {
                        dimOff();
                        dimOn(curFgHwnd);
                    }
                }
                else if (e.PropertyName == nameof(settings.DimTitleBar))
                {
                    if (settings.DimmingEnabled)
                    {
                        if (settings.DimTitleBar)
                            ShowTitleBarShade(curFgHwnd);
                        else
                            HideTitleBarShade();
                    }
                }
            };
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            curFgHwnd = Win32.GetForegroundWindow();

            var settingsWindow = new SettingsWindow(settings, exceptions);
            // Show the window on launch unless the user has opted to start minimized
            if (!settings.StartMinimized)
            {
                settingsWindow.Show();
            }

            var iconController = new NotifyIconController(settings);
            iconController.ExitClicked += () => Shutdown();
            iconController.OpenSettingsClicked += () =>
            {
                settingsWindow.Show();
                settingsWindow.Activate();
            };
            // Also open settings on double-click of the tray icon
            iconController.NotifyIcon.DoubleClick += (_, _) =>
            {
                settingsWindow.Show();
                settingsWindow.Activate();
            };
            Exit += (e, s) =>
            {
                iconController.NotifyIcon.Visible = false;
                iconController.NotifyIcon.Icon.Dispose();
                iconController.NotifyIcon.Dispose();
            };

            var shortcutActions = new List<Action>();
            shortcutActions.Add(() =>
            {
                settings.DimmingEnabled = !settings.DimmingEnabled;
            });

            shortcutActions.Add(() =>
            {
                if (!settings.DimmingEnabled)
                {
                    return;
                }

                exceptions.ToggleWindow(Win32.GetForegroundWindow());
            });

            shortcutActions.Add(() =>
            {
                if (!settings.WindowShadeEnabled)
                {
                    return;
                }

                //unshade window if exists
                var hwnd = Win32.GetForegroundWindow();
                var shadedWindow = shadeWindows.SingleOrDefault(w => w.TargetHandle == hwnd);
                if (shadedWindow != null)
                {
                    shadedWindow.Close();
                    shadeWindows.Remove(shadedWindow);
                }
                else
                {
                    var opacity = brightnessToOpacity(settings.Brightness);
                    var shade = new WindowShade(hwnd)
                    {
                        Opacity = opacity
                    };
                    shade.Show();
                    shadeWindows.Add(shade);
                }
            });

            shortcutActions.Add(() =>
            {
                if (!settings.WindowShadeEnabled)
                {
                    return;
                }

                var hwnd = Win32.GetForegroundWindow();
                var shadedWindow = shadeWindows.SingleOrDefault(w => w.TargetHandle == hwnd);
                if (shadedWindow != null)
                {
                    shadedWindow.Close();
                    shadeWindows.Remove(shadedWindow);
                }
                var opacity = brightnessToOpacity(settings.Brightness);
                var customShadeCreatedDelegate = new Win32.CustomShadeCreatedEventDelegate(CreatedCustomShadeEventProc);
                var customShade = new CustomShadeTool(hwnd, customShadeCreatedDelegate);
                customShade.Show();
            });

            hotkeys = new HotkeyService(settings, shortcutActions.ToArray());
            settingsWindow.AttachHotkeys(hotkeys);
            if (!string.IsNullOrEmpty(hotkeys.StartupErrors))
                MessageBox.Show(hotkeys.StartupErrors, "Hotkey registration", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (settings.ActiveOnLaunch)
            {
                dimOn(curFgHwnd);
            }

            var eventDelegate = new Win32.WinEventDelegate(WinEventProc);
            GCSafetyHandleForActive = GCHandle.Alloc(eventDelegate);
            foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
                                  IntPtr.Zero, eventDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

            var eventClosedDelegate = new Win32.WinEventDelegate(WinCloseEventProc);
            GCSafetyHandleForClose = GCHandle.Alloc(eventClosedDelegate);
            closeHook = Win32.SetWinEventHook(Win32.SWEH_Events.EVENT_OBJECT_DESTROY, Win32.SWEH_Events.EVENT_OBJECT_DESTROY,
                                  IntPtr.Zero, eventClosedDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

            showCallback = WinShowEventProc;
            showHook = Win32.SetWinEventHook((uint)Win32.SWEH_Events.EVENT_OBJECT_SHOW,
                (uint)Win32.SWEH_Events.EVENT_OBJECT_SHOW, IntPtr.Zero, showCallback, 0, 0,
                (uint)Win32.SWEH_dwFlags.WINEVENT_SKIPOWNPROCESS);
        }

        private void dimOn(IntPtr fgHwnd)//creates a dim window on each screen
        {
            var opacity = brightnessToOpacity(settings.Brightness);
            var screens = settings.MultiMonitorDimming
                ? System.Windows.Forms.Screen.AllScreens
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
            foreach (var screen in screens)
            {
                var win = new DimWindow(settings, screen.Bounds)
                {
                    Opacity = opacity
                };
                win.Show();
                dimWindows.Add(win);
            }

            if (settings.DimTitleBar)
                ShowTitleBarShade(fgHwnd);

            UpdateDimming(fgHwnd);
        }

        private void dimOff()
        {
            dimmingPaused = false;
            HideTitleBarShade();
            dimWindows.ForEach(w => w.Close());
            dimWindows.Clear();
            // the following maintains the proper
            // foreground window upon disabling
            Win32.SetForegroundWindow(curFgHwnd);
        }

        private void ShowTitleBarShade(IntPtr fgHwnd)
        {
            if (fgHwnd == IntPtr.Zero || exceptions.IsException(fgHwnd))
            {
                HideTitleBarShade();
                return;
            }

            // If the shade is already tracking this exact window, leave it
            // in place — no need to destroy/recreate, which would cause a
            // visible flash (e.g. EVENT_SYSTEM_FOREGROUND firing on a title-bar
            // click of the already-focused window).
            if (titleBarShade != null && titleBarShade.TargetHandle == fgHwnd)
                return;

            var opacity = brightnessToOpacity(settings.Brightness);
            // Show the new shade BEFORE closing the old one so there is no
            // visible gap between the two during window transitions.
            var next = new TitleBarShade(fgHwnd) { Opacity = opacity };
            next.Show();
            titleBarShade?.Close();
            titleBarShade = next;
        }

        private void HideTitleBarShade()
        {
            if (titleBarShade != null)
            {
                titleBarShade.Close();
                titleBarShade = null;
            }
        }

        // https://stackoverflow.com/questions/4372055/detect-active-window-changed-using-c-sharp-without-polling/10280800#10280800
        // https://docs.microsoft.com/en-us/windows/win32/winauto/event-constants
        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != Win32.GetForegroundWindow())
                return;

            if (settings.DimmingEnabled)
            {
                if (Win32.IsStandardWindow(hwnd))
                {
                    curFgHwnd = hwnd;
                    if (dimmingPaused)
                    {
                        dimmingPaused = false;
                        dimWindows.ForEach(w => w.Show());
                    }
                    if (settings.DimTitleBar)
                        ShowTitleBarShade(hwnd);
                    UpdateDimming(hwnd);
                }
                else if (settings.UndimOnDesktop && Win32.IsDesktopWindow(hwnd))
                {
                    dimmingPaused = true;
                    dimWindows.ForEach(w => w.Hide());
                    HideTitleBarShade();
                }
            }
            if (settings.WindowShadeEnabled && shadeWindows.Count != 0)
            {
                var whwnd = Win32.GetForegroundWindow();
                WindowShade windowShade = shadeWindows.SingleOrDefault(s => s.TargetHandle == whwnd);
                if (windowShade != null)
                {
                    UpdateShade(whwnd, windowShade);
                }
            }
        }
        public void WinCloseEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Accessibility object destruction is raised system-wide, often at high
            // frequency. Only react to an actual window being destroyed.
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0)
            {
                return;
            }

            exceptions.RemoveWindow(hwnd);
            if (shadeWindows.Count != 0)
            {
                WindowShade windowShade = shadeWindows.SingleOrDefault(s => s.TargetHandle == hwnd);
                if(windowShade != null)
                {
                    windowShade.Close();
                    shadeWindows.Remove(windowShade);
                }
            }

            if (settings.DimmingEnabled && settings.DimTaskbar && hwnd == curFgHwnd)
            {
                IntPtr taskbarHwnd = Win32.FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd != IntPtr.Zero)
                {
                    Win32.SetWindowPos(taskbarHwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                }
            }
        }

        private void WinShowEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0 ||
                !settings.DimmingEnabled || dimmingPaused || !exceptions.HasApplicationRules)
                return;

            if (Win32.IsStandardWindow(hwnd) && exceptions.IsApplicationException(hwnd))
                Dispatcher.BeginInvoke(new Action(RefreshExceptions));
        }

        private void RefreshExceptions()
        {
            if (!settings.DimmingEnabled || dimmingPaused || dimWindows.Count == 0)
                return;

            var foreground = Win32.GetForegroundWindow();
            if (foreground == IntPtr.Zero || !Win32.IsWindow(foreground))
                return;

            if (settings.DimTitleBar)
                ShowTitleBarShade(foreground);
            UpdateDimming(foreground);
        }

        private void UpdateDimming(IntPtr fgHwnd)
        {
            // Place the title-bar shade as TOPMOST so the OS can never raise
            // fgHwnd above it (e.g. on title-bar click). The shade covers only
            // the caption area and has WS_EX_TRANSPARENT + WS_EX_NOACTIVATE so
            // all mouse input passes through to fgHwnd unobstructed.
            if (titleBarShade != null)
            {
                Win32.SetWindowPos(titleBarShade.Handle, Win32.HWND_TOPMOST,
                    0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }

            // Preserve the relative z-order of exceptions behind the foreground.
            var windows = exceptions.HasExceptions ? Win32.GetOpenWindows() : new List<IntPtr>();
            WindowStacking.Apply(fgHwnd, windows, exceptions.IsException, dimWindows.Select(w => w.Handle));

            if(settings.DimTaskbar)
            {
                IntPtr taskbarHwnd = Win32.FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd != IntPtr.Zero)
                {
                    Win32.SetWindowPos(taskbarHwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                }
            }
        }

        private async void UpdateShade(IntPtr shadedHwnd, WindowShade windowShade)
        {
            // Set the window shade handle as TOP...
            Win32.SetWindowPos(windowShade.Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            // Allow the target window to settle without blocking a thread-pool thread.
            await Task.Delay(500);
            await Dispatcher.InvokeAsync(() =>
            {
                if (shadeWindows.Contains(windowShade))
                {
                    Win32.SetWindowPos(windowShade.Handle, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                }
            });
        }

        public void CreatedCustomShadeEventProc(Window shadeTool, IntPtr hwnd, double left, double top, double width, double height)
        {
            var shadedWindow = shadeWindows.SingleOrDefault(w => w.TargetHandle == hwnd);
            if (shadedWindow != null)
            {
                shadedWindow.Close();
                shadeWindows.Remove(shadedWindow);
            }
            else
            {
                var opacity = brightnessToOpacity(settings.Brightness);
                var shade = new WindowShade(hwnd, left, top, width, height)
                {
                    Opacity = opacity
                };
                shade.Show();
                shadeWindows.Add(shade);
            }
            shadeTool.Close();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            hotkeys?.Dispose();
            base.OnExit(e);
            if (foregroundHook != IntPtr.Zero)
            {
                Win32.UnhookWinEvent(foregroundHook);
            }
            if (closeHook != IntPtr.Zero)
            {
                Win32.UnhookWinEvent(closeHook);
            }
            if (showHook != IntPtr.Zero)
            {
                Win32.UnhookWinEvent(showHook);
            }
            if(GCSafetyHandleForActive.IsAllocated)
            {
                GCSafetyHandleForActive.Free();
            }
            if(GCSafetyHandleForClose.IsAllocated)
            {
                GCSafetyHandleForClose.Free();
            }
        }
    }

    public interface ISettings : INotifyPropertyChanged
    {
        [Option(Alias = "activeOnLaunch", DefaultValue = true)]
        bool ActiveOnLaunch { get; set; }

        [Option(Alias = "dimmingEnabled", DefaultValue = false)]
        bool DimmingEnabled { get; set; }

        [Option(Alias = "dimTaskbar", DefaultValue = true)]
        bool DimTaskbar { get; set; }

        [Option(Alias = "dimTitleBar", DefaultValue = false)]
        bool DimTitleBar { get; set; }

        [Option(Alias = "undimOnDesktop", DefaultValue = false)]
        bool UndimOnDesktop { get; set; }

        [Option(Alias = "multiMonitorDimming", DefaultValue = true)]
        bool MultiMonitorDimming { get; set; }

        [Option(Alias = "hotkeyBindings", DefaultValue = "")]
        string HotkeyBindings { get; set; }

        [Option(Alias = "excludedApplications", DefaultValue = "")]
        string ExcludedApplications { get; set; }

        [Option(Alias = "brightness", DefaultValue = 50)]
        int Brightness { get; set; }

        [Option(Alias = "windowShadeEnabled", DefaultValue = true)]
        bool WindowShadeEnabled { get; set; }

        [Option(Alias = "startMinimized", DefaultValue = false)]
        bool StartMinimized { get; set; }

        [Option(Alias = "minimizeToTray", DefaultValue = true)]
        bool MinimizeToTray { get; set; }

        [Option(Alias = "closeToTray", DefaultValue = true)]
        bool CloseToTray { get; set; }
    }
}
