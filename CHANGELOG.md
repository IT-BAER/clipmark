# Changelog

All notable changes to Clipmark are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Clipmark Premium: 30-day trial, then offline license keys. Premium covers select, attached arrows, text, fill,
  eyedropper, editable PNG, Send to AI and the capture history; editable PNGs still open without a license.
- Send to AI: pastes the capture into a running Claude Code or Codex CLI, the Claude or ChatGPT desktop app, or the
  Claude or Codex VS Code extension, with each agent's own icon in the menu.
- Capture history (opt-in): the last 25 captures, encrypted for the Windows user, with a thumbnail flyout; edits are
  written back to the item.
- The tray icon is shown on the taskbar the first time a copy of Clipmark runs; hiding it later in Windows settings
  is kept.
- MSIX readiness: "Start with Windows" through a startup task, toast activation, capture capabilities.

### Fixed
- Exiting from the tray right after an edit no longer loses that edit in the capture history.
- Send to AI into Windows Terminal: the menu names the agent's own tab, and Clipmark asks to switch tabs instead of
  typing into another tab when the agent's tab is not active.
- The editor no longer crashes on opening an image in the release (Native AOT) build.
- Recordings no longer start with a black frame.
- The tray menu follows Windows dark mode; toggle switches show English On / Off.
- The record bar dot is anti-aliased.

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
- A recording that is still running when Windows signs out or shuts down is not finalized.

[Unreleased]: https://github.com/IT-BAER/clipmark/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/IT-BAER/clipmark/releases/tag/v1.0.0
