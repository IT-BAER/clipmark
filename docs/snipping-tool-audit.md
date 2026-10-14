# Snipping Tool 11.2608.41.0 vs Clipmark (M12.1, 14 October 2026)

Sources. Snipping Tool: UIA walk through its own UI on a separate desktop (de-DE UI, this PC): capture window, More menu,
settings (all groups expanded), video mode, capture overlay (Quick markup, color picker, text extractor), editor with every
toolbar flyout, crop, text actions on a text image and a QR image. Features the walk cannot reach (record toolbar, video
editor, flighted tools) come from the en-US UI string table of its `resources.pri` (`makepri dump`), marked "strings".
A strings-only feature exists in this build but is not shown on this PC: staged rollout or Copilot+ PC hardware.
Clipmark: source at `337d689` (XAML, `Strings\en-US\Resources.resw`, C#).

## Gaps (missing or partial in Clipmark)

| # | Feature | Snipping Tool | Clipmark | Source |
|---|---|---|---|---|
| G1 | Zoom commands | More > Zoom: 100%, zoom in, zoom out, fit to window | Ctrl+wheel only (0.1x-8x), no 100% / fit command | UIA |
| G2 | Ruler | More > Show ruler | None | UIA |
| G3 | Print | More > Print | None | UIA |
| G4 | Edit in Paint | Toolbar button | None | UIA |
| G5 | Open screenshots folder | More menu; Settings "Open folder" for screenshots and recordings | Settings: Change folder, Use default; no open | UIA |
| G6 | Emoji stamps | Shapes > Emoji: 18 emoji, move / resize / rotate | None (M12.3) | UIA |
| G7 | Opacity | Fill and outline opacity 1-100 %; outline width 1-24 | Color ring without alpha | UIA |
| G8 | Erase all mark-ups | Eraser flyout | Select all + Delete (Select tool is Premium) | UIA |
| G9 | Visual Search with Bing | More menu | None (Ask AI covers the use case) | UIA |
| G10 | Copy as table | Text actions | None (M12.2) | UIA |
| G11 | Quick redact options | Choose emails / phone numbers; remove all redactions | Both always; Undo removes | UIA |
| G12 | QR phone / email actions | Call / copy phone, send / copy email, copy text | Copy, Open link (http / https only) | strings |
| G13 | Text extractor options | Options button: automatically copy text, remove line breaks; select all, copy | Copies all text at once, no options | UIA + strings |
| G14 | Color picker | Capture overlay, zoom loupe, HEX / RGB / HSL | Editor eyedropper, #RRGGBB only (Premium) | UIA |
| G15 | Quick markup | Ctrl+E: draw on the live screen before the capture | None | UIA |
| G16 | Keyboard snipping | Enter starts a selection, arrow keys move / resize it | Overlay handles only Esc | strings |
| G17 | Border on each screenshot | Setting with color and thickness | None | UIA |
| G18 | HDR color corrector | Setting (tone mapping) | GDI BitBlt, SDR only | UIA |
| G19 | Multiple windows | Setting: each capture in its own window | One editor window | UIA |
| G20 | App theme | Settings: light, dark, system | Follows Windows | UIA |
| G21 | Open file types | bmp, dib, gif, ico, jfif, jpe, jpeg, jpg, png, tif, tiff, camera RAW | png, jpg, jpeg, bmp, gif, tif, tiff | manifest |
| G22 | Auto-copy edits | Setting (on here): every edit goes to the clipboard | Copies the capture once; edits need Copy | UIA |
| G23 | Pause / resume recording | Record toolbar | None | strings |
| G24 | Mute during recording | Mic and system audio buttons on the record toolbar | Audio sources fixed once recording runs | strings |
| G25 | Trim recording | Video editor | None | strings |
| G26 | GIF export | Create GIF: low 12 fps / high 24 fps, max 30 s, copy or export | None | strings |
| G27 | Edit in Clipchamp | Video editor | None | strings |
| G28 | Blur tool | Ctrl+U, blur intensity, move / resize / rotate | None | strings |
| G29 | Text formatting | Text tool: font, size, bold, italic, underline, alignment | Text labels without formatting | strings |
| G30 | Scrolling screenshot | Capture mode | None | strings |
| G31 | Draw and hold | Pen stroke held at the end becomes a line, arrow, circle or square | None | strings |
| G32 | Copilot features | Perfect Screenshot (hold Ctrl), Click to Do, Ask Copilot | None | strings |

Status 14 October 2026: M12.4 closed G1-G5, G7, G8, G11-G15, G17 and G20-G24 (commits `29ad64a`..`89d06ab`,
`0dc00a8`; G21 without camera RAW). M12.2 closed G10 copy as table. Open: G6 emoji (M12.3). Out of scope: G9, G16,
G25-G32.
Deferred: G18 HDR, G19 multiple windows. The Clipmark column above shows the state at `337d689`.

Same in both: rectangle / window / full screen / freeform capture, delay 3 / 5 / 10 s, text extractor, pen, highlighter,
eraser, rectangle / oval / line / arrow, fill, crop, undo / redo, OCR copy all, select and copy part of the text, quick
redact, QR link, Save as, Copy, Share, auto save with folder, recording with system audio and microphone, settings for
default audio sources, Print Screen key.

Clipmark only: all-monitors capture, full-screen recording, crop X / Y / W / H fields, select / move / rotate any object
later, attached arrows, editable PNG, capture history, Send to AI, Ask AI, custom colors, width 1-96 px, 16 UI languages
with an in-app setting.

Not checked: the record toolbar and video editor live (they need a real mouse drag to start a recording), the text tool
and blur in a build where they are switched on, Copilot+ features.
