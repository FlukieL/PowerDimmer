using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;

namespace PowerDimmer
{
    public sealed class HotkeyBinding
    {
        public bool Enabled { get; set; }
        public Key Key { get; set; }
        public ModifierKeys Modifiers { get; set; }
        public HotkeyBinding Clone() => new() { Enabled = Enabled, Key = Key, Modifiers = Modifiers };
        public string Display => $"{(Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl + " : "")}{(Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt + " : "")}{(Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift + " : "")}{(Modifiers.HasFlag(ModifierKeys.Windows) ? "Win + " : "")}{Key}";
    }

    public sealed class HotkeyService : IDisposable
    {
        public static readonly string[] Names = { "Toggle dimming", "Pin / unpin window", "Toggle window shade", "Custom shade area" };
        private static readonly HotkeyBinding[] Defaults =
        {
            new() { Enabled = true, Key = Key.D, Modifiers = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows },
            new() { Key = Key.D, Modifiers = ModifierKeys.Windows | ModifierKeys.Shift },
            new() { Key = Key.S, Modifiers = ModifierKeys.Windows | ModifierKeys.Alt },
            new() { Key = Key.A, Modifiers = ModifierKeys.Windows | ModifierKeys.Alt }
        };
        private readonly ISettings settings;
        private readonly HwndSource source;
        private readonly Action[] actions;
        private readonly HotkeyBinding[] bindings;
        private readonly int[] ids = new int[4];
        private int nextId = 100;
        public string StartupErrors { get; private set; } = "";

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        public HotkeyService(ISettings settings, Action[] actions)
        {
            this.settings = settings;
            this.actions = actions;
            if (actions.Length != Names.Length) throw new ArgumentException("Expected four hotkey actions.", nameof(actions));
            var parameters = new HwndSourceParameters("PowerDimmerHotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            source = new HwndSource(parameters);
            source.AddHook(WindowProc);
            bindings = Defaults.Select(b => b.Clone()).ToArray();
            try
            {
                var saved = JsonSerializer.Deserialize<HotkeyBinding[]>(settings.HotkeyBindings ?? "");
                if (saved != null && saved.Length == bindings.Length)
                    for (int i = 0; i < bindings.Length; i++)
                        if (Valid(saved[i])) bindings[i] = saved[i].Clone();
            }
            catch (JsonException) { }

            for (int i = 0; i < bindings.Length; i++)
            {
                if (!bindings[i].Enabled) continue;
                if (TryRegister(i, bindings[i], out var id))
                    ids[i] = id;
                else
                {
                    StartupErrors += $"{Names[i]}: {bindings[i].Display} could not be registered.\n";
                    bindings[i].Enabled = false;
                }
            }
        }

        public HotkeyBinding Get(int index) => bindings[index].Clone();

        public bool TryChange(int index, HotkeyBinding proposed, out string error)
        {
            error = "";
            if (!Valid(proposed))
            {
                error = "Choose a key with at least one modifier (Ctrl, Alt, Shift or Win).";
                return false;
            }
            for (int i = 0; i < bindings.Length; i++)
                if (i != index && proposed.Enabled && bindings[i].Enabled &&
                    bindings[i].Key == proposed.Key && bindings[i].Modifiers == proposed.Modifiers)
                {
                    error = "That shortcut is already used by another PowerDimmer action.";
                    return false;
                }

            var old = bindings[index];
            bool unchanged = ids[index] != 0 && proposed.Enabled &&
                old.Key == proposed.Key && old.Modifiers == proposed.Modifiers;
            int newId = 0;
            if (proposed.Enabled && !unchanged && !TryRegister(index, proposed, out newId))
            {
                error = "Windows could not register that shortcut. It may already be in use or reserved.";
                return false;
            }
            if (!unchanged && ids[index] != 0)
                UnregisterHotKey(source.Handle, ids[index]);
            if (!unchanged) ids[index] = newId;
            bindings[index] = proposed.Clone();
            settings.HotkeyBindings = JsonSerializer.Serialize(bindings);
            return true;
        }

        private static bool Valid(HotkeyBinding? b)
        {
            if (b == null || b.Modifiers == ModifierKeys.None ||
                (b.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)
                return false;
            int vk = KeyInterop.VirtualKeyFromKey(b.Key);
            return vk > 0 && vk <= 255 && b.Key != Key.LeftCtrl && b.Key != Key.RightCtrl &&
                b.Key != Key.LeftAlt && b.Key != Key.RightAlt && b.Key != Key.LeftShift &&
                b.Key != Key.RightShift && b.Key != Key.LWin && b.Key != Key.RWin &&
                b.Key != Key.System && b.Key != Key.ImeProcessed && b.Key != Key.DeadCharProcessed;
        }

        private bool TryRegister(int index, HotkeyBinding binding, out int id)
        {
            id = nextId++;
            return RegisterHotKey(source.Handle, id, (uint)binding.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(binding.Key));
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312)
            {
                int index = Array.IndexOf(ids, wParam.ToInt32());
                if (index >= 0) { handled = true; actions[index](); }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            foreach (int id in ids) if (id != 0) UnregisterHotKey(source.Handle, id);
            source.RemoveHook(WindowProc);
            source.Dispose();
        }
    }
}
