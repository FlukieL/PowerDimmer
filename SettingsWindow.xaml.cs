using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerDimmer
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettings _settings;
        private readonly DimmingExceptions _exceptions;
        private sealed record WindowItem(IntPtr Handle, uint ProcessId, string Title, string Executable)
        {
            public override string ToString() => $"{Title} ({(string.IsNullOrEmpty(Executable) ? "unknown application" : Executable)})";
        }
        private sealed record ExceptionItem(IntPtr Handle, string? Application, string Display)
        {
            public override string ToString() => Display;
        }
        private bool _exiting;
        private HotkeyService? _hotkeys;
        private bool _initialSizeApplied;
        private static readonly Brush ActiveSliderBrush = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
        private static readonly Brush InactiveSliderBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0x84, 0x9C));

        public SettingsWindow(ISettings settings, DimmingExceptions exceptions)
        {
            InitializeComponent();
            _settings = settings;
            _exceptions = exceptions;
            _exceptions.Changed += RefreshExceptionList;
            ContentRendered += (_, _) =>
            {
                if (!_initialSizeApplied)
                {
                    _initialSizeApplied = true;
                    SizeToAvailableScreen();
                }
            };
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) RefreshWindows();
            };
            RefreshExceptionList();
            DataContext = settings;
            VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(2) ?? "1.2"}";
            // Set initial status indicator state
            UpdateStatusIndicator(_settings.DimmingEnabled);

            // Keep status indicator in sync whenever DimmingEnabled changes
            // (fired from hotkeys, tray toggles, etc. — dispatched to UI thread)
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        public void AttachHotkeys(HotkeyService hotkeys)
        {
            _hotkeys = hotkeys;
            RefreshHotkeys();
            if (IsVisible)
                Dispatcher.BeginInvoke(new Action(SizeToAvailableScreen), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SizeToAvailableScreen()
        {
            UpdateLayout();
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            var workWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
            var workHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
            MinWidth = Math.Min(MinWidth, workWidth);
            MinHeight = Math.Min(MinHeight, workHeight);
            MaxWidth = workWidth;
            MaxHeight = workHeight;
            Width = Math.Min(Math.Max(Width, 860), workWidth);
            UpdateLayout();
            var scroll = (ScrollViewer)FindName("SettingsScroll");
            Height = Math.Min(workHeight, Math.Max(Height, ActualHeight + scroll.ExtentHeight - scroll.ViewportHeight + 24));
            UpdateLayout();

            // Window bounds and Screen.WorkingArea are both physical pixels.
            // Keep the expanded window on the selected monitor, including its taskbar.
            if (Win32.GetWindowRect(handle, out var bounds))
            {
                var work = screen.WorkingArea;
                int left = Math.Max(work.Left, Math.Min(bounds.Left, work.Right - (bounds.Right - bounds.Left)));
                int top = Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - (bounds.Bottom - bounds.Top)));
                Win32.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            }
        }

        private void RefreshHotkeys()
        {
            if (_hotkeys == null) return;
            HotkeyRows.Children.Clear();
            for (int i = 0; i < HotkeyService.Names.Length; i++)
            {
                int index = i;
                var binding = _hotkeys.Get(index);
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
                var edit = new Button { Content = binding.Display, MinWidth = 120, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Change shortcut", Style = (Style)FindResource("SmallBtn") };
                DockPanel.SetDock(edit, Dock.Right);
                edit.Click += (_, _) => EditHotkey(index);
                row.Children.Add(edit);
                var enabled = new CheckBox { Content = HotkeyService.Names[index], IsChecked = binding.Enabled, Style = (Style)FindResource("CB"), VerticalAlignment = VerticalAlignment.Center };
                enabled.Click += (_, _) =>
                {
                    var proposed = _hotkeys.Get(index);
                    proposed.Enabled = enabled.IsChecked == true;
                    if (!_hotkeys.TryChange(index, proposed, out var error))
                        MessageBox.Show(this, error, "Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RefreshHotkeys();
                };
                row.Children.Add(enabled);
                HotkeyRows.Children.Add(row);
            }
        }

        private void EditHotkey(int index)
        {
            if (_hotkeys == null) return;
            var binding = _hotkeys.Get(index);
            var editor = new Window
            {
                Title = "Edit shortcut", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                Background = (Brush)new BrushConverter().ConvertFromString("#1E1E2E")!,
                FontFamily = FontFamily
            };
            var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 300 };
            panel.Children.Add(new TextBlock { Text = HotkeyService.Names[index], Foreground = Brushes.White, FontSize = 15, Margin = new Thickness(0, 0, 0, 12) });
            var modifiers = new WrapPanel();
            CheckBox Modifier(string label, ModifierKeys flag)
            {
                var box = new CheckBox { Content = label, IsChecked = (binding.Modifiers & flag) != 0, Style = (Style)FindResource("CB"), Margin = new Thickness(0, 0, 12, 0) };
                modifiers.Children.Add(box);
                return box;
            }
            var ctrl = Modifier("Ctrl", ModifierKeys.Control);
            var alt = Modifier("Alt", ModifierKeys.Alt);
            var shift = Modifier("Shift", ModifierKeys.Shift);
            var win = Modifier("Win", ModifierKeys.Windows);
            panel.Children.Add(modifiers);
            var keys = Enumerable.Range((int)Key.A, 26).Select(x => (Key)x)
                .Concat(Enumerable.Range((int)Key.D0, 10).Select(x => (Key)x))
                .Concat(Enumerable.Range((int)Key.F1, 24).Select(x => (Key)x))
                .Concat(new[] { Key.Space, Key.Tab, Key.Enter, Key.Insert, Key.Delete, Key.Home, Key.End, Key.PageUp, Key.PageDown, Key.Up, Key.Down, Key.Left, Key.Right })
                .ToList();
            if (!keys.Contains(binding.Key)) keys.Add(binding.Key);
            var keySelector = new ComboBox { ItemsSource = keys, SelectedItem = binding.Key, Margin = new Thickness(0, 12, 0, 12), MinHeight = 28 };
            panel.Children.Add(keySelector);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", Style = (Style)FindResource("SmallBtn"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var save = new Button { Content = "Save", Style = (Style)FindResource("SmallBtn"), IsDefault = true };
            cancel.Click += (_, _) => editor.Close();
            save.Click += (_, _) =>
            {
                var proposed = binding.Clone();
                proposed.Key = (Key)keySelector.SelectedItem;
                proposed.Modifiers = (ctrl.IsChecked == true ? ModifierKeys.Control : 0) |
                    (alt.IsChecked == true ? ModifierKeys.Alt : 0) |
                    (shift.IsChecked == true ? ModifierKeys.Shift : 0) |
                    (win.IsChecked == true ? ModifierKeys.Windows : 0);
                if (!_hotkeys.TryChange(index, proposed, out var error))
                {
                    MessageBox.Show(editor, error, "Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                editor.Close();
                RefreshHotkeys();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            panel.Children.Add(buttons);
            editor.Content = panel;
            editor.ShowDialog();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Config.Net may report either CLR property names or aliases.
                UpdateStatusIndicator(_settings.DimmingEnabled);

                // Force all WPF bindings to re-read from the ISettings source.
                // Necessary because Config.Net may fire PropertyChanged with the
                // alias name (camelCase) rather than the CLR property name, which
                // WPF binding doesn't recognise. Cycling DataContext is the
                // simplest way to make every binding pick up the latest value
                // regardless of the event name used.
                var ctx = DataContext;
                DataContext = null;
                DataContext = ctx;
            });
        }

        private void RefreshWindows()
        {
            var selected = (OpenWindows.SelectedItem as WindowItem)?.Handle;
            var items = Win32.GetOpenWindows().Select(hwnd =>
            {
                var title = Win32.GetWindowTitle(hwnd);
                var executable = Path.GetFileName(Win32.GetProcessPath(hwnd));
                return new WindowItem(hwnd, Win32.GetProcessId(hwnd), string.IsNullOrWhiteSpace(title) ? "(Untitled)" : title, executable);
            }).ToList();
            OpenWindows.ItemsSource = items;
            OpenWindows.SelectedItem = items.FirstOrDefault(item => item.Handle == selected);
        }

        private void RefreshExceptionList()
        {
            var items = _exceptions.Applications.Select(name => new ExceptionItem(IntPtr.Zero, name, $"Application: {name}")).ToList();
            items.AddRange(_exceptions.Windows.Select(hwnd =>
                new ExceptionItem(hwnd, null, $"Window: {Win32.GetWindowTitle(hwnd)} (0x{hwnd.ToInt64():X})")));
            ExceptionList.ItemsSource = items;
        }

        private void RefreshWindows_Click(object sender, RoutedEventArgs e) => RefreshWindows();

        private bool IsCurrentWindow(WindowItem item)
        {
            if (Win32.IsWindow(item.Handle) && item.ProcessId != 0 &&
                Win32.GetProcessId(item.Handle) == item.ProcessId)
                return true;

            MessageBox.Show("This window is no longer available. Refresh the open windows list and try again.",
                "Window exception", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshWindows();
            return false;
        }

        private void AddWindowException_Click(object sender, RoutedEventArgs e)
        {
            if (OpenWindows.SelectedItem is WindowItem item && IsCurrentWindow(item))
                _exceptions.AddWindow(item.Handle);
        }

        private void AddWindowApplication_Click(object sender, RoutedEventArgs e)
        {
            if (OpenWindows.SelectedItem is WindowItem item && IsCurrentWindow(item))
                AddApplication(item.Executable);
        }

        private void AddApplication_Click(object sender, RoutedEventArgs e) => AddApplication(ApplicationName.Text);

        private void AddApplication(string name)
        {
            if (!_exceptions.AddApplication(name))
            {
                MessageBox.Show("Enter a valid executable name such as notepad.exe. Some protected applications cannot be identified.",
                    "Application exception", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ApplicationName.Clear();
        }

        private void RemoveException_Click(object sender, RoutedEventArgs e)
        {
            if (ExceptionList.SelectedItem is not ExceptionItem item) return;
            if (item.Application != null) _exceptions.RemoveApplication(item.Application);
            else _exceptions.RemoveWindow(item.Handle);
        }

        private void UpdateStatusIndicator(bool dimmingOn)
        {
            StatusText.Text = dimmingOn ? "Dimming on" : "Dimming off";
            BrightnessSlider.Foreground = dimmingOn ? ActiveSliderBrush : InactiveSliderBrush;
        }

        private void BrightnessSlider_UserInteraction(object? sender, EventArgs e)
        {
            if (!_settings.DimmingEnabled)
                _settings.DimmingEnabled = true;
        }

        private void GitHubLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/FlukieL/PowerDimmer") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "Could not open the browser. Visit https://github.com/FlukieL/PowerDimmer",
                    "Open GitHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _exiting = true;
            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            // Minimize to tray: hide instead of minimizing to taskbar
            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_settings.CloseToTray && !_exiting)
            {
                // Hide to tray rather than destroying the window
                e.Cancel = true;
                Hide();
            }
            else
            {
                // Let the window close and shut down the application
                e.Cancel = false;
                if (!_exiting)
                {
                    _exiting = true;
                    Application.Current.Shutdown();
                }
            }
            base.OnClosing(e);
        }
    }
}
