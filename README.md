# ShowLang V.2

ShowLang is a lightweight Windows tray utility that briefly displays the active keyboard language near the current text caret whenever the input language changes.

The current native implementation replaces the original AutoHotkey script and is built with .NET 8 Windows Forms, Win32 APIs, MSAA, and UI Automation.

## Features

- Detects the keyboard layout of the focused input thread.
- Displays `TH`, `EN`, `JP`, or a language identifier near the text caret.
- Polls only the inexpensive foreground keyboard layout while idle; it does not scan or cache caret positions in the background.
- Captures the caret only after an actual language change: Win32 first, then one MSAA/UI Automation request through an isolated worker.
- Keeps a preloaded worker asleep between requests so modern controls remain fast without continuous accessibility activity.
- Coalesces rapid layout switches and restarts only the worker if an accessibility provider times out.
- Shows the overlay at the lower-right corner of the active monitor when no text caret is available.
- Supports modern text surfaces such as Windows Terminal, Raycast, Electron, and WebView-based apps when they expose accessibility information.
- Corrects Chromium-style address bars that expose the caret at the field's left edge instead of its real text position.
- Scales the caret gap together with the selected overlay size.
- Does not steal focus and allows mouse clicks to pass through the overlay.
- Includes tray controls for **Show test**, **Pause**, **Resume**, and **Exit**.
- Supports multiple overlay sizes.
- Supports box-only transparency while keeping the language text fully opaque.
- Stores appearance settings between launches.
- Includes special placement logic for Windows Search so the overlay is not covered by the search panel.

## Requirements

- Windows 10 or Windows 11, x64
- .NET 8 Desktop Runtime for the default framework-dependent build (not required for the portable build)
- .NET 8 SDK to build from source

## Build and run

Open PowerShell in the repository root and run:

```powershell
.\build.ps1 -Run
```

The published executable is written to:

```text
app\ShowLang.exe
```

The `app` directory is intentionally excluded from Git because it contains generated binaries.

For a PC without the .NET 8 Desktop Runtime, create a self-contained build:

```powershell
.\scripts\publish-portable.ps1
```

The portable files are written to `dist\portable-win-x64`. Copy the **entire folder** when deploying it; copying only `ShowLang.exe` omits native WPF and UI Automation dependencies.

## Start with Windows

Install or refresh the startup entry with:

```powershell
.\scripts\install-startup.ps1
```

Remove only this project's startup entry with:

```powershell
.\scripts\remove-startup.ps1
```

## Tray controls

Right-click the ShowLang tray icon to access:

- **Show test** — display the current language immediately.
- **Pause / Resume** — stop or restart monitoring without closing the tray process.
- **Appearance → Size** — choose a preset overlay scale.
- **Appearance → Box transparency** — adjust only the background and border alpha.
- **Reset appearance** — restore the default size and opaque box.
- **Exit** — close ShowLang.

## Settings and logs

Per-user files are kept outside the repository:

```text
%LOCALAPPDATA%\ShowLang\settings.json
%LOCALAPPDATA%\ShowLang\showlang.log
```

## Project layout

```text
src\ShowLang\          Current application source
tools\UiaProbeRunner\ UI Automation diagnostic helper
tools\diagnostics\    One-off caret and terminal investigation scripts
legacy\                Original AutoHotkey implementation
scripts\               Build/startup maintenance helpers
app\                   Locally published executable, ignored by Git
.archive\              Local migration backups, ignored by Git
```

## Compatibility notes

A normal topmost window generally works over desktop applications and windowed or borderless games. Exclusive fullscreen, protected rendering surfaces, elevated applications, and anti-cheat systems may still prevent overlays or accessibility queries.

Pause stops language monitoring, caret queries, and overlay rendering, but it does not hide the ShowLang process from Task Manager or other process-list checks.

## Recovered history

The repository history and tags reconstruct the available local snapshots from the original AutoHotkey version through the native caret, pause/resume, Windows Search positioning, appearance controls, and box-only transparency updates.
