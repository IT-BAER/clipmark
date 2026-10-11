# Changelog

All notable changes to Clipmark are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-11

First public release.

### Added
- Capture: rectangle, window, full screen, all monitors and freeform on a frozen, dimmed overlay across all monitors;
  delay 3 / 5 / 10 s; global hotkey; tray icon and menu; optional clipboard copy, automatic save and notification.
- Editor: Win2D canvas with pen, highlighter, object eraser, rectangle, oval, line and arrow (45-degree snap); select,
  move, resize, rotate, z-order and nudge; arrows that attach to shapes; text labels; eyedropper; fill; color ring and
  width presets; mouse-wheel sizing; crop with handles and exact-size fields with arithmetic; drawing outside the image
  grows the canvas; undo / redo.
- Output: save as PNG / JPEG, copy (PNG + DIB), Windows share sheet; editable PNG (objects stored in a private `clMK`
  chunk) that reopens with live objects; Open (Ctrl+O).
- Text actions: OCR copy, quick redact of email addresses and phone numbers, QR code decoding; text extractor in the
  capture overlay.
- Screen recording: region, window area or full screen to MP4 (H.264 + AAC, 30 fps) with system audio and microphone;
  record bar with countdown and region outline, both excluded from the recording; recording hotkey
  (Ctrl+Shift+Print Screen); player view with Save as, Copy and Share.
- Settings: capture and recording hotkeys, default mode and delay, after-capture actions, save folder, recording audio,
  "Use the Print Screen key to open Clipmark" (restores the Windows option when turned off), start with Windows.
- Notifications with banners for the unpackaged build (Start menu shortcut with the app's AUMID).
- `--restore-system` command line option to undo the Print Screen, autostart and Start menu changes.

### Known limitations
- The release zip is not code-signed; SmartScreen may warn on first start.
- No Microsoft Store package yet (the MSIX build exists but is unsigned).
- Clicking a notification opens the capture only while Clipmark is running.
- Text recognition uses the Windows OCR languages installed on the PC.

[1.0.0]: https://github.com/IT-BAER/clipmark/releases/tag/v1.0.0
