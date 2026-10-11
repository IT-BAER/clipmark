<p align="center">
  <img src="assets/logo/clipmark.svg" alt="Clipmark logo" width="96">
</p>

<h1 align="center">Clipmark</h1>

<p align="center">
  Screen capture, annotation and screen recording for Windows 11.<br>
  The Snipping Tool workflow with a real annotation editor, in a small Native AOT app.
</p>

![Clipmark editor](docs/images/editor.png)

## Features

**Capture**
- Rectangle, window, full screen, all monitors and freeform, on every monitor, including monitors left of or above the primary one
- Delay of 3, 5 or 10 seconds; global hotkey (Ctrl+Print Screen by default, or Print Screen itself)
- Optional copy to clipboard, automatic save and notification after each capture
- Text extractor: pick a region and its text goes straight to the clipboard

**Editor**
- Pen, highlighter, object eraser, rectangle, oval, line and arrow (Shift snaps to 45 degrees)
- Select, move, resize and rotate; arrows attach to shapes and follow them
- Text labels, eyedropper (copies `#RRGGBB`), fill for shapes and background
- Crop with handles or exact X / Y / W / H fields that accept arithmetic (`800+20`)
- Color ring, width presets, mouse wheel sets width or text size; undo / redo
- Save as PNG or JPEG, copy, share; "Save as editable PNG" keeps every object editable when reopened

**Text actions**
- OCR copy (Windows text recognition), quick redact of email addresses and phone numbers, QR code decoding

**Screen recording**
- Record a region, a window area or a full screen to MP4 (H.264 + AAC, 30 fps)
- System audio and microphone, mixed into one track
- Record bar with a 3 second countdown, kept out of the video

![Record bar](docs/images/record-bar.png)

**System**
- Lives in the tray, single instance, starts with Windows on request
- Toast notifications with banners; clicking one opens the capture while Clipmark runs

**Footprint** (measured on the development PC, median of 5 starts)

| | Clipmark 1.0.0 (self-contained zip) | Snipping Tool 11.2608 |
|---|---|---|
| Size on disk | 72 MB | 586 MB |
| Window visible after start | 328 ms | 359 ms |
| Idle private memory | 92 MB | 83 MB |

## Install

1. Download `Clipmark-<version>-win-x64.zip` from [Releases](https://github.com/IT-BAER/clipmark/releases).
2. Extract it to a folder of your choice, for example `%LOCALAPPDATA%\Programs\Clipmark`.
3. Run `Clipmark.App.exe`. It adds a "Clipmark" Start menu entry (needed for notification banners) and sits in the tray.

Requirements: Windows 11 x64 (build 22000 or later). No runtime installation is needed.

The build is not code-signed yet, so SmartScreen may warn on first start ("More info" > "Run anyway").

**Remove:** open Settings and turn off "Use the Print Screen key" and "Start Clipmark when Windows starts", or run
`Clipmark.App.exe --restore-system` once. This puts the Windows Print Screen option back, removes the autostart entry
and the Start menu shortcut. Then delete the folder. Settings live in `%LOCALAPPDATA%\Clipmark`.

## Usage

| Action | Default |
|---|---|
| New capture | Ctrl+Print Screen, tray icon click, or New |
| New recording | Ctrl+Shift+Print Screen, tray menu, or the camera button; the same hotkey stops it |
| Tools | V select, P pen, H highlighter, E eraser, R / C / L / A shapes, T text, I eyedropper, G fill |
| Editing | Ctrl+Z / Ctrl+Y, Ctrl+C copy, Ctrl+S save, Ctrl+Shift+S save editable, Ctrl+O open, Ctrl+N new |
| Selection | Arrow keys nudge (Shift = 10 px), Page Up / Down z-order, Del delete |

Recordings are saved to `Videos\Screen Recordings`. Screenshots go to `Pictures\Screenshots` (configurable) when
automatic save is on.

## Build from source

- .NET SDK 10, Windows SDK 10.0.26100
- For Native AOT publishing: Visual Studio Build Tools with the C++ workload (MSVC linker)

```powershell
dotnet test tests/Clipmark.Core.Tests
dotnet build src/Clipmark.App -p:Platform=x64 -p:WindowsPackageType=None
pwsh scripts/publish-release.ps1   # self-contained Native AOT zip in artifacts\release
```

`src/Clipmark.Core` holds the document model, geometry, editable file format, audio mixer and settings, fully unit
tested. `src/Clipmark.App` is the WinUI 3 app (Win2D canvas, GDI overlay, Windows.Graphics.Capture recorder, WASAPI).

## Clean-room note

Clipmark is an independent project and is not affiliated with Microsoft. It contains no code, assets or binaries from
Snipping Tool or Shotoshot. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the libraries it uses.

## License

[MIT](LICENSE)
