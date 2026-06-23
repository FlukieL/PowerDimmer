using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PowerDimmer
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettings _settings;

        private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0xA6, 0xE3, 0xA1));
        private static readonly SolidColorBrush GrayBrush  = new(Color.FromRgb(0x45, 0x47, 0x5A));

        public SettingsWindow(ISettings settings)
        {
            InitializeComponent();
            _settings = settings;
            DataContext = settings;

            // Set initial status indicator state
            UpdateStatusIndicator(_settings.DimmingEnabled);

            // Keep status indicator in sync whenever DimmingEnabled changes
            // (fired from hotkeys, tray toggles, etc. — dispatched to UI thread)
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Always refresh the status indicator when dimming state changes
                if (e.PropertyName == nameof(ISettings.DimmingEnabled))
                {
                    UpdateStatusIndicator(_settings.DimmingEnabled);
                }

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

        private void UpdateStatusIndicator(bool dimmingOn)
        {
            StatusDot.Fill  = dimmingOn ? GreenBrush : GrayBrush;
            StatusText.Text = dimmingOn ? "Dimming on"  : "Dimming off";
            StatusText.Foreground = dimmingOn ? GreenBrush : GrayBrush;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
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
            if (_settings.CloseToTray)
            {
                // Hide to tray rather than destroying the window
                e.Cancel = true;
                Hide();
            }
            else
            {
                // Let the window close and shut down the application
                e.Cancel = false;
                Application.Current.Shutdown();
            }
        }
    }
}
