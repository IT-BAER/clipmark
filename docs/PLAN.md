# Snipping-Tool-style capture app with Shotoshot-class tools (plan)

Name: Clipmark (repo D:VSCipmark). Stack: C# / WinUI 3 (Windows App SDK 2.x), Win2D for drawing.
Target: Windows 11 x64 (arm64 later). Public release, own code, clean-room.

## Clean-room rules (legal)
- No decompiling, disassembling or extracting anything from Snipping Tool (Microsoft.ScreenSketch) or Shotoshot binaries.
- No copying of Shotoshot source (GPL-3.0). Feature list from its README is a reference for *ideas*, which are not copyrightable.
- Reference material = screenshots of observed behaviour (`ref/` in workspace) + Microsoft public docs.
- Icons: Fluent System Icons (MIT, Microsoft) or Segoe Fluent Icons font that ships with Windows. No Snipping Tool app icon, no "Snipping" in the name.

## Observed reference UX (Snipping Tool 11.2607.23.0, this PC)
- Main window toolbar, left to right: New | Photo / Video toggle | Mode dropdown | Delay dropdown || Pen | Highlighter | Eraser | Shapes || Crop | Text actions || Undo | Redo || Edit in Paint | Save | Copy | Share | More (...).
- Capture overlay: frozen, dimmed screen on all monitors; floating pill top-center: Photo / Video | Mode (rect, window, full screen, freeform) | Text extractor | Close.
- Microsoft docs: Text actions (OCR copy, redact, QR), color picker (Copilot+ only), shapes (rect, oval, line, arrow), recording (Win+Shift+R, region/window, mic + system audio, MP4).

## Feature set
Snipping Tool parity:
1. Capture modes: rectangle, window, full screen (one monitor), all monitors, freeform.
2. Delay 0/3/5/10 s. Global hotkey (default Print Screen; Win+Shift+S is reserved by Windows).
3. After capture: copy to clipboard + optional auto-save + toast; editor window opens.
4. Editor window with Snipping-style toolbar; crop; undo/redo; save PNG/JPEG; copy; share (Windows share sheet).
5. Text actions: OCR copy (Windows.Media.Ocr), quick redact (emails/phones by regex on OCR boxes), QR decode.
6. Screen recording: region/window, MP4 (H.264), optional mic + system audio.
Shotoshot-class tools (own implementation, in the editor):
7. Select (V) with multi-select, rubber band, move/resize/rotate handles, arrow keys nudge, Page Up/Down z-order, Del.
8. Rectangle (R), ellipse (C), line (L), arrow (A) with 45° snap and arrow ends that attach to shapes and follow them.
9. Pen (P), highlighter (H), text (T, editable later), object eraser (E).
10. Eyedropper (I) with pixel loupe + hex to clipboard; bucket fill (G) for shapes/background; color wheel + 6 quick colors.
11. Mouse wheel = stroke width / text size. Exact-size crop fields with arithmetic (`800+20`).
12. Editable file format: PNG with an embedded private chunk (JSON objects + base image); opens back with live objects.
Deferred / decision needed: Shotoshot Board (desktop notes), Presentation mode (key overlay, temp shapes), Google Lens upload (unofficial endpoint).

## Architecture
- `Clipmark.Core` (net10.0 class library, no UI): document model (objects, styles, z-order, attachments), hit-testing, geometry, undo/redo command stack, editable-file serializer. Fully unit-tested (xUnit).
- `Clipmark.App` (WinUI 3): tray icon, hotkey (RegisterHotKey), capture service (Windows.Graphics.Capture or DXGI duplication for frozen frame, per-monitor DPI), overlay windows (one borderless window per monitor showing the frozen frame), editor window (Win2D CanvasControl), OCR, share, settings.
- `Clipmark.Recorder`: Windows.Graphics.Capture -> Media Foundation sink writer (H.264/AAC), WASAPI loopback for system audio.
- Packaging: MSIX via Microsoft Store (framework-dependent on Windows App SDK runtime, Store signs) + unpackaged zip/installer for GitHub.

## Milestones (each ends with a demo + tests green)
- M0 Spike (go/no-go on "lightweight"): empty WinUI 3 app, framework-dependent + Native AOT. Measure package size, cold start, idle RAM vs Snipping Tool. Hotkey + frozen capture of both monitors at mixed DPI.
- M1 Capture: overlay + 5 modes + delay + clipboard + auto-save + toast + tray + settings.
- M2 Editor base: window, toolbar, Win2D canvas, pen/highlighter/eraser/shapes/crop/undo, save/copy/share.
- M3 Shotoshot-class tools: select/transform/rotate, attached arrows, text, eyedropper, bucket, color wheel, wheel sizing, exact size, z-order.
- M4 Editable format + text actions (OCR, redact, QR).
- M5 Recording.
- M6 Release: Print Screen takeover with restore on uninstall, autostart, MSIX/Store listing, GitHub release, signing for the unpackaged build.

## Toolchain needed
- Installed (measured): .NET SDK 10.0.112 and the "WinUI Blank App" template (`dotnet new winui`).
- Windows App SDK 2.x NuGet, Win2D NuGet, xUnit.
- Native AOT publish needs the MSVC linker (VS C++ Build Tools), which is NOT installed (no link.exe, no Windows SDK found).
- Visual Studio 2022/2026 with "WinUI application development" workload, or Build Tools + Windows SDK for MSIX packaging (several GB, UAC).

## Risks
- "Lightweight": WinUI 3 self-contained is tens of MB; framework-dependent + AOT is much smaller but depends on the Windows App SDK runtime. M0 measures this before more work.
- Mixed-DPI multi-monitor overlay and capture are the classic bug source (this PC has 2 monitors).
- Recording with system audio is the largest single feature; keep it last.
- QR decode needs a library (e.g. ZXing.Net, Apache-2.0) or Windows API if one exists; decide in M4.
- Store review / SmartScreen for unsigned GitHub builds.

## Rollback
New repo only; nothing on the system changes until M6 (hotkey takeover). Shotoshot stays installed and untouched.

## Decisions (11 October 2026)
- Approved: install VS Build Tools 2026 (C++ + Windows SDK), start with M0.
- v1 scope: no Board, no Presentation mode, no Google Lens.
- Release: Microsoft Store MSIX + GitHub zip.
- Name: Clipmark. Known overlap: a small "ClipMark" screenshot/clip organizer exists (huntscreens.com listing); check Store name reservation before release.

## Ledger
### M0 Spike
- [x] Build Tools 18.10.2 installed; MSVC 14.51 `link.exe` + Windows SDK 10.0.26100. ILCompiler needs the VS Installer folder on PATH (vswhere), set in scripts/publish-m0.ps1
- [x] Core: VirtualScreen crop math, 3 tests green (negative origins, vertical offset, out-of-range)
- [x] Spike: hotkey (Ctrl+Shift+F8) + frozen GDI capture; 2 monitors 2560x1440 at x=0 and x=-2560 captured, capture 72-86 ms, +PNG 324-375 ms. Mixed DPI NOT testable here (both monitors same scale)
- [x] Variant A (AOT, unpackaged, WinAppSDK self-contained): 65.38 MB shipped (no pdb), window 446 ms median, idle WS 93.8 MB / private 85.5 MB
- [x] Variant B (AOT MSIX, framework-dependent): Clipmark.App_1.0.0.0_x64.msix = 2.80 MB (19.32 MB with the WindowsAppSDK metapackage: AI/ML parts add onnxruntime + DirectML; csproj uses component packages)
- [x] Variant C (R2R + trimmed, self-contained): 86.40 MB shipped, window 526 ms, idle WS 107.3 MB
- [x] Snipping Tool 11.2607.23.0: install folder 585.6 MB, window 484 ms median, idle WS 107.2 MB / private 83.3 MB. Variant D (AOT unpackaged, framework runtime, Store-equivalent): 6.53 MB shipped, window 490 ms, idle WS 93.6 MB / private 85.0 MB
- [x] Go: Store package 2.80 MB vs 585.6 MB; start time and idle RAM at parity with Snipping Tool (empty-shell baseline, will grow with features)
### M1-M6
- [x] Logo: concept 03 noir-red (assets/logo/*.svg), assets via scripts/build-icons.ps1, exe icon embedded, taskbar verified by screenshot - [ ] M1 Capture  - [ ] M2 Editor base  - [ ] M3 Shotoshot-class tools  - [ ] M4 Editable format + text actions  - [ ] M5 Recording  - [ ] M6 Release
