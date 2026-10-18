# Changelog

All notable changes to Clipmark are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Update check: once a day Clipmark asks GitHub whether a newer release exists (no identifier is sent) and offers a
  popup with Download, Never and Later when you open the window on an empty editor, never over a capture. Download
  opens the release page. Settings > About turns the check off or on, shows the last check, and has Check now and
  Download. The Store build does not check.
- Create GIF from a recording (GIF button after Share, as in Snipping Tool): Low (12 fps) or High (24 fps), long edge
  up to 1280 px, the first 30 seconds, Export or Copy, with progress and Cancel. A Low GIF stays under 7.5 MB so it
  fits a Claude upload; when it would be larger, Clipmark writes it again at a smaller size. Free.
- Emoji stamps (Shapes > Emoji, M): a picker with every emoji the Windows emoji font can draw, a search over names
  and keywords in the UI language and English, Recent (starts with Snipping Tool's 18) and group tabs. Click places
  an emoji, drag sets its size; Select moves, resizes and rotates it. Editable PNGs with emoji use format version 2,
  which earlier Clipmark versions cannot open; files without emoji stay version 1.
- Clipmark Premium: 30-day trial, then a license. Premium covers select, attached arrows, text, fill,
  eyedropper, editable PNG, Send to AI, Ask AI and the capture history; editable PNGs still open without a license.
  The trial starts on IT-BAER's licence server (releash.it-baer.net) at the first start with an internet connection
  and is bound to a non-exportable key of this PC (TPM when available), so reinstalling does not restart it.
  Settings > License takes an activation code (or a license key from an earlier version), shows how many PCs the
  license covers, and "Remove from this PC" frees the seat. The signed entitlement is checked offline and renewed
  once a day while online (a trial about every two weeks), so a removed or revoked seat locks within about a day;
  offline, Premium keeps working until the entitlement and its grace period run out. Premium comes only from the
  server's signed entitlement: there is no local trial, and a license key of an earlier version unlocks nothing
  until it is activated, which happens automatically at the next online start.
- Ask AI: ask Claude about the capture in a toolbar flyout (Sonnet 5.5, Opus 5.5 or Haiku 5.5; answer depth low, med,
  high or xhigh in the chat header or Settings). Answers stream in as formatted text with a collapsed Thinking
  summary; Stop, follow-up questions and New chat. Premium activated on the PC uses Ask AI without a key through
  IT-BAER's relay (the flyout shows the monthly budget left); with your own Anthropic API key (kept in Windows
  Credential Manager) Clipmark sends directly to Anthropic, also during the trial. The first send on each path asks
  for consent.
- Send to AI: pastes the capture into a running Claude Code or Codex CLI, the Claude or ChatGPT desktop app, or the
  Claude or Codex VS Code extension, with each agent's own icon in the menu.
- Capture history (opt-in): the last 25 captures, encrypted for the Windows user, with a thumbnail flyout; edits are
  written back to the item.
- The tray icon is shown on the taskbar the first time a copy of Clipmark runs; hiding it later in Windows settings
  is kept.
- Text actions: select part of the recognized text by dragging across it (by character, across lines) or by
  double-clicking a word, and copy it with Ctrl+C, the right-click menu or Copy selection; Ctrl+A selects all text.
  A drag that starts on recognized text selects it whatever tool is active; elsewhere the tool works as before.
- Text actions: Quick redact also covers IP and MAC addresses, bank data (IBAN, card numbers, BIC after a BIC / SWIFT
  label) and passwords or keys (known token shapes, and the value after a label such as "Password:"), each kind
  switchable in the menu. Redact selection blacks out the selected text from the right-click menu, the Redact
  selection button or Ctrl+Shift+R, as one undo step.
- Snipping Tool features (all free):
  - Capture overlay: Color picker with a loupe that copies HEX, RGB or HSL (Settings > Color format, also used by the
    eyedropper); Quick markup (Ctrl+E) draws a red pen on the frozen screen, and the strokes stay editable in the
    editor.
  - Editor: More menu with zoom (100%, in, out, fit; Ctrl+0, Ctrl+plus, Ctrl+minus), Ruler, Print, Edit in Paint and
    Open screenshots folder; Erase all mark-ups; Opacity slider (10-100 %) for pen, shapes, text and fill. The ruler
    turns with Ctrl+mouse wheel in 15 degree steps, and pen and highlighter strokes that start at its edge run along it.
  - Text actions: Copy as table puts a table from the capture on the clipboard as HTML (Word, Outlook, Excel) and
    tab-separated text; a title or note above or below the table stays out of its columns. Quick redact can skip email addresses or phone numbers and has Remove all redactions; QR codes with
    a phone number or email address offer Call or Send email; the text extractor can remove line breaks.
  - Recording: Pause / Resume on the record bar (the video has no gap), and the microphone or system audio can be
    muted while recording without changing the setting.
  - Settings: border on each capture (color, width), app theme (Windows default, Light, Dark), copy every edit to
    the clipboard, close the window after copying (Copy, Ctrl+C, or the copy after a capture), Open folder buttons
    for screenshots and recordings.
  - Opens .jfif, .jpe, .dib and .ico files.
- MSIX readiness: "Start with Windows" through a startup task, toast activation, capture capabilities.
- 16 interface languages: English, German, French, Italian, Spanish, Portuguese (Brazil), Japanese, Korean, Chinese
  (Simplified and Traditional), Dutch, Polish, Russian, Swedish, Turkish and Ukrainian. Clipmark follows the Windows
  display language, or pick one in Settings > Language. German is reviewed; the others are machine-translated.

### Changed
- Settings are grouped into cards: License and billing, General, Capture, Recording, Ask AI, About. With a paid
  license, License and billing has Manage subscription (Stripe customer portal), Request a refund (Link support) and
  Remove from this PC, which now says that it frees the seat and does not cancel billing.
- Mouse wheel in the editor zooms around the cursor; Ctrl+mouse wheel sets the width, text size or ruler angle
  (before: the plain wheel did, and Ctrl+mouse wheel zoomed).
- Output toolbar order: Copy, Save, Share, then Send to AI and Ask AI after a separator.
- The Delay button is highlighted while a delay is set and shows the timer icon with the seconds; it no
  longer grows taller than the other toolbar buttons, and neither does New.
- License: the Elastic License 2.0 replaces the MIT License. The source stays public; it must not be used to remove or
  work around the license key check, or to offer Clipmark as a hosted service. Release 1.0.0 stays MIT licensed.
- Text recognition uses Clipmark's own OCR engine (PaddleOCR PP-OCRv6 models on ONNX Runtime, on your PC). On a
  benchmark of 54 screenshot-style images it made 0.22% character errors, Snipping Tool 2.40% and the Windows text
  recognition used before 8.52%. Windows text recognition still reads non-Latin languages and is the fallback when
  the engine fails.
- Text actions dim the image with Snipping Tool's gray veil and show each recognized line as a rounded cutout.

### Fixed
- Screen recording starts when the 3-second countdown ends. Before, the encoder and capture setup ran after the
  countdown, so the recording started up to 2.4 seconds late.
- Exiting from the tray right after an edit no longer loses that edit in the capture history.
- Send to AI into Windows Terminal: the menu names the agent's own tab, and Clipmark asks to switch tabs instead of
  typing into another tab when the agent's tab is not active.
- The editor no longer crashes on opening an image in the release (Native AOT) build.
- Share no longer crashes the release (Native AOT) build for a capture or a recording; if the file cannot be handed
  to Windows, the share window shows why.
- A recording shows a plain Save button, as in Snipping Tool; before, its arrow opened an empty menu.
- Licence server and relay errors: Clipmark tries once more when the server is busy, says when a long wait for too
  many attempts ends (minutes, or the date and time), says when the license plan lacks a feature, and asks to
  activate again when the server no longer knows this PC; Premium keeps working offline meanwhile, and Ask AI stops
  using the relay until then.
- License checks: Premium locks while the PC clock is set more than an hour behind the latest time Clipmark has
  seen, and Settings asks to correct the clock. A license from the server that this version cannot verify no longer
  replaces the working one; activation then asks to update Clipmark. After a 30-day limit on trials or activations,
  Clipmark waits for the date the server named instead of asking again every hour. Ask AI uses the relay only when
  the license plan includes it.
- Recordings no longer start with a black frame.
- The tray menu follows Windows dark mode; toggle switches show On / Off in the interface language.
- The record bar dot is anti-aliased.
- With a toolbar menu open, a click on another toolbar button closes the menu and acts at once; before, the
  first click only closed the menu.
- A narrow window no longer hides toolbar buttons. When one row does not fit, the edit tools move to a second row
  and wrap there; the top row then drops the New label, moves Send to AI, Ask AI, Open and History into More, and
  drops the delay label, in that order. The window is at least 643 px wide at 125 % scale, as Snipping Tool.
  While cropping, the crop controls replace the draw tools. Text actions and QR code results wrap to new lines.

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
