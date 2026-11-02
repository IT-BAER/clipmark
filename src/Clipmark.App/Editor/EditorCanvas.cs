using System.Numerics;
using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.TextActions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Overlay = Microsoft.UI.Xaml.Controls.Canvas;
using TextBox = Microsoft.UI.Xaml.Controls.TextBox;

namespace Clipmark.App.Editor;

/// <summary>Grid that sets the pointer cursor for its children (ProtectedCursor is protected).</summary>
internal sealed partial class CursorHost : Grid
{
    public InputCursor? Cursor { set => ProtectedCursor = value; }
}

/// <summary>Win2D surface for one capture. Draws in image pixel space: one image pixel = one physical pixel at zoom 1.</summary>
internal sealed class EditorCanvas
{
    private const float EdgeDips = 16;
    private const float HandleDips = 8;
    private const float HitDips = 6;
    private const float RotateDips = 24;
    private const float RulerDips = 96;
    private const float RulerSnapDips = 16;
    private const string TextFont = "Segoe UI";
    // Screen size of a clicked emoji: 80 px at 125 % display scaling, as Snipping Tool places one (measured 15 October 2026).
    private const float EmojiDips = 64;

    // Native AOT cannot unbox the "SystemAccentColor" resource (InvalidCastException on the first draw); UISettings returns the struct.
    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();
    private static Windows.UI.Color Accent => UiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);

    private readonly string? _path;
    private readonly byte[]? _file; // set when opened from memory (history item) instead of a path
    private static readonly CanvasStrokeStyle _round = new()
    {
        StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round,
    };
    private readonly CanvasStrokeStyle _dash = new() { DashStyle = CanvasDashStyle.Dash };
    private CanvasBitmap? _image;
    private byte[]? _imageBytes; // encoded base image, for a reload after a lost device
    private ToolSession? _session;
    private SelectionSession? _selection;
    private EditorTool _tool;
    // View: zoom factor and the image px point at the viewport's top-left; the control fills the viewport.
    private Vector2 _corner;
    private float _zoom = 1;
    private readonly CompositeTransform _overlayView = new();
    // Touch: a finger's press waits until it moves TouchSlopDips or lifts, so that a second finger in that time turns both
    // into a pinch (zoom and pan) instead of a drag. Positions in control DIPs.
    private const float TouchSlopDips = 4;
    private readonly Dictionary<uint, Vector2> _touches = [];
    private (uint Id, Vector2 At, bool Shift)? _pendingTouch;
    private uint? _dragTouch;
    private (float Distance, float Zoom, Vector2 Image)? _pinch;
    // Crop mode: the frame being edited (null outside crop mode) and the handle being dragged.
    private PixelRect? _cropFrame;
    private CropHandle? _handle;
    private PixelRect _handleStart;
    private Vector2 _grab;
    // Text editing: the TextBox over the canvas, the text it replaces (null for new text) and its style.
    private readonly Overlay _overlay = new();
    private TextBox? _textBox;
    private TextObject? _editing;
    private Vector2 _textAt;
    private Rgba _textColor;
    private float _textSize;
    // Eyedropper: straight BGRA of the shown area (built on first use, dropped on change) and the pointer for the loupe.
    private byte[]? _composite;
    private PixelRect _compositeArea;
    private Vector2? _hover;
    // Text actions: recognized lines, the character selection in them (anchor = drag start) and the I-beam cursor state.
    private IReadOnlyList<OcrLine> _textLines = [];
    private (OcrCaret Anchor, OcrCaret Focus)? _textSelection;
    private bool _selectingText;
    private bool _ibeam;
    private static readonly InputCursor IBeam = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
    private readonly CursorHost _host;
    // Ruler (a view aid, never exported or saved): center in image px once placed, angle in degrees, viewport size in DIPs.
    private Vector2? _rulerCenter;
    private float _rulerAngle;
    private bool _rulerShown, _movingRuler;
    private Vector2 _rulerGrab;
    private Vector2 _viewportDips;

    /// <summary>Recognized text (text actions); a drag that starts on it selects characters. Setting it clears the selection.</summary>
    public IReadOnlyList<OcrLine> TextLines
    {
        get => _textLines;
        set
        {
            _textLines = value;
            SetTextSelection(null);
        }
    }

    /// <summary>Selected recognized text; empty without a selection.</summary>
    public string SelectedText => _textSelection is { } s ? OcrSelection.Text(_textLines, s.Anchor, s.Focus, AppSettings.Current.OcrRemoveLineBreaks) : "";

    /// <summary>Boxes of the selected text in image px, grown outward by pad px; empty without a selection.</summary>
    public IReadOnlyList<PixelRect> SelectedTextBoxes(int pad) => _textSelection is not { } s ? [] : [.. OcrSelection.Boxes(_textLines, s.Anchor, s.Focus)
        .Select(b => PixelRect.FromCorners((int)MathF.Floor(b.Min.X) - pad, (int)MathF.Floor(b.Min.Y) - pad, (int)MathF.Ceiling(b.Max.X) + pad, (int)MathF.Ceiling(b.Max.Y) + pad))];

    public void ClearTextSelection() => SetTextSelection(null);

    /// <summary>Raised when the text selection changes.</summary>
    public event Action? TextSelectionChanged;

    /// <summary>Raised on a right-click on selected text, with the point in control DIPs.</summary>
    public event Action<Windows.Foundation.Point>? TextMenuRequested;

    public void SelectAllText()
    {
        if (_textLines.Count > 0)
            SetTextSelection(OcrSelection.All(_textLines));
    }

    private void SetTextSelection((OcrCaret Anchor, OcrCaret Focus)? selection)
    {
        if (selection == _textSelection)
            return;
        _textSelection = selection;
        Control.Invalidate();
        TextSelectionChanged?.Invoke();
    }

    /// <summary>Raised on a right-click elsewhere on the canvas, with the point in image px and in control DIPs.</summary>
    public event Action<Vector2, Windows.Foundation.Point>? ToolRingRequested;

    /// <summary>
    /// Right-click at an image px point: on selected text raises <see cref="TextMenuRequested"/>, elsewhere
    /// <see cref="ToolRingRequested"/>; false while cropping or dragging.
    /// </summary>
    public bool RightClick(Vector2 point)
    {
        var pad = new Vector2(HitDips * Scale / _zoom);
        if (_textSelection is { } s && OcrSelection.Boxes(_textLines, s.Anchor, s.Focus).Any(b => new FloatRect(b.Min - pad, b.Max + pad).Contains(point)))
        {
            TextMenuRequested?.Invoke(ControlPoint(point));
            return true;
        }
        if (Document is null || IsCropping || Dragging)
            return false;
        ToolRingRequested?.Invoke(point, ControlPoint(point));
        return true;
    }

    /// <summary>Tool ring pick at an image px point: Text edits the text there or starts new text, Emoji places the current emoji centered there.</summary>
    public void PlaceAt(Vector2 point)
    {
        if (Document is null || IsCropping || Dragging)
            return;
        CommitText();
        if (Tool == EditorTool.Text)
            BeginText(point, Document.HitTest(point, HitDips * Scale / _zoom) as TextObject);
        else if (Tool == EditorTool.Emoji)
        {
            BeginSession(point, shift: false);
            _session!.End();
            Control.Invalidate();
        }
    }

    /// <summary>Raised when the eyedropper picks a color (alpha 255).</summary>
    public event Action<Rgba>? ColorPicked;

    public CanvasControl Control { get; }
    /// <summary>Canvas plus the text editing overlay; it fills the image view.</summary>
    public FrameworkElement Host { get; }
    public EditorDocument? Document { get; private set; }
    /// <summary>Current tool; a drawing tool clears the selection, any change commits text being edited.</summary>
    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (value == _tool)
                return;
            CommitText();
            _tool = value;
            _hover = null;
            _composite = null;
            // The eyedropper keeps the selection (a picked color recolors it), also on the way back to Select.
            if (value is not (EditorTool.Select or EditorTool.Eyedropper) && _selection?.Selected.Count > 0)
            {
                _selection.Clear();
                Control.Invalidate();
                Changed?.Invoke();
            }
        }
    }

    public SelectionSession? Selection => _selection;
    public Rgba Color { get; set; }
    public float StrokeWidth { get; set; }
    /// <summary>Emoji the Emoji tool places.</summary>
    public string Emoji { get; set; } = "";
    /// <summary>False = new line / arrow ends do not attach to objects (premium feature locked).</summary>
    public bool AttachEnabled { get; set; } = true;
    public bool IsCropping => _cropFrame is not null;

    /// <summary>Frame being edited in crop mode, else null.</summary>
    public PixelRect? CropFrame => _cropFrame;

    /// <summary>Raised when the crop frame moves (handle drag, field input, undo while cropping).</summary>
    public event Action? CropFrameChanged;

    /// <summary>Sets the crop frame from exact values, clamped to the canvas; ignored outside crop mode or when empty after clamping.</summary>
    public void SetCropFrame(PixelRect rect)
    {
        if (!IsCropping || Dragging || Document is null || rect.Intersect(Document.Bounds) is not { } clamped || clamped == _cropFrame)
            return;
        _cropFrame = clamped;
        Control.Invalidate();
        CropFrameChanged?.Invoke();
    }

    /// <summary>Raised when the document is created or changes (add, remove, undo, redo, crop), crop mode starts or ends, or the selection changes.</summary>
    public event Action? Changed;

    public EditorCanvas(string path) : this(path, null) { }

    /// <summary>Opens image or editable PNG bytes that have no file.</summary>
    public EditorCanvas(byte[] file) : this(null, file) { }

    private EditorCanvas(string? path, byte[]? file)
    {
        _path = path;
        _file = file;
        Control = new CanvasControl();
        AutomationProperties.SetName(Control, Loc.Get("CanvasName"));
        Control.CreateResources += (s, e) => e.TrackAsyncAction(LoadAsync(s).AsAsyncAction());
        Control.Draw += OnDraw;
        Control.PointerPressed += OnPointerPressed;
        Control.PointerMoved += OnPointerMoved;
        Control.PointerReleased += (_, e) => Control.ReleasePointerCapture(e.Pointer);
        Control.PointerCaptureLost += (_, e) =>
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
                TouchUp(e.Pointer.PointerId);
            else
                Release();
        };
        Control.PointerExited += (_, _) =>
        {
            _hover = null;
            Control.Invalidate();
        };
        Control.DoubleTapped += (_, e) => DoubleClick(ToImage(e.GetPosition(Control)));
        Control.RightTapped += (_, e) => e.Handled = RightClick(ToImage(e.GetPosition(Control)));
        _overlay.RenderTransform = _overlayView;
        Host = _host = new CursorHost { Children = { Control, _overlay } };
    }

    public bool IsEditingText => _textBox is not null;

    internal TextBox? TextEditor => _textBox;

    /// <summary>Text being edited with its current style, or null.</summary>
    public TextObject? EditingText => _textBox is { } box
        ? new TextObject(_editing?.Id ?? 0, _textColor, _textSize, _textAt, box.Text, _editing?.UnitSize ?? default) { Rotation = _editing?.Rotation ?? 0 }
        : null;

    /// <summary>Text tool click: edits the text under the point or starts new text there; a click while editing commits instead.</summary>
    private void TextClick(Vector2 point)
    {
        if (IsEditingText)
        {
            CommitText();
            return;
        }
        BeginText(point, Document?.HitTest(point, HitDips * Scale / _zoom) as TextObject);
    }

    /// <summary>Double-click with Select edits the text under the point.</summary>
    internal void DoubleClick(Vector2 point)
    {
        if (!IsCropping && !Dragging && OcrSelection.HitTest(_textLines, point, HitDips * Scale / _zoom) is { } caret)
        {
            SetTextSelection(OcrSelection.Word(_textLines, caret));
            return;
        }
        if (Tool == EditorTool.Select && !IsCropping && !Dragging && !IsEditingText
            && Document?.HitTest(point, HitDips * Scale / _zoom) is TextObject text)
            BeginText(text.Position, text);
    }

    private void BeginText(Vector2 at, TextObject? existing)
    {
        _editing = existing;
        _textAt = existing?.Position ?? at;
        _textColor = existing?.Color ?? Color;
        _textSize = existing?.Width ?? StrokeWidth;
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var box = _textBox = new TextBox
        {
            Text = existing?.Text ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 8,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            Background = clear,
            FontFamily = new FontFamily(TextFont),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = (existing?.Rotation ?? 0) * 180 / MathF.PI },
        };
        // Lightweight styling: no theme background behind the text while hovered or focused.
        foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            box.Resources[key] = clear;
        AutomationProperties.SetName(box, Loc.Get("CanvasTextBoxName"));
        _overlay.Children.Add(box);
        PlaceTextBox();
        Control.Invalidate();
        // After the pointer press finishes, which would otherwise keep the focus on the canvas.
        Control.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectionStart = box.Text.Length;
        });
        Changed?.Invoke();
    }

    /// <summary>New color and / or size (font px) for the text being edited.</summary>
    public void RestyleText(Rgba? color = null, float? size = null)
    {
        if (_textBox is null)
            return;
        _textColor = color ?? _textColor;
        _textSize = size ?? _textSize;
        PlaceTextBox();
    }

    private void PlaceTextBox()
    {
        if (_textBox is not { } box)
            return;
        var brush = new SolidColorBrush(ToColor(_textColor));
        box.Foreground = brush;
        foreach (var key in new[] { "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = brush;
        box.FontSize = _textSize / Scale;
        Overlay.SetLeft(box, _textAt.X / Scale - 1);
        Overlay.SetTop(box, _textAt.Y / Scale - 1);
    }

    // The overlay holds DIPs at zoom 1 from the image origin and is scaled and moved with the view.
    private void PlaceOverlay()
    {
        var offset = Offset / Scale;
        _overlayView.ScaleX = _overlayView.ScaleY = _zoom;
        _overlayView.TranslateX = offset.X;
        _overlayView.TranslateY = offset.Y;
    }

    /// <summary>Ends text editing as one undo step: new text is added, edited text replaced, text left empty removed.</summary>
    public void CommitText()
    {
        if (_textBox is not { } box || Document is null)
            return;
        _textBox = null;
        _overlay.Children.Remove(box);
        var existing = _editing is { } e ? Document.Objects.FirstOrDefault(o => o.Id == e.Id) as TextObject : null;
        _editing = null;
        string text = box.Text.Replace("\r\n", "\n").Replace('\r', '\n'); // TextBox line breaks are \r
        if (string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null)
                Document.Remove([existing.Id]);
        }
        else
        {
            var unit = MeasureText(text, _textSize) / _textSize;
            if (existing is null)
                Document.Add(new TextObject(Document.NewId(), _textColor, _textSize, _textAt, text, unit));
            else
                Document.Replace([existing with { Text = text, Color = _textColor, Width = _textSize, UnitSize = unit }]);
        }
        Control.Invalidate();
        Changed?.Invoke();
    }

    private static CanvasTextLayout TextLayout(ICanvasResourceCreator creator, string text, float size)
    {
        using var format = new CanvasTextFormat { FontFamily = TextFont, FontSize = size, WordWrapping = CanvasWordWrapping.NoWrap };
        return new CanvasTextLayout(creator, text, format, 0, 0);
    }

    private Vector2 MeasureText(string text, float size)
    {
        using var layout = TextLayout(Control, text, size);
        var bounds = layout.LayoutBounds;
        return new Vector2((float)bounds.Width, (float)bounds.Height);
    }

    internal static Windows.UI.Color ToColor(Rgba c) => Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>True when the file was an editable PNG whose objects loaded.</summary>
    public bool FromEditable { get; private set; }

    /// <summary>Raised with a user-facing message when an editable PNG's data cannot be used; the flattened image opens instead.</summary>
    public event Action<string>? Problem;

    /// <summary>
    /// Opens the file as an image, or an editable PNG as its base image plus live objects. Damaged editable data falls back
    /// to the flattened image (what every viewer shows).
    /// </summary>
    private async Task LoadAsync(CanvasControl sender)
    {
        if (_imageBytes is not null) // device lost: same image again
        {
            _image = await LoadBitmapAsync(sender, _imageBytes);
            return;
        }
        byte[] file = _file ?? await File.ReadAllBytesAsync(_path!);
        EditorDocument? loaded = null;
        if (EditablePng.Read(file) is { } data)
        {
            try
            {
                loaded = DocumentJson.Parse(data.Json);
                _image = await LoadBitmapAsync(sender, data.BasePng);
                if (_image.SizeInPixels.Width != loaded.Width || _image.SizeInPixels.Height != loaded.Height)
                    throw new InvalidDataException(Loc.Get("EditableDataMismatch"));
                _imageBytes = data.BasePng;
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                loaded = null;
                _image?.Dispose();
                _image = null;
                Problem?.Invoke(Loc.Format("EditableDataDropped", e.Message));
            }
        }
        if (_image is null)
        {
            _image = await LoadBitmapAsync(sender, file);
            _imageBytes = file;
        }
        var size = _image.SizeInPixels;
        if (Document is null)
        {
            FromEditable = loaded is not null;
            Document = loaded ?? new EditorDocument((int)size.Width, (int)size.Height);
            Document.Changed += () =>
            {
                _composite = null;
                if (IsCropping)
                {
                    _cropFrame = Document.Crop ?? Document.Bounds;
                    CropFrameChanged?.Invoke();
                }
                Control.Invalidate();
                Changed?.Invoke();
            };
            _session = new ToolSession(Document);
            _selection = new SelectionSession(Document);
            Changed?.Invoke();
        }
    }

    private static async Task<CanvasBitmap> LoadBitmapAsync(CanvasControl sender, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        return await CanvasBitmap.LoadAsync(sender, stream, 96);
    }

    // Display scale: physical px per DIP (DpiScale stays 1, so the surface is the viewport in physical px).
    private float Scale => Control.Dpi / 96;

    private bool Dragging => _session?.IsActive == true || _selection?.IsActive == true || _handle is not null || _selectingText || _movingRuler;

    public bool RulerVisible => _rulerShown;

    /// <summary>Ruler center (image px) and angle once it has been placed.</summary>
    internal (Vector2 Center, float Angle)? RulerPose => _rulerCenter is { } c ? (c, _rulerAngle) : null;

    internal float RulerHalfHeight => CurrentRuler.HalfHeight;

    private Ruler CurrentRuler => new(_rulerCenter ?? Vector2.Zero, _rulerAngle, RulerDips / 2 * Scale / _zoom);

    // 1.5 x the viewport diagonal, so the ruler spans the view at any angle.
    private float RulerHalfLength => 0.75f * _viewportDips.Length() * Scale / _zoom;

    private bool OnRuler(Vector2 point) => _rulerShown && !IsCropping && CurrentRuler.Contains(point, RulerHalfLength);

    /// <summary>Shows or hides the ruler; the first time it is centered on the control DIP point.</summary>
    public void SetRuler(bool on, Windows.Foundation.Point center)
    {
        _rulerShown = on;
        _movingRuler &= on;
        if (on)
            _rulerCenter ??= ToImage(center);
        Control.Invalidate();
    }

    /// <summary>Puts the ruler at an image px point and angle in degrees.</summary>
    internal void PlaceRuler(Vector2 center, float angle)
    {
        _rulerCenter = center;
        _rulerAngle = angle;
        Control.Invalidate();
    }

    /// <summary>Turns the ruler by 15 degree steps when the point is on it; false elsewhere.</summary>
    public bool RotateRuler(Vector2 point, int steps)
    {
        if (!OnRuler(point))
            return false;
        _rulerAngle = CurrentRuler.RotateBy(steps).Angle;
        Control.Invalidate();
        return true;
    }

    /// <summary>Shown part of the document: the whole canvas while cropping, else the crop.</summary>
    private PixelRect View => IsCropping ? Document!.Bounds : Document!.Crop ?? Document.Bounds;

    /// <summary>Enters crop mode; the frame starts at the current crop or the whole canvas.</summary>
    public void BeginCrop()
    {
        if (Document is null || IsCropping || Dragging)
            return;
        CommitText();
        _cropFrame = Document.Crop ?? Document.Bounds;
        Control.Invalidate();
        Changed?.Invoke();
    }

    /// <summary>Leaves crop mode and commits the frame as one undo step.</summary>
    public void ApplyCrop()
    {
        if (_cropFrame is not { } frame || Document is null || Dragging)
            return;
        _cropFrame = null;
        Document.SetCrop(frame);
        Control.Invalidate();
        Changed?.Invoke();
    }

    public void CancelCrop()
    {
        if (!IsCropping || Dragging)
            return;
        _cropFrame = null;
        Control.Invalidate();
        Changed?.Invoke();
    }

    /// <summary>Size of the shown part of the image (the whole canvas while cropping) in DIPs at zoom 100%.</summary>
    public Windows.Foundation.Size ShownSizeDips => new(View.Width / Scale, View.Height / Scale);

    /// <summary>Center of the shown part of the image, in image px.</summary>
    public Vector2 ShownCenter => new(View.X + View.Width / 2f, View.Y + View.Height / 2f);

    /// <summary>Raised when the zoom, the view or the viewport size changes.</summary>
    public event Action? ViewChanged;

    /// <summary>Screen px per image px.</summary>
    public float Zoom => _zoom;

    /// <summary>Image px point at the viewport's top-left.</summary>
    public Vector2 Corner => _corner;

    /// <summary>Physical px per DIP.</summary>
    internal float DisplayScale => Scale;

    /// <summary>Viewport the control fills, in DIPs.</summary>
    public Vector2 ViewportDips => _viewportDips;

    // Viewport physical px of the image origin, in whole px so that zoom 1 maps image pixels to screen pixels.
    private Vector2 Offset => new(MathF.Round(-_corner.X * _zoom), MathF.Round(-_corner.Y * _zoom));

    /// <summary>Shows the image at that zoom with the image px point corner at the viewport's top-left, unclamped (see <see cref="ClampCorner"/>).</summary>
    public void SetView(float zoom, Vector2 corner)
    {
        if (zoom == _zoom && corner == _corner)
            return;
        _zoom = zoom;
        _corner = corner;
        PlaceOverlay();
        Control.Invalidate();
        ViewChanged?.Invoke();
    }

    /// <summary>Sets the viewport size in DIPs; the view keeps its top-left.</summary>
    public void Resize(double width, double height)
    {
        var size = new Vector2((float)width, (float)height);
        if (size == _viewportDips)
            return;
        _viewportDips = size;
        Control.Invalidate();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// Range of the view's top-left at that zoom and the viewport span, in image px. Per axis the view stays inside the shown
    /// image plus an edge; where the shown image fits the viewport, the image (or the crop) is centered and does not scroll.
    /// </summary>
    public (Vector2 Min, Vector2 Max, Vector2 Span) ViewRange(float zoom)
    {
        var span = _viewportDips * Scale / zoom;
        var anchor = Document!.Crop ?? new PixelRect(0, 0, Document.Width, Document.Height);
        var view = View;
        var edge = new Vector2(EdgeDips * Scale);
        var centered = new Vector2(anchor.X + (anchor.Width - span.X) / 2, anchor.Y + (anchor.Height - span.Y) / 2);
        var min = Vector2.Min(centered, new Vector2(view.X, view.Y) - edge);
        var max = Vector2.Max(centered + span, new Vector2(view.Right, view.Bottom) + edge) - span;
        // Half a px of slack: fit zoom makes the image as large as the viewport up to rounding.
        if (view.Width <= span.X + 0.5f)
            min.X = max.X = centered.X;
        if (view.Height <= span.Y + 0.5f)
            min.Y = max.Y = centered.Y;
        return (min, max, span);
    }

    /// <summary>The top-left nearest to corner inside <see cref="ViewRange"/> at that zoom.</summary>
    public Vector2 ClampCorner(float zoom, Vector2 corner)
    {
        var (min, max, _) = ViewRange(zoom);
        return Vector2.Clamp(corner, min, max);
    }

    /// <summary>True while two fingers zoom and pan the view.</summary>
    public bool Pinching => _pinch is not null;

    /// <summary>A finger touched the canvas at a control DIP point; pointer input and the Debug test script both enter here.</summary>
    internal void TouchDown(uint id, Vector2 at, bool shift)
    {
        _touches[id] = at;
        if (_touches.Count == 2 && _pinch is null && (_pendingTouch is not null || !Dragging))
        {
            _pendingTouch = null;
            var (a, b) = TwoTouches();
            // The view's own point, without the whole-pixel snap of the drawing, which the zoom would magnify.
            _pinch = (Vector2.Distance(a, b), _zoom, _corner + (a + b) / 2 * Scale / _zoom);
        }
        else if (_touches.Count == 1 && !Dragging)
            _pendingTouch = (id, at, shift);
    }

    internal void TouchMove(uint id, Vector2 at, bool shift)
    {
        if (!_touches.ContainsKey(id))
            return;
        _touches[id] = at;
        if (_pinch is { } pinch)
        {
            var (a, b) = TwoTouches();
            float distance = Vector2.Distance(a, b);
            float zoom = Math.Clamp(pinch.Zoom * (pinch.Distance > 0 ? distance / pinch.Distance : 1), (float)ZoomMath.Min, (float)ZoomMath.Max);
            // The image point first under the center of the fingers stays under it.
            SetView(zoom, pinch.Image - (a + b) / 2 * Scale / zoom);
        }
        else if (_pendingTouch is { } pending && pending.Id == id)
        {
            if (Vector2.Distance(at, pending.At) < TouchSlopDips)
                return;
            _pendingTouch = null;
            _dragTouch = id;
            Press(ToImage(pending.At), pending.Shift);
            Drag(ToImage(at), shift);
        }
        else if (id == _dragTouch)
            Drag(ToImage(at), shift);
    }

    internal void TouchUp(uint id)
    {
        if (!_touches.Remove(id))
            return;
        if (_pinch is not null)
        {
            if (_touches.Count >= 2)
                return;
            _pinch = null; // a finger still down does nothing until it lifts
            SetView(_zoom, ClampCorner(_zoom, _corner));
        }
        else if (_pendingTouch is { } pending && pending.Id == id)
        {
            _pendingTouch = null;
            Press(ToImage(pending.At), pending.Shift);
            Release();
        }
        else if (id == _dragTouch)
        {
            _dragTouch = null;
            Release();
        }
    }

    private (Vector2, Vector2) TwoTouches()
    {
        using var e = _touches.Values.GetEnumerator();
        e.MoveNext();
        var a = e.Current;
        e.MoveNext();
        return (a, e.Current);
    }

    /// <summary>Runs a selection command outside a drag and redraws; document edits also raise Changed through the document.</summary>
    public void Select(Action<SelectionSession> command)
    {
        if (_selection is null || Dragging)
            return;
        command(_selection);
        Control.Invalidate();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (Dragging) return;
        CommitText();
        Document?.Undo();
    }

    public void Redo()
    {
        if (Dragging) return;
        CommitText();
        Document?.Redo();
    }

    internal Vector2 PointOf(PointerRoutedEventArgs e) => ImagePoint(e);

    /// <summary>Control DIP point of an image px point; it can lie outside the control.</summary>
    internal Windows.Foundation.Point ControlPoint(Vector2 point)
    {
        var p = (point * _zoom + Offset) / Scale;
        return new(p.X, p.Y);
    }

    /// <summary>Image px point of a control DIP point.</summary>
    internal Vector2 ToImage(Windows.Foundation.Point p) => ToImage(new Vector2((float)p.X, (float)p.Y));

    private Vector2 ToImage(Vector2 dip) => (dip * Scale - Offset) / _zoom;

    private (Vector2 Point, float Width)? _sizeHint;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hintTimer;

    /// <summary>Shows a circle of the stroke width plus its value at the point for about a second.</summary>
    public void ShowSizeHint(Vector2 point, float width)
    {
        _sizeHint = (point, width);
        if (_hintTimer is null)
        {
            _hintTimer = Control.DispatcherQueue.CreateTimer();
            _hintTimer.Interval = TimeSpan.FromMilliseconds(900);
            _hintTimer.IsRepeating = false;
            _hintTimer.Tick += (_, _) => { _sizeHint = null; Control.Invalidate(); };
        }
        _hintTimer.Stop();
        _hintTimer.Start();
        Control.Invalidate();
    }

    private Vector2 ImagePoint(PointerRoutedEventArgs e) => ToImage(e.GetCurrentPoint(Control).Position);

    private Vector2 DipOf(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Control).Position;
        return new Vector2((float)p.X, (float)p.Y);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            if (_session is not null && Control.CapturePointer(e.Pointer))
            {
                TouchDown(e.Pointer.PointerId, DipOf(e), IsShift(e));
                e.Handled = true;
            }
            return;
        }
        if (_session is null || Dragging || !e.GetCurrentPoint(Control).Properties.IsLeftButtonPressed)
            return;
        if (!Control.CapturePointer(e.Pointer))
            return;
        Press(ImagePoint(e), IsShift(e), (e.KeyModifiers & VirtualKeyModifiers.Control) != 0);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            TouchMove(e.Pointer.PointerId, DipOf(e), IsShift(e));
            e.Handled = true;
            return;
        }
        if (Tool == EditorTool.Eyedropper && !IsCropping)
            Hover(ImagePoint(e));
        bool ibeam = !IsCropping && (_selectingText || OcrSelection.HitTest(_textLines, ImagePoint(e), HitDips * Scale / _zoom) is not null);
        if (ibeam != _ibeam)
            _host.Cursor = (_ibeam = ibeam) ? IBeam : null;
        if (!Dragging)
            return;
        Drag(ImagePoint(e), IsShift(e));
        e.Handled = true;
    }

    /// <summary>Starts a drag with the current tool (or on a crop handle) at an image pixel point; pointer input and the Debug test script both enter here.</summary>
    public void Press(Vector2 point, bool shift, bool ctrl = false)
    {
        if (_session is null || _selection is null || Dragging)
            return;
        // A pen / highlighter press near an edge snaps instead of moving the ruler.
        if (OnRuler(point) && !IsEditingText
            && !(Tool is EditorTool.Pen or EditorTool.Highlighter && CurrentRuler.EdgeNear(point, RulerSnapDips * Scale / _zoom) is not null))
        {
            _movingRuler = true;
            _rulerGrab = _rulerCenter!.Value - point;
            return;
        }
        // On recognized text a press selects characters whatever the tool; Shift extends the selection.
        if (!IsCropping && OcrSelection.HitTest(_textLines, point, HitDips * Scale / _zoom) is { } caret)
        {
            CommitText();
            _selectingText = true;
            SetTextSelection(shift && _textSelection is { } current ? (current.Anchor, caret) : (caret, caret));
            return;
        }
        SetTextSelection(null);
        if (Tool == EditorTool.Text && !IsCropping)
        {
            TextClick(point);
            return;
        }
        if (Tool == EditorTool.Eyedropper && !IsCropping)
        {
            if (Pick(point) is { } picked)
                ColorPicked?.Invoke(picked with { A = 255 });
            return;
        }
        if (Tool == EditorTool.Bucket && !IsCropping)
        {
            Document?.Fill(point, Color, HitDips * Scale / _zoom);
            return;
        }
        CommitText();
        if (_cropFrame is { } frame)
        {
            _handle = CropMath.HitHandle(frame, point, HandleDips * Scale / _zoom);
            if (_handle is { } h)
            {
                _handleStart = frame;
                _grab = HandlePoint(frame, h) - point;
            }
            return;
        }
        if (Tool == EditorTool.Select)
        {
            SyncHandleSizes(_selection);
            _selection.Begin(point, shift || ctrl);
            Control.Invalidate();
            return;
        }
        BeginSession(point, shift);
    }

    private void BeginSession(Vector2 point, bool shift)
    {
        _session!.Tool = Tool;
        _session.Color = Color;
        _session.Width = StrokeWidth;
        _session.SnapTolerance = HitDips * Scale / _zoom;
        _session.AttachEnabled = AttachEnabled;
        _session.ActiveRuler = _rulerShown ? CurrentRuler : null;
        _session.RulerTolerance = RulerSnapDips * Scale / _zoom;
        _session.Emoji = Emoji;
        _session.EmojiSize = EmojiDips * Scale / _zoom;
        _session.Begin(point, shift);
        Control.Invalidate();
    }

    public void Drag(Vector2 point, bool shift)
    {
        if (_movingRuler)
        {
            _rulerCenter = point + _rulerGrab;
            Control.Invalidate();
            return;
        }
        if (_selectingText)
        {
            if (_textSelection is { } s && OcrSelection.Nearest(_textLines, point) is { } focus)
                SetTextSelection((s.Anchor, focus));
            return;
        }
        if (_handle is { } h && Document is not null)
        {
            var to = point + _grab;
            _cropFrame = CropMath.Resize(_handleStart, h, new PixelPoint((int)MathF.Round(to.X), (int)MathF.Round(to.Y)), Document.Bounds);
            Control.Invalidate();
            CropFrameChanged?.Invoke();
            return;
        }
        if (_selection?.IsActive == true)
        {
            _selection.Move(point, shift);
            Control.Invalidate();
            return;
        }
        if (_session?.IsActive != true)
            return;
        _session.Move(point, shift);
        Control.Invalidate();
    }

    public void Release()
    {
        _handle = null;
        if (_movingRuler)
        {
            _movingRuler = false;
            return;
        }
        if (_selectingText)
        {
            _selectingText = false;
            if (_textSelection is { } s && s.Anchor == s.Focus)
                SetTextSelection(null);
            return;
        }
        if (_selection?.IsActive == true)
        {
            _selection.End();
            Control.Invalidate();
            Changed?.Invoke();
            return;
        }
        if (_session?.IsActive != true)
            return;
        _session.End();
        Control.Invalidate();
    }

    private static bool IsShift(PointerRoutedEventArgs e) => (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;

    // Hit and handle distances are constant on screen, so they shrink in image px as the zoom grows.
    private void SyncHandleSizes(SelectionSession s)
    {
        float dip = Scale / _zoom;
        s.Tolerance = HitDips * dip;
        s.HandleTolerance = HandleDips * dip;
        s.RotateOffset = RotateDips * dip;
    }

    /// <summary>Image px point of a selection handle: "rotate", "start", "end" or a <see cref="CropHandle"/> name; null when absent.</summary>
    internal Vector2? SelectionHandle(string name)
    {
        if (_selection is not { } s)
            return null;
        SyncHandleSizes(s);
        return name switch
        {
            "rotate" => s.RotateHandle,
            "start" => s.EndpointHandles?.Start,
            "end" => s.EndpointHandles?.End,
            _ => s.Frame?.Handle(Enum.Parse<CropHandle>(name, ignoreCase: true)),
        };
    }

    private static Vector2 HandlePoint(PixelRect r, CropHandle h) => h switch
    {
        CropHandle.Left => new(r.X, r.Y + r.Height / 2f),
        CropHandle.Top => new(r.X + r.Width / 2f, r.Y),
        CropHandle.Right => new(r.Right, r.Y + r.Height / 2f),
        CropHandle.Bottom => new(r.X + r.Width / 2f, r.Bottom),
        CropHandle.TopLeft => new(r.X, r.Y),
        CropHandle.TopRight => new(r.Right, r.Y),
        CropHandle.BottomLeft => new(r.X, r.Bottom),
        _ => new(r.Right, r.Bottom),
    };

    /// <summary>Image plus annotations at image pixel size, cut to the crop; BGRA with straight alpha. Commits text being edited first.</summary>
    public CapturedImage? Export()
    {
        if (_image is null || Document is null)
            return null;
        CommitText();
        var crop = Document.Crop ?? Document.Bounds;
        var (bgra, transparent) = Render(crop);
        return new CapturedImage(crop.Width, crop.Height, bgra, transparent);
    }

    /// <summary>The image with its redactions burned in, at its own size (the base image of an editable PNG).</summary>
    public CapturedImage? ExportBase()
    {
        if (_image is null || Document is null)
            return null;
        var (bgra, transparent) = Render(new PixelRect(0, 0, Document.Width, Document.Height), baseOnly: true);
        return new CapturedImage(Document.Width, Document.Height, bgra, transparent);
    }

    /// <summary>What text actions read: the image with its redactions inside the crop (annotations excluded), and where it starts.</summary>
    public (CapturedImage Image, Vector2 Origin)? RecognitionSource()
    {
        if (_image is null || Document is null
            || (Document.Crop ?? Document.Bounds).Intersect(new PixelRect(0, 0, Document.Width, Document.Height)) is not { } area)
            return null;
        var (bgra, transparent) = Render(area, baseOnly: true);
        return (new CapturedImage(area.Width, area.Height, bgra, transparent), new Vector2(area.X, area.Y));
    }

    /// <summary>
    /// Image plus annotations (or only the image and its redactions) of an area at image pixel size, BGRA with straight alpha;
    /// true when any pixel is not opaque.
    /// </summary>
    private (byte[] Bgra, bool Transparent) Render(PixelRect area, bool baseOnly = false)
    {
        using var target = new CanvasRenderTarget(Control.Device, area.Width, area.Height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale; // ClearType fringes on a transparent target
            ds.Transform = Matrix3x2.CreateTranslation(-area.X, -area.Y);
            if (!baseOnly)
                DrawBackground(ds);
            DrawBase(ds);
            if (!baseOnly)
                foreach (var o in Document!.Objects)
                    DrawObject(ds, o);
        }
        return Straight(target);
    }

    /// <summary>The target's pixels as BGRA with straight alpha (Win2D pixels are premultiplied); true when any pixel is not opaque.</summary>
    internal static (byte[] Bgra, bool Transparent) Straight(CanvasRenderTarget target)
    {
        var bgra = target.GetPixelBytes();
        bool transparent = false;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255)
                continue;
            transparent = true;
            for (int c = 0; a > 0 && c < 3; c++)
                bgra[i + c] = (byte)Math.Min(255, (bgra[i + c] * 255 + a / 2) / a);
        }
        return (bgra, transparent);
    }

    /// <summary>Shown color at an image point (null outside the shown area or where nothing is painted).</summary>
    public Rgba? Pick(Vector2 point)
    {
        if (_image is null || Document is null)
            return null;
        if (_composite is null)
        {
            _compositeArea = View;
            _composite = Render(_compositeArea).Bgra;
        }
        return Sample(point);
    }

    // Cached pixels only: drawing must not start another drawing session.
    private Rgba? Sample(Vector2 point) => _composite is null ? null : ColorSample.At(_composite, _compositeArea.Width, _compositeArea.Height,
        (int)MathF.Floor(point.X) - _compositeArea.X, (int)MathF.Floor(point.Y) - _compositeArea.Y);

    /// <summary>Moves the eyedropper loupe to an image point.</summary>
    internal void Hover(Vector2 point)
    {
        _hover = point;
        Pick(point); // builds the pixel cache outside the draw callback
        Control.Invalidate();
    }

    /// <summary>11 x 11 magnified pixels right below the pointer, the center cell framed, and its hex value.</summary>
    private void DrawLoupe(CanvasDrawingSession ds, Vector2 point)
    {
        const int Radius = 5, Cells = 2 * Radius + 1;
        float dip = Scale / _zoom, cell = 8 * dip, size = Cells * cell;
        var origin = point + new Vector2(16 * dip);
        var center = new Vector2(MathF.Floor(point.X), MathF.Floor(point.Y));
        var empty = Windows.UI.Color.FromArgb(255, 64, 64, 64);
        for (int dy = -Radius; dy <= Radius; dy++)
            for (int dx = -Radius; dx <= Radius; dx++)
            {
                var c = Sample(center + new Vector2(dx, dy));
                ds.FillRectangle(origin.X + (dx + Radius) * cell, origin.Y + (dy + Radius) * cell, cell, cell,
                    c is { } v ? ToColor(v with { A = 255 }) : empty);
            }
        ds.DrawRectangle(origin.X, origin.Y, size, size, Microsoft.UI.Colors.White, 2 * dip);
        var mid = origin + new Vector2(Radius * cell);
        ds.DrawRectangle(mid.X, mid.Y, cell, cell, Microsoft.UI.Colors.Black, 2 * dip);
        ds.DrawRectangle(mid.X, mid.Y, cell, cell, Microsoft.UI.Colors.White, dip);
        if (Sample(center) is not { } picked)
            return;
        using var format = new CanvasTextFormat { FontSize = 12 * dip, FontFamily = TextFont };
        var label = new Vector2(origin.X, origin.Y + size + 4 * dip);
        ds.FillRoundedRectangle(label.X, label.Y, size, 20 * dip, 4 * dip, 4 * dip, Windows.UI.Color.FromArgb(220, 32, 32, 32));
        ds.DrawText(ColorSample.Hex(picked), label.X + 6 * dip, label.Y + 2 * dip, Microsoft.UI.Colors.White, format);
    }

    /// <summary>Number of draws and the zoom of the last one (Debug test script).</summary>
    internal int Draws { get; private set; }
    internal float DrawnZoom { get; private set; }

    /// <summary>
    /// Raised at the start of each draw, before the view is read: an animation sets the view of this frame here (and a
    /// changed view asks for the next frame). From CompositionTarget.Rendering an Invalidate comes too late for the frame.
    /// </summary>
    public event Action? BeforeDraw;

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        BeforeDraw?.Invoke();
        if (_image is null || Document is null)
            return;
        Draws++;
        DrawnZoom = _zoom;
        var ds = args.DrawingSession;
        ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale; // same text pixels as the export
        ds.Transform = Matrix3x2.CreateScale(_zoom) * Matrix3x2.CreateTranslation(Offset) * Matrix3x2.CreateScale(1 / Scale);
        var view = View;
        using (ds.CreateLayer(1, new Windows.Foundation.Rect(view.X, view.Y, view.Width, view.Height)))
        {
            DrawBackground(ds);
            // Linear filtering breaks up small text when it scales the image down.
            DrawBase(ds, _zoom < 1 ? CanvasImageInterpolation.HighQualityCubic : CanvasImageInterpolation.Linear);
            // A selection drag shows its transformed copies in place of the originals, keeping the z-order.
            // Attached arrows follow their targets in the preview.
            var live = _selection is { Live.Count: > 0 } sel ? Document.WithAttached(sel.Live).ToDictionary(o => o.Id) : [];
            foreach (var o in Document.Objects)
            {
                if (_session?.Erased.Contains(o.Id) != true && o.Id != _editing?.Id)
                    DrawObject(ds, live.GetValueOrDefault(o.Id, o));
            }
            var dragged = _selection?.Live.Select(o => live.GetValueOrDefault(o.Id, o)) ?? [];
            DrawSnapTargets(ds, dragged.Append(_session?.Live), live);
            DrawHighlights(ds);
            DrawTextSelection(ds);
        }
        if (_session?.Live is { } drawing)
            DrawObject(ds, drawing);
        if (_selection is { } selection && !IsEditingText)
            DrawSelection(ds, selection);
        if (_sizeHint is { } hint)
        {
            float dip = Scale / _zoom;
            ds.DrawCircle(hint.Point, hint.Width / 2, Microsoft.UI.Colors.White, 1.5f * dip);
            ds.DrawCircle(hint.Point, hint.Width / 2, Microsoft.UI.Colors.Black, dip, _dash);
            using var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontSize = 12 * dip };
            var at = hint.Point + new Vector2(hint.Width / 2 + 6 * dip, -8 * dip);
            string size = Loc.Format("WidthPixels", hint.Width);
            ds.DrawText(size, at + new Vector2(dip), Microsoft.UI.Colors.Black, format);
            ds.DrawText(size, at, Microsoft.UI.Colors.White, format);
        }
        if (_rulerShown)
            DrawRuler(ds);
        if (_cropFrame is { } frame)
            DrawCropFrame(ds, frame, view);
        else if (Tool == EditorTool.Eyedropper && _hover is { } hover)
            DrawLoupe(ds, hover);
    }

    /// <summary>The ruler band with ticks every 10 image px (50 / 100 longer) on both long edges, and its angle, at a constant on-screen size.</summary>
    private void DrawRuler(CanvasDrawingSession ds)
    {
        var ruler = CurrentRuler;
        float dip = Scale / _zoom, half = RulerHalfLength, height = ruler.HalfHeight;
        var c = ruler.Center;
        var ink = Windows.UI.Color.FromArgb(255, 40, 40, 40);
        var band = Windows.UI.Color.FromArgb(200, 245, 245, 245);
        var transform = ds.Transform;
        ds.Transform = Matrix3x2.CreateRotation(ruler.Angle * MathF.PI / 180, c) * transform;
        ds.FillRectangle(c.X - half, c.Y - height, 2 * half, 2 * height, band);
        ds.DrawLine(c.X - half, c.Y - height, c.X + half, c.Y - height, ink, dip);
        ds.DrawLine(c.X - half, c.Y + height, c.X + half, c.Y + height, ink, dip);
        float unit = 10; // coarser ticks while zoomed out so they stay apart
        while (unit * _zoom < 5 * Scale)
            unit *= 10;
        for (int i = -(int)(half / unit); i <= (int)(half / unit); i++)
        {
            float x = c.X + i * unit, len = (i % 10 == 0 ? 20 : i % 5 == 0 ? 14 : 8) * dip;
            ds.DrawLine(x, c.Y - height, x, c.Y - height + len, ink, dip);
            ds.DrawLine(x, c.Y + height, x, c.Y + height - len, ink, dip);
        }
        ds.Transform = transform;
        using var format = new CanvasTextFormat
        {
            FontSize = 16 * dip, FontFamily = TextFont,
            HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        var label = new Windows.Foundation.Rect(c.X - 28 * dip, c.Y - 14 * dip, 56 * dip, 28 * dip);
        ds.FillRoundedRectangle(label, 6 * dip, 6 * dip, band);
        ds.DrawText($"{(int)ruler.Angle}\u00B0", label, ink, format);
    }

    /// <summary>
    /// Transform frame (dashed, turned with the object) with its handles, or the end-point handles of a single line / arrow,
    /// and the rubber band, at a constant on-screen size.
    /// </summary>
    private void DrawSelection(CanvasDrawingSession ds, SelectionSession s)
    {
        float dip = Scale / _zoom;
        SyncHandleSizes(s);
        var accent = Accent;
        if (s.Frame is { } frame)
        {
            var transform = ds.Transform;
            ds.Transform = Matrix3x2.CreateRotation(frame.Rotation, frame.Center) * transform;
            // Outside the stroke so the object stays visible; a group frame already includes the widths.
            var single = s.Selected.Count == 1 ? Document!.Objects.FirstOrDefault(o => s.Selected.Contains(o.Id)) : null;
            var pad = new Vector2((single is null ? 0 : EditorGeometry.HalfStroke(single)) + 2 * dip);
            var min = frame.Box.Min - pad;
            var size = frame.Box.Max - frame.Box.Min + 2 * pad;
            ds.DrawRectangle(min.X, min.Y, size.X, size.Y, Microsoft.UI.Colors.White, 1.5f * dip);
            ds.DrawRectangle(min.X, min.Y, size.X, size.Y, accent, 1.5f * dip, _dash);
            ds.Transform = transform;
            var rotate = frame.RotateHandle(s.RotateOffset);
            ds.DrawLine(frame.Handle(CropHandle.Top), rotate, accent, dip);
            var handles = s.HasEdgeHandles ? Enum.GetValues<CropHandle>()
                : [CropHandle.TopLeft, CropHandle.TopRight, CropHandle.BottomLeft, CropHandle.BottomRight];
            foreach (var h in handles)
                DrawHandle(ds, frame.Handle(h), dip, round: false);
            DrawHandle(ds, rotate, dip, round: true);
        }
        else if (s.EndpointHandles is var (start, end))
        {
            DrawHandle(ds, start, dip, round: true);
            DrawHandle(ds, end, dip, round: true);
        }
        if (s.Band is { } band)
        {
            var size = band.Max - band.Min;
            ds.FillRectangle(band.Min.X, band.Min.Y, size.X, size.Y, Windows.UI.Color.FromArgb(48, accent.R, accent.G, accent.B));
            ds.DrawRectangle(band.Min.X, band.Min.Y, size.X, size.Y, accent, dip);
        }
    }

    /// <summary>Accent box around each object a dragged line / arrow end is attached to.</summary>
    private void DrawSnapTargets(CanvasDrawingSession ds, IEnumerable<EditorObject?> dragged, Dictionary<int, EditorObject> live)
    {
        float dip = Scale / _zoom;
        var accent = Accent;
        var targets = dragged.OfType<Shape>().SelectMany(s => new[] { s.StartAttach, s.EndAttach }).OfType<Attachment>().Select(a => a.Target);
        foreach (int id in targets.Distinct())
        {
            if ((live.GetValueOrDefault(id) ?? Document!.Objects.FirstOrDefault(o => o.Id == id)) is not { } target)
                continue;
            var box = EditorGeometry.LocalBox(target);
            var pad = new Vector2(EditorGeometry.HalfStroke(target) + 3 * dip);
            var transform = ds.Transform;
            ds.Transform = Matrix3x2.CreateRotation(target.Rotation, (box.Min + box.Max) / 2) * transform;
            var min = box.Min - pad;
            var size = box.Max - box.Min + 2 * pad;
            ds.DrawRectangle(min.X, min.Y, size.X, size.Y, accent, 2 * dip);
            ds.Transform = transform;
        }
    }

    private static void DrawHandle(CanvasDrawingSession ds, Vector2 p, float dip, bool round)
    {
        float half = HandleDips / 2 * dip;
        if (round)
        {
            ds.FillCircle(p, half, Microsoft.UI.Colors.White);
            ds.DrawCircle(p, half, Microsoft.UI.Colors.Black, dip);
            return;
        }
        ds.FillRectangle(p.X - half, p.Y - half, 2 * half, 2 * half, Microsoft.UI.Colors.White);
        ds.DrawRectangle(p.X - half, p.Y - half, 2 * half, 2 * half, Microsoft.UI.Colors.Black, dip);
    }

    /// <summary>Dims the canvas outside the frame and draws its border and eight handles at a constant on-screen size.</summary>
    private void DrawCropFrame(CanvasDrawingSession ds, PixelRect f, PixelRect b)
    {
        var dim = Windows.UI.Color.FromArgb(140, 0, 0, 0);
        ds.FillRectangle(b.X, b.Y, b.Width, f.Y - b.Y, dim);
        ds.FillRectangle(b.X, f.Bottom, b.Width, b.Bottom - f.Bottom, dim);
        ds.FillRectangle(b.X, f.Y, f.X - b.X, f.Height, dim);
        ds.FillRectangle(f.Right, f.Y, b.Right - f.Right, f.Height, dim);
        float dip = Scale / _zoom;
        ds.DrawRectangle(f.X, f.Y, f.Width, f.Height, Microsoft.UI.Colors.White, 1.5f * dip);
        foreach (var h in Enum.GetValues<CropHandle>())
            DrawHandle(ds, HandlePoint(f, h), dip, round: false);
    }

    /// <summary>The captured image and its redaction boxes.</summary>
    private void DrawBase(CanvasDrawingSession ds, CanvasImageInterpolation interpolation = CanvasImageInterpolation.Linear)
    {
        var bounds = _image!.Bounds;
        ds.DrawImage(_image, bounds, bounds, 1, interpolation);
        foreach (var r in Document!.Redactions)
            ds.FillRectangle(r.X, r.Y, r.Width, r.Height, Microsoft.UI.Colors.Black);
    }

    private IReadOnlyList<FloatRect> _highlights = [];

    /// <summary>Boxes of recognized words and QR codes (text actions), in image px; shown on screen only.</summary>
    public IReadOnlyList<FloatRect> Highlights
    {
        get => _highlights;
        set
        {
            _highlights = value;
            Control.Invalidate();
        }
    }

    private void DrawHighlights(CanvasDrawingSession ds)
    {
        if (_highlights.Count == 0)
            return;
        // Snipping Tool look (measured 11.2608.41.0): gray 77 at alpha 157 over the view (white 255 -> 146, dark 32 -> 60),
        // highlights cut out as rounded boxes in image pixels.
        const float radius = 5;
        var view = View;
        var boxes = _highlights.Select(r => CanvasGeometry.CreateRoundedRectangle(ds, r.Min.X, r.Min.Y, r.Max.X - r.Min.X, r.Max.Y - r.Min.Y, radius, radius)).ToArray();
        using var holes = CanvasGeometry.CreateGroup(ds, boxes, CanvasFilledRegionDetermination.Winding);
        using var area = CanvasGeometry.CreateRectangle(ds, view.X, view.Y, view.Width, view.Height);
        using var dim = area.CombineWith(holes, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        ds.FillGeometry(dim, Windows.UI.Color.FromArgb(157, 77, 77, 77));
        foreach (var box in boxes)
            box.Dispose();
    }

    // Accent at alpha 96, 2 px taller than the glyph boxes.
    private void DrawTextSelection(CanvasDrawingSession ds)
    {
        if (_textSelection is not { } s)
            return;
        var accent = Accent;
        foreach (var r in OcrSelection.Boxes(_textLines, s.Anchor, s.Focus))
            ds.FillRectangle(r.Min.X, r.Min.Y - 2, r.Max.X - r.Min.X, r.Max.Y - r.Min.Y + 4, Windows.UI.Color.FromArgb(96, accent.R, accent.G, accent.B));
    }

    /// <summary>Canvas color under the image; shows where the image is transparent or the canvas grew past it.</summary>
    private void DrawBackground(CanvasDrawingSession ds)
    {
        if (Document!.Background is { } background)
        {
            var b = Document.Bounds;
            ds.FillRectangle(b.X, b.Y, b.Width, b.Height, ToColor(background));
        }
    }

    private void DrawObject(CanvasDrawingSession ds, EditorObject o)
    {
        if (o.Rotation != 0)
        {
            var transform = ds.Transform;
            var box = EditorGeometry.LocalBox(o);
            ds.Transform = Matrix3x2.CreateRotation(o.Rotation, (box.Min + box.Max) / 2) * transform;
            DrawLocal(ds, o);
            ds.Transform = transform;
        }
        else
            DrawLocal(ds, o);
    }

    /// <summary>Draws the object unrotated, in its local frame.</summary>
    private void DrawLocal(CanvasDrawingSession ds, EditorObject o)
    {
        var color = ToColor(o.Color);
        switch (o)
        {
            case Stroke s:
                DrawStroke(ds, s);
                break;
            case Shape { Kind: ShapeKind.Rectangle } s:
            {
                var min = Vector2.Min(s.Start, s.End);
                var size = Vector2.Abs(s.End - s.Start);
                if (s.Fill is { } fill)
                    ds.FillRectangle(min.X, min.Y, size.X, size.Y, ToColor(fill));
                ds.DrawRectangle(min.X, min.Y, size.X, size.Y, color, s.Width);
                break;
            }
            case Shape { Kind: ShapeKind.Ellipse } s:
            {
                var radius = Vector2.Abs(s.End - s.Start) / 2;
                if (s.Fill is { } fill)
                    ds.FillEllipse((s.Start + s.End) / 2, radius.X, radius.Y, ToColor(fill));
                ds.DrawEllipse((s.Start + s.End) / 2, radius.X, radius.Y, color, s.Width);
                break;
            }
            case Shape { Kind: ShapeKind.Line } s:
                ds.DrawLine(s.Start, s.End, color, s.Width, _round);
                break;
            case TextObject t:
            {
                using var layout = TextLayout(ds, t.Text, t.Width);
                ds.DrawTextLayout(layout, t.Position, color);
                break;
            }
            case EmojiObject e:
                EmojiFont.Draw(ds, e.Emoji, new Windows.Foundation.Rect(e.Position.X, e.Position.Y, e.Width, e.Width));
                break;
            case Shape s:
            {
                var (left, right) = EditorGeometry.ArrowHead(s.Start, s.End, s.Width);
                ds.DrawLine(s.Start, s.End, color, s.Width, _round);
                ds.DrawLine(s.End, left, color, s.Width, _round);
                ds.DrawLine(s.End, right, color, s.Width, _round);
                break;
            }
        }
    }

    internal static void DrawStroke(CanvasDrawingSession ds, Stroke s)
    {
        var color = ToColor(s.Color);
        if (s.Points.Length == 1)
        {
            ds.FillCircle(s.Points[0], s.Width / 2, color);
            return;
        }
        // One geometry per stroke: a translucent stroke does not darken where it crosses itself.
        using var builder = new CanvasPathBuilder(ds);
        builder.BeginFigure(s.Points[0]);
        for (int i = 1; i < s.Points.Length; i++)
            builder.AddLine(s.Points[i]);
        builder.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(builder);
        ds.DrawGeometry(geometry, color, s.Width, _round);
    }

    /// <summary>Releases the Win2D resources and returns the device's image memory to the system; the control must not be reused.</summary>
    public void Close()
    {
        var device = Control.Device;
        _textBox = null; // text still being edited is dropped with the capture
        _composite = null;
        _overlay.Children.Clear();
        Control.RemoveFromVisualTree();
        _image?.Dispose();
        _image = null;
        _imageBytes = null;
        device?.Trim(); // the shared device stays alive; Trim frees its cached allocations
    }
}
