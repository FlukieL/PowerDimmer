using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerDimmer
{
    public static partial class Win32
    {

        // from:
        //   https://github.com/microsoft/PowerToys/blob/fa3a5f80a113568155d9c2dbbcea8af16e15afa1/src/common/utils/process_path.h#L10
        public static String GetProcessPath(uint pid)
        {
            var process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, true, pid);
            var name = new StringBuilder();
            if (process != INVALID_HANDLE_VALUE)
            {
                name.Capacity = MAX_PATH;
                var nameLength = name.Capacity;

                if (QueryFullProcessImageName(process, 0, name, ref nameLength) == false)
                {
                    nameLength = 0;
                }
                name.Clear();
                name.Capacity = 0;
                CloseHandle(process);
            }
            return name.ToString();
        }

        // from:
        //   https://github.com/microsoft/PowerToys/blob/fa3a5f80a113568155d9c2dbbcea8af16e15afa1/src/common/utils/process_path.h#L29
        public static String GetProcessPath(IntPtr window)
        {
            var appFrameHost = "ApplicationFrameHost.exe";
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            var name = GetProcessPath(pid);

            // TODO: debug this substring
            if (name.Length >= appFrameHost.Length &&
                appFrameHost == name.Substring(name.Length - appFrameHost.Length, appFrameHost.Length))
            {
                // It is a UWP app. We will enumerate the windows and look for one created
                // by something with a different PID
                var newPid = pid;

                EnumChildWindows(window, (childWindow, _) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(childWindow, out pid);
                    if (pid != newPid)
                    {
                        newPid = pid;
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }, 0);

                // If we have a new pid, get the new name.
                if (newPid != pid)
                {
                    return GetProcessPath(newPid);
                }
            }

            return name;
        }

        // from:
        //   https://github.com/microsoft/PowerToys/blob/fa3a5f80a113568155d9c2dbbcea8af16e15afa1/src/common/utils/window.h#L36
        public static bool IsSystemWindow(IntPtr window, String className)
        {
            // We compare the HWND against HWND of the desktop and shell windows,
            // we also filter out some window class names know to belong to the taskbar.
            var systemClasses = new string[] { "SysListView32", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman" };
            var systemHwnds = new IntPtr[] { GetDesktopWindow(), GetShellWindow() };
            foreach (var systemHwnd in systemHwnds)
            {
                if (window == systemHwnd)
                {
                    return true;
                }
            }
            foreach (var systemClass in systemClasses)
            {
                if (className == systemClass)
                {
                    return true;
                }
            }
            return false;
        }

        // from:
        //   https://github.com/microsoft/PowerToys/blob/7d0304fd06939d9f552e75be9c830db22f8ff9e2/src/modules/fancyzones/FancyZonesLib/util.cpp#L383
        public static bool HasNoVisibleOwner(IntPtr window)
        {
            var owner = GetWindow(window, GW_OWNER);
            // TODO: debug this
            if (owner == IntPtr.Zero)
            {
                return true; // There is no owner at all
            }
            if (!IsWindowVisible(owner))
            {
                return true; // Owner is invisible
            }
            RECT rect;
            if (!GetWindowRect(owner, out rect))
            {
                return false; // Could not get the rect, return true (and filter out the window) just in case
            }
            // It is enough that the window is zero-sized in one dimension only.
            return rect.Top == rect.Bottom || rect.Left == rect.Right;
        }


        // from:
        //   https://github.com/microsoft/PowerToys/blob/7d0304fd06939d9f552e75be9c830db22f8ff9e2/src/modules/fancyzones/FancyZonesLib/util.cpp#L403
        public static bool IsStandardWindow(IntPtr window)
        {
            if (GetAncestor(window, GA_ROOT) != window || !IsWindowVisible(window))
            {
                return false;
            }

            var style = GetWindowLong(window, GWL_STYLE);
            var exStyle = GetWindowLong(window, GWL_EXSTYLE);
            // WS_POPUP need to have a border or minimize/maximize buttons,
            // otherwise the window is "not interesting"
            if ((style & WS_POPUP) == WS_POPUP &&
                (style & WS_THICKFRAME) == 0 &&
                (style & WS_MINIMIZEBOX) == 0 &&
                (style & WS_MAXIMIZEBOX) == 0)
            {
                return false;
            }
            if ((style & WS_CHILD) == WS_CHILD ||
                (style & WS_DISABLED) == WS_DISABLED ||
                (exStyle & WS_EX_TOOLWINDOW) == WS_EX_TOOLWINDOW ||
                (exStyle & WS_EX_NOACTIVATE) == WS_EX_NOACTIVATE)
            {
                return false;
            }
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            if (IsSystemWindow(window, className.ToString()))
            {
                return false;
            }
            var processPath = GetProcessPath(window);
            if (className.ToString() == "Windows.UI.Core.CoreWindow" &&
            processPath.EndsWith("SearchUI.exe"))
            {
                return false;
            }

            return true;
        }
        
        public static uint GetProcessId(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);

            return pid;
        }

        public static bool IsDesktopWindow(IntPtr window)
        {
            if (window == GetDesktopWindow() || window == GetShellWindow())
                return true;
            var className = new StringBuilder(256);
            GetClassName(window, className, className.Capacity);
            var name = className.ToString();
            return name == "Progman" || name == "WorkerW";
        }

        public static Win32.RECT GetWindowRectangle(IntPtr hWnd)
        {
            Win32.DwmGetWindowAttribute(hWnd,
                Win32.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                out Win32.RECT rect, Marshal.SizeOf<Win32.RECT>());
            return rect;
        }

        /// <summary>
        /// Returns the screen-space bounding rect of the title bar (caption area)
        /// for <paramref name="hWnd"/>.
        /// Uses <c>DWMWA_CAPTION_BUTTON_BOUNDS</c> (window-relative) to get the
        /// actual rendered caption height, with a system-metrics fallback.
        /// </summary>
        public static Win32.RECT GetTitleBarRect(IntPtr hWnd)
        {
            // Visible (DWM) frame rect – no invisible glass margins.
            Win32.DwmGetWindowAttribute(hWnd,
                Win32.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                out Win32.RECT frame, Marshal.SizeOf<Win32.RECT>());

            // Full window rect including DWM invisible margins (origin for
            // window-relative coordinates returned by DWM attributes).
            Win32.GetWindowRect(hWnd, out Win32.RECT windowRect);

            // Caption button bounds in window-relative coordinates.
            // DWMWA_CAPTION_BUTTON_BOUNDS = 5 (already in the enum).
            Win32.DwmGetWindowAttribute(hWnd,
                Win32.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_BUTTON_BOUNDS,
                out Win32.RECT captionBounds, Marshal.SizeOf<Win32.RECT>());

            // Convert to screen coords: captionBounds is relative to windowRect origin.
            // Add 1 pixel to include the bottom border of the caption area which
            // DWMWA_CAPTION_BUTTON_BOUNDS does not account for.
            int titleBarBottom = windowRect.Top + captionBounds.Bottom + 1;

            // Fallback when DWMWA_CAPTION_BUTTON_BOUNDS returns a zero rect
            // (non-DWM windows, tool windows, etc.).
            if (titleBarBottom <= frame.Top)
            {
                int captionHeight = Win32.GetSystemMetrics(Win32.SM_CYCAPTION);
                var style = GetWindowLong(hWnd, Win32.GWL_STYLE);
                bool hasThickFrame = (style & (int)Win32.WS_THICKFRAME) != 0;
                int frameHeight = hasThickFrame ? Win32.GetSystemMetrics(Win32.SM_CYSIZEFRAME) : 0;
                titleBarBottom = frame.Top + captionHeight + frameHeight;
            }

            return new Win32.RECT
            {
                Left   = frame.Left,
                Top    = frame.Top,
                Right  = frame.Right,
                Bottom = titleBarBottom,
            };
        }
    }
}
