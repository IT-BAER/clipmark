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
- M1: overlay = Win32 window per monitor painted with GDI (pill drawn with Segoe Fluent Icons). Default hotkey Ctrl+Print Screen (changeable). Auto-save off by default. Freeform result: bounding box, outside of the path transparent.

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
- [x] Logo: concept 03 noir-red (assets/logo/*.svg), assets via scripts/build-icons.ps1, exe icon embedded, taskbar verified by screenshot
### M1 Capture
- [x] M1.1 Core: selection normalize/clamp, monitor + window pickers, freeform mask, file namer, hotkey text, settings JSON (TDD, 41 tests green)
- [x] M1.2 Tray host: icon + menu render, Ctrl+Print Screen delivered (overlay 35-72 ms), single-instance redirect works. Menu picks threw EntryPointNotFoundException (`PostMessage` has no unsuffixed export, fixed to `PostMessageW`); Open, Settings, Exit and a Mode submenu pick verified
- [x] M1.3 Overlay: per-monitor GDI windows + pill; rectangle (primary, x=-2560 monitor, across both), window, full screen, all monitors, freeform pixel-compared against a reference grab; Esc cancels without touching the clipboard
- [x] M1.4 Delay: 3 s setting -> overlay after 2996 ms
- [ ] M1.5 Output: clipboard PNG + DIB verified, auto-save verified (name pattern, bytes = clipboard PNG). Toast reaches notification center only (no banner, unpackaged; fix in M6). Main window shows the capture after New (UIA "Captured image" 400x300 = drag size, screenshot checked). Toast-click open not tested (toast lives in notification center only; M6)
- [x] M1.6 Settings page renders and persists; Spike.cs removed; `--measure-startup` in measure script. Hotkey change to Ctrl+Shift+F8 re-registers (new key opens overlay, old key released, restore works); folder picker saves and shows the folder, "Use default" clears it
- [x] M1.7 Re-measure (scripts\measure-m0.ps1 D-aot-framework, 5 runs): D window 348 ms median (M0 490), first frame 371 ms, idle WS 103.4 MB / private 89.2 MB (M0 93.6 / 85.0), shipped 7.63 MB without pdb (M0 6.53); MSIX 3.2 MB (M0 2.80); Snipping Tool same run 389 ms, WS 107.4 MB / private 81.8 MB
- [x] M1.8 Self-contained Windows App SDK builds (A zip candidate, C) never showed a window: `AppNotificationManager.Default.Register()` throws COMException 0x8007007E "Unable to load resource dll. Microsoft.WindowsAppRuntime.Insights.Resource.dll" (only in the installed framework package, in no NuGet package), and `async void OnLaunched` ended silently. Fixed: `CaptureOutput.RegisterNotifications()` catches the failure and toasts are skipped. Self-contained Debug: no window before, window "Clipmark" after; capture check green (400x300, toast on). Measure (5 runs): A window 360 ms, first frame 363 ms, WS 102.7 / private 87.9 MB, 66.47 MB without pdb; C 422 ms, 444 ms, WS 118.7 / private 91.8 MB, 87.82 MB; D same run 352 ms, Snipping Tool 369 ms
- Deferred to M6: test shipping Microsoft.WindowsAppRuntime.Insights.Resource.dll in self-contained builds so toasts work there (M1.8)
- Deferred to M6 (decided 11 October 2026): toast banner for the unpackaged build needs a Start menu shortcut with the AUMID; verify MSIX banner there
### M2 Editor base
- Decided 11 October 2026: Win2D 1.4.0 (AOT measured clean: D 9.40 MB, MSIX 4.0 MB, +18 MB private with a live canvas, start unchanged); object eraser; ask to discard unsaved edits on New / close
- [x] M2.1 Core: document (base size, objects, crop), strokes + shapes, undo/redo + dirty flag, eraser hit test, arrow head + 45-degree snap + point thinning, crop math (TDD, `Clipmark.Core.Editor`, 90 tests green)
- [x] M2.2 Win2D EditorCanvas replaces the Image view: 400x300 capture = 400x300 physical px (UIA + DPI-aware screenshot), centered; Ctrl+wheel zoom around the cursor (ScrollViewer ZoomMode alone did not zoom): 0.64 -> 1.0 -> 1.5625 by UIA rect. One run right after a capture ignored the zoom-in notches (not reproduced)
- Decided 11 October 2026: copy to clipboard and toast are off by default; with the toast off a capture opens the editor, which has Copy (Ctrl+C). Copy is pulled forward from M2.5 (verified: clipboard untouched after capture, 400x300 after Copy)
- Decided 11 October 2026: no real mouse/keyboard input in tests. Debug-only TestDriver (`CLIPMARK_TEST_DIR`, cloaked window, `--open FILE`): toolbar clicks via automation peers, drags in image px, export PNG, window screenshots via RenderTargetBitmap. WinUI 3 ignores posted mouse messages (measured). Real pointer path = one manual drag by the user
- [x] M2.3 Pen, highlighter (no self-overlap darkening), rect/oval/line/arrow, object eraser, undo/redo; 6 colors + 3 widths; tool keys. Tool logic in Core `ToolSession`, `.claude\m1-harness\m23-script.ps1` GREEN (17 export pixel checks incl. single highlighter blend 192,179,64, eraser one undo step, Shift-snap, toolbar via automation, screenshots reviewed), manual pen drag drew; tool keys P/H/E/R/C/L/A and Ctrl+Z/Y/C confirmed manually (11 October 2026)
- [x] M2.3b Draw outside the image; the canvas always fits image + objects (user request 11 October 2026). Outside area transparent in the export; the editor shows the app background there (user, no checkerboard); grows and shrinks with add / erase / undo. Core `Bounds` + nullable `Crop` (113 tests); `.claude\m1-harness\m23b-script.ps1` GREEN (export 524x300 / 524x342 with negative origin, outside stroke red, empty outside alpha 0, undo 400x300; 2400x1400 image keeps its screen position when the canvas grows left); screenshots reviewed; manual drag from outside confirmed. Below zoom 1 the canvas does not grow with the viewport (GPU memory bound)
- Decided 11 October 2026 (user): toolbar in Snipping Tool layout (New/mode/delay left, drawing tools + undo/redo centered, output + settings right); menus mark the current item in the accent color instead of a radio dot
- Decided 11 October 2026: an applied crop grows to include objects drawn outside it; undo shrinks it back
- [x] M2.4 Crop: handles, apply/cancel, undoable. Core `CropMath.HitHandle` (corners first, then anywhere along an edge), crop grows on add and is trimmed to the bounds on erase (128 tests); Crop toggle + Apply/Cancel buttons, Enter/Esc via page PreviewKeyDown (an accelerator lost Enter to the focused Crop button); `.claude\m1-harness\m24-script.ps1` GREEN (8 checks: 300x240 crop, growth to 354x240 by a stroke outside, undo, cancel); screenshots reviewed; manual handles, Enter, Esc, Ctrl+Z confirmed (11 October 2026)
- Decided 11 October 2026 (user): closing the window ends the editor session (prompt first if dirty) and frees the canvas; tray Exit asks too
- [x] M2.5 Save as (FileSavePicker, PNG/JPEG, name `CaptureFileName`, save folder), Copy, Share (`IDataTransferManagerInterop` via raw vtable, works unpackaged), Save / Don't save / Cancel prompt on New, Ctrl+N, capture hotkey, toast open, window close, tray Exit; Ctrl+S / Ctrl+N. JPEG puts transparent pixels onto white (Core `Pixels.OverWhite`, 129 tests). MainPage is navigation-cached: Settings and back lost the capture before. `.claude\m1-harness\m25-script.ps1` GREEN (17 checks: saved PNG = export pixels, JPEG 400x300 red/gray, grown bounds PNG alpha 0 / JPEG white, dirty flag, Settings round trip, close Cancel / Don't save); prompt screenshot reviewed; m23/m23b/m24 GREEN; picker, share sheet, Ctrl+N / Exit / close prompts confirmed manually (11 October 2026)
- [x] M2.6 Closing the window ends the session (M2.5); `EditorCanvas.Close` now trims the shared Win2D device. Debug, 2560x1440: live canvas +46-48 MB private, close freed 0 MB before, 24-26 MB after (`.claude\m1-harness\m26-mem.ps1` GREEN, 2 runs); a private device disposed on close freed no more. About 22 MB stays as the one-time Win2D load (DLLs, globals; GC frees nothing). Re-measure (measure-m0.ps1, 5 runs): A window 401 ms, first frame 436 ms, WS 104.3 / private 90.0 MB, 69.55 MB without pdb; C 448 ms, 466 ms, WS 120.3 / private 91.9 MB, 90.60 MB; D 368 ms, 395 ms, WS 104.7 / private 89.0 MB, 10.71 MB; MSIX 4.4 MB; Snipping Tool same run 397 ms, WS 107.4 / private 83.0 MB (11 October 2026)
### M3 Shotoshot-class tools (done 11 October 2026)
- Decided 11 October 2026 (user): plain wheel over the canvas = width / text size, Shift+wheel scrolls, Ctrl+wheel zooms; bucket fills rectangles / ellipses or the canvas background outside the image (no capture pixel changes); an attached arrow end keeps the spot where it was dropped and follows the shape
- [x] M3.1 Select (V): click / Shift-click / rubber band, move, nudge (1 / 10 px), Page Up / Down z-order (Shift = front / back), Del, Ctrl+A, Esc; style changes apply to the selection. Core `SelectionSession`, `EditorDocument.Replace` / `Reorder`, value equality for strokes (158 tests); `.claude\m1-harness\m31-script.ps1` GREEN (14 export pixel checks), m23 / m23b / m24 / m25 GREEN; screenshots reviewed. Real keys first failed: after a canvas click nothing in the page has focus, so the page's PreviewKeyDown never fired (measured by a key log); editing keys now hook the window root. Manual check confirmed (11 October 2026)
- [x] M3.2 Resize + rotate handles (Shift = aspect / 15 degrees); line / arrow end-point handles; rotation in hit test, bounds, eraser, export. Core: `EditorObject.Rotation`, `EditorGeometry.LocalBox` / `ToWorld` / `ToLocal`, rotated `Extent` and HitTest, `ObjectTransform` (Rotate / Resize / ScaleUniform), `SelectionSession` Frame / handles / `Live`; handles win over Shift / Ctrl toggle at press (192 tests). App: objects drawn with `Rotation`, drags show `Live` in place of the originals, rotated dashed frame outside the stroke with 8 handles (group: 4 corners) + rotate handle, end-point handles for a single line / arrow; handle sizes constant on screen. `.claude\m1-harness\m32-script.ps1` GREEN (17 export pixel checks: corner / edge resize, Shift 2:1, Shift rotate 90, rotated hit test, undo, arrow end drag); m31 / m35 / m38 / m23 / m23b / m24 / m25 GREEN; screenshots reviewed (11 October 2026)
- [x] M3.3 Attached arrows: snap highlight, follow on move / resize / rotate, detach / reattach, delete target keeps the arrow. Core: `Attachment(Target, Anchor)` normalized in the target's local box (the end keeps its dropped spot), `Shape.StartAttach` / `EndAttach`, `EditorDocument.SnapTarget` / `Attach` / `WithAttached`; `Replace` moves ends of changed targets and detaches ends that left their anchor, `Remove` detaches ends of removed targets, both in the same undo step (201 tests). App: drag previews include dependent arrows, accent box on the target while a dragged end snaps. `.claude\m1-harness\m33-script.ps1` GREEN (13 checks), m31 / m32 / m35 / m38 / m23 / m23b / m24 / m25 GREEN; mid-drag screenshots reviewed (11 October 2026)
- [x] M3.4 Text tool (T): place, edit (double-click / T click), rotate; single-letter shortcuts off while typing. Core `TextObject(Id, Color, Width, Position, Text, UnitSize)`: Width is the font size (wheel, presets 16 / 24 / 40 and slider set the text size), Size = UnitSize x Width; box hit test / extent / snap target, corner handles scale uniformly, no edge handles (208 tests). App: TextBox overlay above the canvas (`EditorCanvas.Host`), Esc / click outside / tool change / undo / export / crop commit as one undo step, empty text deletes, Win2D `CanvasTextLayout` drawing with grayscale antialiasing; all shortcuts off while text is edited or a text field has focus. `.claude\m1-harness\m34-script.ps1` GREEN (7 ink checks plus nokey P / V / Ctrl+Z), m31 / m32 / m33 / m35 / m38 / m23 / m23b / m24 / m25 GREEN; screenshots reviewed (11 October 2026)
- [x] M3.5 Style flyout (built on first open): 6 swatches, "More colors" `ColorPicker` ring, Thin / Medium / Thick, width slider 1-96; plain wheel scales the width about 20 % per notch (tool or selection) with a 0.9 s size hint, Shift+wheel scrolls. `.claude\m1-harness\m35-script.ps1` GREEN (13 checks), m23 / m31 / m38 GREEN; hint screenshot reviewed. AOT D publish exit 0 without IL warnings; `.claude\m1-harness\aot-uia-smoke.ps1` GREEN (flyout, color ring, crop fields open in the AOT build). Each wheel notch / slider step on a selection is its own undo step (11 October 2026)
- [x] M3.6 Eyedropper (I): loupe + hex to clipboard, sets the current color. Core `ColorSample.At` / `Hex` (210 tests). App: composite of the shown area rendered once while the tool is active (dropped on any document change), 11 x 11 loupe with the hex label below-right of the pointer, click copies `#RRGGBB` (`CaptureOutput.CopyTextToClipboard`, CF_UNICODETEXT), returns to the previous tool and gives it the color; with Select the selection stays and gets the color; Esc cancels. `.claude\m1-harness\m36-script.ps1` GREEN (5 checks, picks a highlighter blend from the composite; the script overwrites the clipboard), m23 / m23b / m24 / m25 / m31 / m32 / m33 / m34 / m35 / m38 GREEN; loupe screenshot reviewed (11 October 2026)
- [x] M3.7 Bucket (G): shape fill + canvas background. Core `Shape.Fill` (rectangle / ellipse, a filled shape is hit inside), document `Background` in the undo history, `EditorDocument.Fill` (topmost rectangle / ellipse whose inside or outline holds the point, rotation respected, else the background; same color adds no step) (216 tests). App: Fill tool with its own color (default yellow), fill under the outline, background under the image in editor and export (shows in transparent image parts and grown canvas). Icon: Segoe Fluent Icons has no bucket glyph, E771 (brush) is used. `.claude\m1-harness\m37-script.ps1` GREEN (10 checks, half-transparent fixture), m23 / m23b / m24 / m25 / m31 / m32 / m33 / m34 / m35 / m36 / m38 GREEN; screenshot reviewed (11 October 2026)
- [x] M3.8 Exact-size crop fields X / Y / W / H with arithmetic (`NumberBox` `AcceptsExpression`; a field moves that edge or size, values clamp to the canvas, handles and fields stay in sync; Enter inside a field evaluates it, outside applies). `.claude\m1-harness\m38-script.ps1` GREEN (9 checks: 200+50, 10*3, clamp of H and Y, invalid text, handle sync, Enter, 230x230 export); m24 / m31 GREEN; screenshot reviewed (11 October 2026)
- Decided 11 October 2026 (user): no more manual real-input checks; test-driver scripts only. Known gap: real focus and pointer routing are not exercised (the M3.1 key bug was of this kind), so key handling stays on the window root
- [x] M3.9 AOT publish + re-measure A, C, D, MSIX + memory check. `scripts\publish-m0.ps1` all variants exit 0, A / D without IL warnings (C: IL2104 from Microsoft.Windows.SDK.NET and WinRT.Runtime only). `scripts\measure-m0.ps1` (5 runs, median window / first frame, idle after 5 s): A 330 / 357 ms, WS 105.2 / private 89.3 MB, 113.2 MB; C 444 / 459 ms, 121.3 / 93.4 MB, 91.2 MB; D 351 / 382 ms, 106.6 / 90.1 MB, 11.25 MB without pdb (M2 9.40); MSIX 4.6 MB (M2 4.0); Snipping Tool 11.2607.23.0 same run 374 ms, 107.5 / 81.9 MB, 585.6 MB. `.claude\m1-harness\m26-mem.ps1` GREEN (2560x1440 live canvas +47.4 MB, close frees 24.7 MB). `.claude\m1-harness\aot-uia-smoke.ps1` GREEN on D (15 checks, adds Select / Text / Eyedropper / Fill tool switches); text editing, picking and filling in the AOT build need canvas clicks and stay unverified there (11 October 2026)
### M4-M6
- [ ] M4 Editable format + text actions  - [ ] M5 Recording  - [ ] M6 Release
