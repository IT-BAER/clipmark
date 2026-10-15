<p align="center">
  <img src="assets/logo/clipmark.svg" alt="Clipmark logo" width="96">
</p>

<h1 align="center">Clipmark</h1>

<p align="center">
  <b>Snipping Tool on steroids.</b><br>
  Screen capture, annotation and screen recording for Windows 11, in a small Native AOT app.<br>
  <sub>Clipmark is an independent project and is not affiliated with Microsoft. Snipping Tool is a trademark of Microsoft.</sub>
</p>

![Clipmark editor](docs/images/editor.png)

## Features

Items marked **(Premium)** need a license key after the 30-day trial. See [Free and Premium](#free-and-premium).

**Capture**
- Rectangle, window, full screen (the monitor you click), all monitors and freeform, on every monitor, including
  monitors left of or above the primary one
- Delay of 3, 5 or 10 seconds; global hotkey (Ctrl+Print Screen by default, or Print Screen itself)
- Optional copy to clipboard, automatic save, notification and a border (color, width) on each capture
- Color picker in the capture overlay: a loupe shows the pixels, a click copies the color as HEX, RGB or HSL
- Quick markup (Ctrl+E): draw with a red pen on the frozen screen before you pick the region; the strokes stay
  editable in the editor
- Text extractor: pick a region and its text goes straight to the clipboard, optionally without line breaks

**Editor**
- Pen, highlighter, object eraser with Erase all mark-ups, rectangle, oval, line and arrow (Shift snaps to 45 degrees)
- Ruler: drag it, turn it with the mouse wheel in 15 degree steps; pen and highlighter strokes that start at its
  edge run straight along it
- Select, move, resize and rotate any object at any time; arrows attach to shapes and follow them **(Premium)**
- Text labels, eyedropper (copies HEX, RGB or HSL), fill for shapes and the background **(Premium)**
- Crop with handles or exact X / Y / W / H fields that accept arithmetic (`800+20`)
- Color ring, opacity 10-100 %, width presets, mouse wheel sets width or text size; undo / redo
- Zoom 100%, in, out or fit to window (Ctrl+0, Ctrl+plus, Ctrl+minus)
- Save as PNG or JPEG, copy (optionally every edit goes to the clipboard at once), share, print, Edit in Paint, open
  the screenshots folder
- Opens PNG, JPEG, JFIF, BMP, DIB, GIF, TIFF and ICO files
- "Save as editable PNG" keeps every object editable when you reopen the file **(Premium)**. Any copy of Clipmark opens
  editable PNGs.

**Capture history (Premium, off by default)**
- Turn it on in Settings > "Keep a capture history". The History button (clock icon) in the toolbar then shows your
  last 25 captures (up to 250 MB) as thumbnails
- Click a thumbnail to reopen the capture with every object still editable; your edits are saved back to it
- Right-click a thumbnail to delete it, or use Clear all; turning the setting off deletes the whole history
- Stored only on this PC in `%LOCALAPPDATA%\Clipmark\History`, encrypted for your Windows account (DPAPI)

**Text actions**
- OCR copy: drag across the recognized text or double-click a word to copy part of it (Ctrl+C, right-click or
  Copy selection), or copy all of it. The built-in offline OCR engine reads Latin-script languages; other languages
  use Windows text recognition
- Copy as table: a table in the capture goes to the clipboard as a table for Word, Outlook and Excel, and as
  tab-separated text for everything else
- Quick redact of email addresses and / or phone numbers; Remove all redactions
- QR code decoding: Copy and Open link; Call and Send email for phone numbers and email addresses

**Send to AI (Premium)**
- Pastes the capture into a running Claude Code or Codex CLI session, the Claude or ChatGPT desktop app, or the Claude
  or Codex extension in VS Code
- In Windows Terminal, Clipmark names the agent's tab and asks you to switch to it instead of typing into another tab

**Ask AI (Premium, with your own Anthropic API key)**
- Ask Claude questions about the capture in a flyout next to the toolbar; answers stream in with formatting, and you
  can stop an answer, ask follow-up questions or start a new chat
- Uses your own Anthropic API key, stored in Windows Credential Manager; pick Claude Sonnet 5.5, Opus 5.5 or Haiku 5.5
  in Settings; the answer depth (low, med, high or xhigh: how much Claude reasons before it answers) is set in the
  chat header or in Settings > Ask AI
- Sends the capture as shown (redactions burned in) and your question to api.anthropic.com only when you send a
  question, after a one-time consent; nothing goes to IT-BAER

**Screen recording**
- Record a region, a window area or a full screen to MP4 (H.264 + AAC, 30 fps)
- System audio and microphone, mixed into one track; mute either one while recording
- Record bar with a 3 second countdown and Pause / Resume, kept out of the video

![Record bar](docs/images/record-bar.png)

**System**
- Lives in the tray (shown on the taskbar the first time it runs), single instance, starts with Windows on request
- Toast notifications with banners; clicking one opens the capture while Clipmark runs
- App theme: Windows default, Light or Dark
- 16 interface languages, see [Languages](#languages)

## Clipmark vs Snipping Tool

The Snipping Tool column was tested on Snipping Tool 11.2608.41.0 (Windows 11 build 26300) in October 2026 and checked
against Microsoft's [Snipping Tool help page](https://support.microsoft.com/en-us/windows/apps/use-snipping-tool-to-capture-screenshots).

| | Clipmark | Snipping Tool |
|---|---|---|
| Capture modes | Rectangle, window, full screen (one monitor), all monitors, freeform | Rectangle, window, full screen (all monitors as one image), freeform |
| Capture delay | 3, 5 or 10 seconds | 3, 5 or 10 seconds |
| Text extractor (region text straight to the clipboard) | Yes | Yes |
| Pen, highlighter, eraser | Yes | Yes |
| Shapes | Rectangle, oval, line, arrow | Rectangle, oval, line, arrow, emoji |
| Shape fill | Yes, also fills the background (Premium) | Yes, fill and outline color |
| Move, resize, rotate shapes | Any object, any time (Premium) | Only the shape you just drew |
| Arrows that attach to shapes and follow them | Yes (Premium) | No |
| Text labels | Yes, editable later (Premium) | No text tool (Edit in Paint) |
| Opacity | 10-100 % | 1-100 % |
| Ruler | Yes | Yes |
| Color picker | In the capture overlay with a loupe, HEX / RGB / HSL; eyedropper in the editor (Premium) | In the capture overlay with a loupe, HEX / RGB / HSL |
| Quick markup before the capture (Ctrl+E) | Yes | Yes |
| Zoom, print, Edit in Paint | Yes | Yes |
| Border on each capture | Yes | Yes |
| Reopen a capture with editable objects | Yes, editable PNG (Premium to save) | No |
| OCR copy and quick redact | Yes | Yes |
| OCR character errors on 60 rendered web pages (our benchmark) | 0.89% | 1.07% |
| Copy recognized text as a table | Yes | Yes |
| QR code decoding (links, phone numbers, email addresses, plain text) | Yes, with Copy, Open link, Call and Send email | Yes, as a chip on the code |
| Screen recording | Region, window area or full screen; MP4 with system audio and microphone; pause, mute while recording | Rectangle or window; MP4 with system audio and microphone; pause, mute while recording; edit in Clipchamp |
| Send the capture to an AI agent | Yes (Premium) | No |
| Perfect screenshot (automatic fit to content) | No | Copilot+ PCs only |

**Footprint** (measured on the development PC, median of 5 starts)

| | Clipmark 1.0.0 (self-contained zip) | Snipping Tool 11.2608 |
|---|---|---|
| Size on disk | 72 MB | 586 MB |
| Window visible after start | 328 ms | 359 ms |
| Idle private memory | 92 MB | 83 MB |

## Free and Premium

The source code is public under the [Elastic License 2.0](LICENSE): you may read, build and change it, but not remove or
work around the license key check. Clipmark 1.0.0 was MIT licensed and has no license check: every feature is free.

From the next release on, a new install starts a 30-day Premium trial. After the trial, these features need a license
key: select / move / resize / rotate, attached arrows, text labels, fill, eyedropper, saving editable PNGs, Send to AI,
Ask AI and the capture history. Everything else, including capture, recording, OCR, crop and saving PNG or JPEG, stays free.
You enter the key in Settings. The key works offline and contains no name and no email address.

## Install

1. Download `Clipmark-<version>-win-x64.zip` from [Releases](https://github.com/IT-BAER/clipmark/releases).
2. Extract it to a folder of your choice, for example `%LOCALAPPDATA%\Programs\Clipmark`.
3. Run `Clipmark.App.exe`. It adds a "Clipmark" Start menu entry (needed for notification banners) and sits in the tray.

Requirements: Windows 11 x64 (build 22000 or later). No runtime installation is needed.

The build is not code-signed yet, so SmartScreen may warn on first start ("More info" > "Run anyway").

**Remove**
1. Run `Clipmark.App.exe --restore-system` once, or turn off "Use the Print Screen key" and "Start Clipmark when
   Windows starts" in Settings. This puts the Windows Print Screen option back and removes the autostart entry; the
   command line option also removes the Start menu shortcut and deletes the Ask AI key (Credential Manager entry
   `Clipmark/AnthropicKey`, also removed by "Remove key" in Settings > Ask AI).
2. Delete the program folder and `%LOCALAPPDATA%\Clipmark` (settings, license state `license.json`, capture history).
3. Optional: free this PC's license seat with "Remove from this PC" in Settings > License before uninstalling.
4. Optional: delete the installation key `Clipmark.releash.install` (per Windows user; it holds the trial and the seat
   of this PC; "Remove from this PC" keeps it): `certutil -csp "Microsoft Platform Crypto Provider" -delkey -user
   Clipmark.releash.install` (TPM) or `certutil -csp "Microsoft Software Key Storage Provider" -delkey -user
   Clipmark.releash.install`. A new key starts as a new installation.
5. Optional: older versions left a trial record in `HKCU\Software\Clipmark` and in Windows Credential Manager
   ("Windows Credentials", entry `Clipmark/Trial`); Clipmark deletes both at its first licence server answer.

Your screenshots and recordings stay where you saved them.

## Usage

| Action | Default |
|---|---|
| New capture | Ctrl+Print Screen, tray icon click, or New; Ctrl+E in the capture overlay toggles Quick markup |
| New recording | Ctrl+Shift+Print Screen, tray menu, or the camera button; the same hotkey or a tray icon click stops it |
| Tools | V select, P pen, H highlighter, E eraser, R / C / L / A shapes, T text, I eyedropper, G fill |
| Editing | Ctrl+Z / Ctrl+Y, Ctrl+C copy, Ctrl+S save, Ctrl+Shift+S save editable, Ctrl+O open, Ctrl+N new |
| Zoom | Ctrl+0 100%, Ctrl+Plus / Ctrl+Minus zoom in / out, Ctrl+mouse wheel |
| Selection | Ctrl+A select all, arrow keys nudge (Shift = 10 px), Page Up / Down z-order (Shift = to front / back), Del delete, Esc deselect |

Recordings are saved to `Videos\Screen Recordings`. Screenshots go to `Pictures\Screenshots` (configurable) when
automatic save is on.

## Languages

Clipmark speaks English, German, French, Italian, Spanish, Portuguese (Brazil), Japanese, Korean, Chinese (Simplified
and Traditional), Dutch, Polish, Russian, Swedish, Turkish and Ukrainian. It follows the Windows display language;
Settings > Language picks another one after a restart. Dates and numbers follow your Windows regional format.

The German text is reviewed by a native speaker. The other languages are machine-translated against Microsoft's
Windows terminology and may contain mistakes. To fix one, edit the `<value>` in
`src/Clipmark.App/Strings/<language>/Resources.resw` and open a pull request; keep the key names and placeholders
such as `{0}` unchanged.

## Build from source

- .NET SDK 10, Windows SDK 10.0.26100
- For Native AOT publishing: Visual Studio Build Tools with the C++ workload (MSVC linker)

```powershell
dotnet test tests/Clipmark.Core.Tests
dotnet build src/Clipmark.App -p:Platform=x64 -p:WindowsPackageType=None
pwsh scripts/publish-release.ps1   # self-contained Native AOT zip in artifacts\release
```

`src/Clipmark.Core` holds the document model, geometry, editable file format, audio mixer, licensing, history store
and settings, fully unit tested. `src/Clipmark.App` is the WinUI 3 app (Win2D canvas, GDI overlay,
Windows.Graphics.Capture recorder, WASAPI).

## Privacy

Clipmark has no telemetry. It connects to the network only for Ask AI: when you send a question, the capture and the
question go to api.anthropic.com under your own API key. Send to AI pastes into an app you already run, and that app
decides what it uploads. See the [privacy policy](docs/privacy-policy.md).

## Disclaimer

Clipmark is an independent project and is not affiliated with Microsoft. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the libraries it uses.

## License

[Elastic License 2.0](LICENSE). Versions up to and including commit 31d560a, among them release 1.0.0, were published
under the MIT License.
