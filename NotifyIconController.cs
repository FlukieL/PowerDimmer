using System;
using System.Drawing;
using System.Windows.Forms;
using ModernNotifyIcon.Theme;
using System.Diagnostics;

namespace PowerDimmer
{
    public class NotifyIconController
    {
        internal Action? ExitClicked;
        internal Action? OpenSettingsClicked;
        public NotifyIcon NotifyIcon;

        public NotifyIconController(ISettings settings)
        {
            NotifyIcon = NotifyIconBuilder
                .Create()
                .Configure(builder => builder
                    // ── Quick launch ──────────────────────────────────────────
                    .AddButton(option => option
                        .SetText("⚙  Open Settings")
                        .AddHandler(() => OpenSettingsClicked?.Invoke()))
                    .AddSeparator()
                    // ── Dimming ───────────────────────────────────────────────
                    .AddToggle(option => option
                        .SetText("Dimming active")
                        .SetChecked(settings.DimmingEnabled)
                        .ConfigureItem(item =>
                        {
                            item.ShortcutKeyDisplayString = "Ctrl+Win+Alt+D";
                            settings.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(settings.DimmingEnabled))
                                    item.Checked = settings.DimmingEnabled;
                            };
                        })
                        .AddHandler(b => settings.DimmingEnabled = b))
                    .AddToggle(option => option
                        .SetText("Dim taskbar")
                        .SetChecked(settings.DimTaskbar)
                        .ConfigureItem(item =>
                        {
                            settings.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(settings.DimTaskbar))
                                    item.Checked = settings.DimTaskbar;
                            };
                        })
                        .AddHandler(b => settings.DimTaskbar = b))
                    .AddToggle(option => option
                        .SetText("Dim all monitors")
                        .SetChecked(settings.MultiMonitorDimming)
                        .ConfigureItem(item =>
                        {
                            settings.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(settings.MultiMonitorDimming))
                                    item.Checked = settings.MultiMonitorDimming;
                            };
                        })
                        .AddHandler(b => settings.MultiMonitorDimming = b))
                    .AddToggle(option => option
                        .SetText("Undim on desktop click")
                        .SetChecked(settings.UndimOnDesktop)
                        .ConfigureItem(item =>
                        {
                            settings.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(settings.UndimOnDesktop))
                                    item.Checked = settings.UndimOnDesktop;
                            };
                        })
                        .AddHandler(b => settings.UndimOnDesktop = b))
                    .AddSeparator()
                    // ── Brightness ────────────────────────────────────────────
                    .AddItem(new TrackBarMenuItem(settings))
                    .AddSeparator()
                    // ── Startup ───────────────────────────────────────────────
                    .AddToggle(option => option
                        .SetText("Active on launch")
                        .SetChecked(settings.ActiveOnLaunch)
                        .ConfigureItem(item =>
                        {
                            settings.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName == nameof(settings.ActiveOnLaunch))
                                    item.Checked = settings.ActiveOnLaunch;
                            };
                        })
                        .AddHandler(b => settings.ActiveOnLaunch = b))
                    .AddSeparator()
                    // ── App ───────────────────────────────────────────────────
                    .AddButton(option => option
                        .SetText("E&xit")
                        .AddHandler(() => ExitClicked?.Invoke())))
                .Build(Icon.ExtractAssociatedIcon(
                    Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule!.FileName)!);

            NotifyIcon.Text = "PowerDimmer";
            NotifyIcon.Visible = true;
        }
    }

    // https://stackoverflow.com/a/24825487
    public class TrackBarWithoutFocus : TrackBar
    {
        private const int WM_SETFOCUS = 0x0007;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SETFOCUS) return;
            base.WndProc(ref m);
        }
    }

    // https://stackoverflow.com/questions/4339143/adding-a-trackbar-control-to-a-contextmenu
    public class TrackBarMenuItem : ToolStripControlHost
    {
        public TrackBarMenuItem(ISettings settings) : base(new ContainerControl())
        {
            BackColor = ThemeDictionary.ChromeMidium;

            var brightnessLabel = new Label
            {
                Parent  = Control,
                Text    = "Brightness",
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var trackBar = new TrackBarWithoutFocus
            {
                Parent       = Control,
                Top          = 22,
                Minimum      = 0,
                Maximum      = 100,
                TickFrequency = 1,
                SmallChange  = 5,
                LargeChange  = 20,
                TickStyle    = TickStyle.None,
                Value        = settings.Brightness,
            };
            // Restore hover-highlights after interacting with trackbar
            trackBar.Click += (_, _) => Parent?.Focus();

            var valueBox = new TextBox
            {
                Parent      = trackBar,
                Top         = 28,
                Left        = 1,
                Enabled     = false,
                BackColor   = ThemeDictionary.ChromeMidium,
                TextAlign   = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.None,
                Text        = settings.Brightness.ToString(),
            };

            // Tray → Settings
            trackBar.ValueChanged += (_, _) =>
            {
                settings.Brightness = trackBar.Value;
                valueBox.Text = trackBar.Value.ToString();
            };

            // Settings (GUI) → Tray: refresh slider from current settings value
            // every time the context menu is opened.  BeginInvoke on an
            // unshown WinForms control is unreliable (handle not yet created),
            // so VisibleChanged is the simplest cross-thread-safe hook.
            trackBar.VisibleChanged += (_, _) =>
            {
                if (trackBar.Visible && trackBar.Value != settings.Brightness)
                {
                    trackBar.Value = settings.Brightness;
                    valueBox.Text  = settings.Brightness.ToString();
                }
            };
        }
    }
}
