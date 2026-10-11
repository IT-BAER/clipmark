using System.Numerics;
using Clipmark.App.Capture;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Clipmark.App.Editor;

/// <summary>Win2D surface for one capture. Draws in image pixel space: one image pixel = one physical pixel at zoom 1.</summary>
internal sealed class EditorCanvas
{
    private const float EdgeDips = 16;
    private const float HandleDips = 8;

    private readonly string _path;
    private readonly CanvasStrokeStyle _round = new()
    {
        StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round,
    };
    private CanvasBitmap? _image;
    private ToolSession? _session;
    // Image origin and control size in physical px; the control can be larger than the document.
    private Vector2 _origin;
    private Vector2 _size;
    private float _zoom = 1;
    // Crop mode: the frame being edited (null outside crop mode) and the handle being dragged.
    private PixelRect? _cropFrame;
    private CropHandle? _handle;
    private PixelRect _handleStart;
    private Vector2 _grab;

    public CanvasControl Control { get; }
    public EditorDocument? Document { get; private set; }
    public EditorTool Tool { get; set; }
    public Rgba Color { get; set; }
    public float StrokeWidth { get; set; }
    public bool IsCropping => _cropFrame is not null;

    /// <summary>Raised when the document is created or changes (add, remove, undo, redo, crop), or crop mode starts or ends.</summary>
    public event Action? Changed;

    public EditorCanvas(string path)
    {
        _path = path;
        Control = new CanvasControl
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(Control, "Captured image");
        Control.CreateResources += (s, e) => e.TrackAsyncAction(LoadAsync(s).AsAsyncAction());
        Control.Draw += OnDraw;
        Control.PointerPressed += OnPointerPressed;
        Control.PointerMoved += OnPointerMoved;
        Control.PointerReleased += (_, e) => Control.ReleasePointerCapture(e.Pointer);
        Control.PointerCaptureLost += (_, _) => Release();
    }

    internal static Windows.UI.Color ToColor(Rgba c) => Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);

    private async Task LoadAsync(CanvasControl sender)
    {
        using var stream = File.OpenRead(_path).AsRandomAccessStream();
        _image = await CanvasBitmap.LoadAsync(sender, stream, 96);
        var size = _image.SizeInPixels;
        if (Document is null)
        {
            Document = new EditorDocument((int)size.Width, (int)size.Height);
            Document.Changed += () =>
            {
                if (IsCropping)
                    _cropFrame = Document.Crop ?? Document.Bounds;
                Control.Invalidate();
                Changed?.Invoke();
            };
            _session = new ToolSession(Document);
            Changed?.Invoke();
        }
    }

    private float Scale => Control.Dpi / 96;

    private bool Dragging => _session?.IsActive == true || _handle is not null;

    /// <summary>Shown part of the document: the whole canvas while cropping, else the crop.</summary>
    private PixelRect View => IsCropping ? Document!.Bounds : Document!.Crop ?? Document.Bounds;

    /// <summary>Enters crop mode; the frame starts at the current crop or the whole canvas.</summary>
    public void BeginCrop()
    {
        if (Document is null || IsCropping || Dragging)
            return;
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

    /// <summary>Image origin inside the control, in DIPs.</summary>
    public Windows.Foundation.Point ImageOrigin => new(_origin.X / Scale, _origin.Y / Scale);

    /// <summary>
    /// Sizes the control to cover the viewport (image centered) and the document bounds plus an edge; false when unchanged.
    /// Below zoom 1 the control stops growing with the viewport to bound GPU memory.
    /// </summary>
    public bool Layout(double viewportWidth, double viewportHeight, float zoom)
    {
        if (Document is null)
            return false;
        if (zoom != _zoom)
        {
            _zoom = zoom;
            Control.Invalidate();
        }
        float fill = Scale / Math.Max(zoom, 1);
        int fw = (int)(viewportWidth * fill), fh = (int)(viewportHeight * fill);
        var anchor = Document.Crop ?? new PixelRect(0, 0, Document.Width, Document.Height);
        int fx = anchor.X + (int)Math.Floor((anchor.Width - fw) / 2.0), fy = anchor.Y + (int)Math.Floor((anchor.Height - fh) / 2.0);
        var b = View;
        int edge = (int)MathF.Ceiling(EdgeDips * Scale);
        int left = Math.Min(fx, b.X - edge), top = Math.Min(fy, b.Y - edge);
        int right = Math.Max(fx + fw, b.Right + edge), bottom = Math.Max(fy + fh, b.Bottom + edge);
        var origin = new Vector2(-left, -top);
        var size = new Vector2(right - left, bottom - top);
        if (origin == _origin && size == _size)
            return false;
        _origin = origin;
        _size = size;
        Control.Width = size.X / Scale;
        Control.Height = size.Y / Scale;
        Control.Invalidate();
        return true;
    }

    public void Undo()
    {
        if (!Dragging) Document?.Undo();
    }

    public void Redo()
    {
        if (!Dragging) Document?.Redo();
    }

    private Vector2 ImagePoint(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Control).Position;
        return new Vector2((float)p.X * Scale, (float)p.Y * Scale) - _origin;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session is null || Dragging || !e.GetCurrentPoint(Control).Properties.IsLeftButtonPressed)
            return;
        if (!Control.CapturePointer(e.Pointer))
            return;
        Press(ImagePoint(e), IsShift(e));
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!Dragging)
            return;
        Drag(ImagePoint(e), IsShift(e));
        e.Handled = true;
    }

    /// <summary>Starts a drag with the current tool (or on a crop handle) at an image pixel point; pointer input and the Debug test script both enter here.</summary>
    public void Press(Vector2 point, bool shift)
    {
        if (_session is null || Dragging)
            return;
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
        _session.Tool = Tool;
        _session.Color = Color;
        _session.Width = StrokeWidth;
        _session.Begin(point, shift);
        Control.Invalidate();
    }

    public void Drag(Vector2 point, bool shift)
    {
        if (_handle is { } h && Document is not null)
        {
            var to = point + _grab;
            _cropFrame = CropMath.Resize(_handleStart, h, new PixelPoint((int)MathF.Round(to.X), (int)MathF.Round(to.Y)), Document.Bounds);
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
        if (_session?.IsActive != true)
            return;
        _session.End();
        Control.Invalidate();
    }

    private static bool IsShift(PointerRoutedEventArgs e) => (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;

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

    /// <summary>Image plus annotations at image pixel size, cut to the crop; BGRA with straight alpha.</summary>
    public CapturedImage? Export()
    {
        if (_image is null || Document is null)
            return null;
        var crop = Document.Crop ?? Document.Bounds;
        using var target = new CanvasRenderTarget(Control.Device, crop.Width, crop.Height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            ds.Transform = Matrix3x2.CreateTranslation(-crop.X, -crop.Y);
            ds.DrawImage(_image);
            foreach (var o in Document.Objects)
                DrawObject(ds, o);
        }
        var bgra = target.GetPixelBytes();
        bool transparent = false;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255)
                continue;
            transparent = true;
            for (int c = 0; a > 0 && c < 3; c++)
                bgra[i + c] = (byte)Math.Min(255, (bgra[i + c] * 255 + a / 2) / a); // Win2D pixels are premultiplied
        }
        return new CapturedImage(crop.Width, crop.Height, bgra, transparent);
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_image is null || Document is null)
            return;
        var ds = args.DrawingSession;
        ds.Transform = Matrix3x2.CreateTranslation(_origin) * Matrix3x2.CreateScale(1 / Scale);
        var view = View;
        using (ds.CreateLayer(1, new Windows.Foundation.Rect(view.X, view.Y, view.Width, view.Height)))
        {
            ds.DrawImage(_image);
            foreach (var o in Document.Objects)
            {
                if (_session?.Erased.Contains(o.Id) != true)
                    DrawObject(ds, o);
            }
        }
        if (_session?.Live is { } live)
            DrawObject(ds, live);
        if (_cropFrame is { } frame)
            DrawCropFrame(ds, frame, view);
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
        float half = HandleDips / 2 * dip;
        foreach (var h in Enum.GetValues<CropHandle>())
        {
            var p = HandlePoint(f, h);
            ds.FillRectangle(p.X - half, p.Y - half, 2 * half, 2 * half, Microsoft.UI.Colors.White);
            ds.DrawRectangle(p.X - half, p.Y - half, 2 * half, 2 * half, Microsoft.UI.Colors.Black, dip);
        }
    }

    private void DrawObject(CanvasDrawingSession ds, EditorObject o)
    {
        var color = ToColor(o.Color);
        switch (o)
        {
            case Stroke { Points.Length: 1 } s:
                ds.FillCircle(s.Points[0], s.Width / 2, color);
                break;
            case Stroke s:
            {
                // One geometry per stroke: a translucent stroke does not darken where it crosses itself.
                using var builder = new CanvasPathBuilder(ds);
                builder.BeginFigure(s.Points[0]);
                for (int i = 1; i < s.Points.Length; i++)
                    builder.AddLine(s.Points[i]);
                builder.EndFigure(CanvasFigureLoop.Open);
                using var geometry = CanvasGeometry.CreatePath(builder);
                ds.DrawGeometry(geometry, color, s.Width, _round);
                break;
            }
            case Shape { Kind: ShapeKind.Rectangle } s:
            {
                var min = Vector2.Min(s.Start, s.End);
                var size = Vector2.Abs(s.End - s.Start);
                ds.DrawRectangle(min.X, min.Y, size.X, size.Y, color, s.Width);
                break;
            }
            case Shape { Kind: ShapeKind.Ellipse } s:
            {
                var radius = Vector2.Abs(s.End - s.Start) / 2;
                ds.DrawEllipse((s.Start + s.End) / 2, radius.X, radius.Y, color, s.Width);
                break;
            }
            case Shape { Kind: ShapeKind.Line } s:
                ds.DrawLine(s.Start, s.End, color, s.Width, _round);
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

    /// <summary>Releases the Win2D resources and returns the device's image memory to the system; the control must not be reused.</summary>
    public void Close()
    {
        var device = Control.Device;
        Control.RemoveFromVisualTree();
        _image?.Dispose();
        _image = null;
        device?.Trim(); // the shared device stays alive; Trim frees its cached allocations
    }
}
