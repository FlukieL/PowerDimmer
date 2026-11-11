using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerDimmer
{
    public sealed class DimmingExceptions
    {
        private readonly ISettings settings;
        private readonly Dictionary<IntPtr, uint> windows = new();
        private readonly HashSet<string> applications;

        public event Action? Changed;

        public DimmingExceptions(ISettings settings)
        {
            this.settings = settings;
            applications = new HashSet<string>(
                (settings.ExcludedApplications ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        public bool HasApplicationRules => applications.Count != 0;
        public bool HasExceptions => applications.Count != 0 || windows.Count != 0;
        public IEnumerable<string> Applications => applications.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        public IEnumerable<IntPtr> Windows => windows.Keys.Where(IsWindowException).ToArray();

        public bool IsWindowException(IntPtr hwnd) =>
            windows.TryGetValue(hwnd, out var pid) && Win32.IsWindow(hwnd) && Win32.GetProcessId(hwnd) == pid;

        public bool IsApplicationException(IntPtr hwnd)
        {
            if (applications.Count == 0) return false;
            var path = Win32.GetProcessPath(hwnd);
            return !string.IsNullOrEmpty(path) && applications.Contains(Path.GetFileName(path));
        }

        public bool IsException(IntPtr hwnd) => IsWindowException(hwnd) || IsApplicationException(hwnd);

        public void AddWindow(IntPtr hwnd)
        {
            if (!Win32.IsWindow(hwnd)) return;
            var pid = Win32.GetProcessId(hwnd);
            if (pid == 0) return;
            windows[hwnd] = pid;
            Changed?.Invoke();
        }

        public void RemoveWindow(IntPtr hwnd)
        {
            if (windows.Remove(hwnd)) Changed?.Invoke();
        }

        public void ToggleWindow(IntPtr hwnd)
        {
            if (IsWindowException(hwnd)) RemoveWindow(hwnd);
            else AddWindow(hwnd);
        }

        public bool AddApplication(string value)
        {
            var name = Path.GetFileName(value.Trim());
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('|'))
                return false;
            if (applications.Add(name))
            {
                SaveApplications();
                Changed?.Invoke();
            }
            return true;
        }

        public void RemoveApplication(string name)
        {
            if (applications.Remove(name))
            {
                SaveApplications();
                Changed?.Invoke();
            }
        }

        private void SaveApplications() => settings.ExcludedApplications = string.Join("|", Applications);
    }
}
