using System.Collections.Immutable;
using System.Numerics;

namespace Clipmark.Core.Editor;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);

public enum StrokeKind { Pen, Highlighter }

public enum ShapeKind { Rectangle, Ellipse, Line, Arrow }

/// <summary>Annotation in image pixel space; Id is unique within its document.</summary>
public abstract record EditorObject(int Id, Rgba Color, float Width);

public sealed record Stroke(int Id, StrokeKind Kind, Rgba Color, float Width, ImmutableArray<Vector2> Points)
    : EditorObject(Id, Color, Width);

public sealed record Shape(int Id, ShapeKind Kind, Rgba Color, float Width, Vector2 Start, Vector2 End)
    : EditorObject(Id, Color, Width);
