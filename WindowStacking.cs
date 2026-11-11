using System;
using System.Collections.Generic;

namespace PowerDimmer
{
    internal static class WindowStacking
    {
        public static void Apply(IntPtr foreground, IEnumerable<IntPtr> windows,
            Func<IntPtr, bool> isException, IEnumerable<IntPtr> overlays)
        {
            if (foreground == IntPtr.Zero || !Win32.IsWindow(foreground))
                return;

            const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE |
                Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER;

            Win32.SetWindowPos(foreground, Win32.HWND_TOP, 0, 0, 0, 0, flags);
            var anchor = (Win32.GetWindowLong(foreground, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0
                ? Win32.HWND_TOP : foreground;

            foreach (var hwnd in windows)
            {
                if (hwnd == foreground || !Win32.IsWindow(hwnd) || Win32.IsIconic(hwnd) ||
                    (Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0 ||
                    !isException(hwnd))
                    continue;

                if (Win32.SetWindowPos(hwnd, anchor, 0, 0, 0, 0, flags))
                    anchor = hwnd;
            }

            foreach (var overlay in overlays)
                Win32.SetWindowPos(overlay, anchor, 0, 0, 0, 0, flags);
        }
    }
}
