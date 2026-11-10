# PowerDimmer (Fork)

A simple distraction dimmer for Windows. Dims entire screen except for active/focused window.

| [<img src="https://user-images.githubusercontent.com/79330/147771591-853256ae-f4f1-42d3-8c68-ea467febeb58.png" width="800" />](https://user-images.githubusercontent.com/79330/147771591-853256ae-f4f1-42d3-8c68-ea467febeb58.png) |
| :--: |
| *Dim everything but focused window* |


> **[⬇ Download PowerDimmer](https://github.com/FlukieL/PowerDimmer/releases)** — Windows 10/11 x64. The portable ZIP is self-contained; the smaller ZIP requires the .NET 10 Desktop Runtime.

## **About this fork**
This is a fork of [shayne/PowerDimmer](https://github.com/shayne/PowerDimmer). The original had a bug where dimming didn't work on external monitors in mixed-DPI setups ([issue #4](https://github.com/shayne/PowerDimmer/issues/4)), and I wanted a couple of extra features. No commits have been made to the original repo in 2+ years, so I fixed and extended it (and by that I mean Claude fixed and extended it :-). Just did this for personal use. Not tested on any PCs except my own and haven't checked if the changes affect resource usage compared to the original, so YMMV.

### **Changes from the original**
- Fixed multi-monitor dimming on mixed-DPI setups (uses Win32 pixel coordinates instead of WPF DIPs)
- Added "Undim on desktop click" option — click the desktop wallpaper to temporarily remove all dimming
- Added "Dim all monitors?" toggle — optionally limit dimming to the primary monitor only
- Upgraded to .NET 10 (LTS); shade trackers filter unrelated accessibility events and avoid redundant overlay moves
- **Added a settings GUI** — a dark-themed WPF window accessible from the tray icon or by double-clicking it (see below)
- **Added "Dim title bar of focused window"** — applies a semi-transparent dark overlay to just the caption/title-bar area of the currently focused window, so it blends visually with the dimmed background rather than standing out at full brightness. Follows the window as it moves or resizes. Note: may not align correctly on apps with non-standard or custom-drawn title bars (e.g. borderless, Electron, or heavily themed windows).

---

## Settings GUI

A dedicated settings window replaces the need to dig through the tray context menu for every option. Open it by:

- **Right-clicking** the system tray icon → **⚙ Open Settings**
- **Double-clicking** the system tray icon

The window provides:

| Section | Controls |
|---|---|
| **Dimming** | Toggle dimming on/off, dim taskbar, dim title bar of focused window, dim all monitors, undim on desktop click |
| **Brightness** | Slider (0 – 100 %) with live readout |
| **Window Shade** | Enable/disable the window shade feature |
| **Startup** | Active on launch toggle |
| **Hotkeys** | Quick-reference for all keyboard shortcuts |

All settings are saved immediately to `%APPDATA%\PowerDimmer\settings.json` (i.e. `C:\Users\<you>\AppData\Roaming\PowerDimmer\settings.json`). This location is used because Config.Net performs atomic file writes using `File.Replace()`, which requires a local NTFS volume — storing settings next to the exe would fail if it's run from a mapped or network drive.

---

## **Original README**

### Features

* Dims all but currently focused window
* Toggle dimming for a specific window via `Win + Shift + D`
* Adjust brightness level from the system tray context menu or the Settings window
* Toggle shade for a specific window via `Win + Alt + S`, useful for that one bright screen without dark mode
* Shade an area of a window via `Win + Alt + A` then select the area to shade. The shade will move with the window

| [<img src="https://user-images.githubusercontent.com/79330/147770555-5efe9efc-88e1-438e-a559-47b5f495976b.png" width="800" />](https://user-images.githubusercontent.com/79330/147770555-5efe9efc-88e1-438e-a559-47b5f495976b.png) |
| :--: |
| *Multiple focused windows via dimming toggle hotkey* |

### Hotkeys

| Action | Shortcut |
|---|---|
| Toggle dimming | `Ctrl + Win + Alt + D` |
| Pin / unpin window (keep undimmed) | `Win + Shift + D` |
| Toggle window shade | `Win + Alt + S` |
| Custom shade area | `Win + Alt + A` |

---

## Building & Running

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then:

```bash
# Run directly
dotnet run

# Or produce a self-contained single-file executable (no .NET runtime needed on target)
dotnet publish /p:PublishProfile=SingleFileExe
# Output: publish\PowerDimmer.exe
```

The publish profile (`Properties/PublishProfiles/SingleFileExe.pubxml`) creates a compressed, self-contained `win-x64` executable — just copy `publish\PowerDimmer.exe` to a supported Windows 10/11 x64 machine and run it. The framework-dependent release requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
