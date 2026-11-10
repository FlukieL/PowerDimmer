using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PowerDimmer
{
    /// <summary>
    /// A transparent, click-through overlay that covers only the title-bar
    /// (caption) area of a target window, dimming it to match the background.
    /// Tracks the target window as it moves or resizes.
    /// </summary>
    public partial class TitleBarShade : Window
    {
        public IntPtr Handle;
        private readonly IntPtr _targetHandle;

        // Each shade owns its hook and keeps its callback alive until it is unhooked.
        private Win32.WinEventDelegate? _eventMovedDelegate;
        private GCHandle _gcSafetyHandle;
        private IntPtr _eventHook;
        private Win32.RECT? _lastBounds;

        public IntPtr TargetHandle => _targetHandle;

        public TitleBarShade(IntPtr targetHandle)
        {
            _targetHandle = targetHandle;

            ShowInTaskbar = false;
            AllowsTransparency = true;
            Background = Brushes.Black;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // Start 1×1; the real position is set in OnSourceInitialized
            // using Win32 coordinates to avoid WPF DPI translation issues.
            Width = 1;
            Height = 1;

            if (_targetHandle != IntPtr.Zero)
            {
                _eventMovedDelegate = new Win32.WinEventDelegate(WinEventMovedProc);
                _gcSafetyHandle = GCHandle.Alloc(_eventMovedDelegate);

                uint pid = Win32.GetProcessId(_targetHandle);
                uint targetThreadId = Win32.GetWindowThreadProcessId(_targetHandle, IntPtr.Zero);
                _eventHook = Win32.SetWinEventHook(
                    (uint)Win32.SWEH_Events.EVENT_OBJECT_LOCATIONCHANGE,
                    (uint)Win32.SWEH_Events.EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero, _eventMovedDelegate, pid, targetThreadId,
                    Win32.WINEVENT_OUTOFCONTEXT);
            }
        }

        // Called by the OS whenever the target window moves or resizes.
        public void WinEventMovedProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != _targetHandle || idObject != 0 || idChild != 0 || Handle == IntPtr.Zero)
                return;

            var r = Win32.GetTitleBarRect(_targetHandle);
            if (_lastBounds is Win32.RECT previous &&
                previous.Left == r.Left && previous.Top == r.Top &&
                previous.Right == r.Right && previous.Bottom == r.Bottom)
                return;

            if (Win32.MoveWindow(Handle, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, false) != 0)
                _lastBounds = r;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            Handle = new WindowInteropHelper(this).EnsureHandle();

            // Make the overlay layered, transparent to input, and non-activating.
            var exStyle = Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE,
                exStyle | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE);

            // Position over the title bar immediately (physical pixels, DPI-safe)
            // and place as TOPMOST right away so the shade is never below fgHwnd
            // even before UpdateDimming runs.
            if (_targetHandle != IntPtr.Zero)
            {
                var r = Win32.GetTitleBarRect(_targetHandle);
                if (Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST,
                    r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                    Win32.SWP_NOACTIVATE))
                    _lastBounds = r;
            }

            Win32.ShowWindow(Handle, Win32.SW_SHOWNORMAL);
            base.OnSourceInitialized(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!e.Cancel)
            {
                if (_eventHook != IntPtr.Zero)
                    Win32.UnhookWinEvent(_eventHook);

                if (_gcSafetyHandle.IsAllocated)
                    _gcSafetyHandle.Free();
            }
        }
    }
}
