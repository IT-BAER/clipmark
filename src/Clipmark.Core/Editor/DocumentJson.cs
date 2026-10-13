using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Clipmark.Core.Capture;

namespace Clipmark.Core.Editor;

/// <summary>
/// Versioned JSON of a document's current state (objects, crop, background; no undo history), without reflection (Native AOT).
/// Parse accepts a whole valid document or throws <see cref="InvalidDataException"/>, never returns a partial one.
/// </summary>
public static class DocumentJson
{
    public const int Version = 1;
    private const string Format = "clipmark";
    private const int Limit = 1 << 20;
    private const int MaxCanvas = 16384; // usual Direct2D bitmap limit: larger canvases cannot be rendered or exported

    public static string Serialize(EditorDocument doc)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", Version);
            w.WriteNumber("width", doc.Width);
            w.WriteNumber("height", doc.Height);
            if (doc.Crop is { } c)
            {
                w.WriteStartArray("crop");
                w.WriteNumberValue(c.X);
                w.WriteNumberValue(c.Y);
                w.WriteNumberValue(c.Width);
                w.WriteNumberValue(c.Height);
                w.WriteEndArray();
            }
            if (doc.Background is { } bg) WriteColor(w, "background", bg);
            w.WriteStartArray("objects");
            foreach (var o in doc.Objects) WriteObject(w, o);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static EditorDocument Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new InvalidDataException(CoreText.T("EditableDataNotJson", "The editable data is not valid JSON."), e);
        }
        try { return Read(doc); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) // e.g. a lone surrogate in a string
        {
            throw new InvalidDataException(Invalid(e.Message), e);
        }
        finally { doc.Dispose(); }
    }

    private static EditorDocument Read(JsonDocument doc)
    {
        {
            var root = doc.RootElement;
            if (Required(root, "format", JsonValueKind.String).GetString() != Format)
                throw Bad("not Clipmark data");
            int version = Int(Required(root, "version", JsonValueKind.Number));
            if (version != Version)
                throw Bad(version > Version ? $"made by a newer Clipmark (format version {version})" : $"unknown format version {version}");
            int width = Int(Required(root, "width", JsonValueKind.Number));
            int height = Int(Required(root, "height", JsonValueKind.Number));
            if (width is <= 0 or > Limit || height is <= 0 or > Limit) throw Bad("image size out of range");

            PixelRect? crop = null;
            if (Optional(root, "crop") is { } ce)
            {
                var v = Ints(ce, 4);
                if (v[2] <= 0 || v[3] <= 0) throw Bad("crop size must be positive");
                crop = new PixelRect(v[0], v[1], v[2], v[3]);
            }
            Rgba? background = Optional(root, "background") is { } be ? ReadColor(be) : null;

            var objects = ImmutableList.CreateBuilder<EditorObject>();
            var ids = new HashSet<int>();
            foreach (var oe in Required(root, "objects", JsonValueKind.Array).EnumerateArray())
            {
                var o = ReadObject(oe);
                if (o.Id <= 0 || !ids.Add(o.Id)) throw Bad($"object id {o.Id} is not positive or not unique");
                objects.Add(o);
            }
            foreach (var o in objects)
                if (o is Shape s && (Dangling(s.StartAttach) || Dangling(s.EndAttach)))
                    throw Bad($"object {s.Id} is attached to a missing object");
            var restored = EditorDocument.Restore(width, height, objects.ToImmutable(), crop, background);
            if (restored.Crop is { } c && !restored.Bounds.Contains(c)) throw Bad("crop lies outside the canvas");
            if (restored.Bounds.Width > MaxCanvas || restored.Bounds.Height > MaxCanvas) throw Bad("canvas too large");
            return restored;

            bool Dangling(Attachment? a) => a is { } x && (x.Target == 0 || !ids.Contains(x.Target));
        }
    }

    private static void WriteObject(Utf8JsonWriter w, EditorObject o)
    {
        w.WriteStartObject();
        w.WriteString("type", o switch { Stroke => "stroke", Shape => "shape", TextObject => "text", _ => throw new ArgumentException(o.GetType().Name) });
        w.WriteNumber("id", o.Id);
        WriteColor(w, "color", o.Color);
        w.WriteNumber("width", o.Width);
        w.WriteNumber("rotation", o.Rotation);
        switch (o)
        {
            case Stroke s:
                w.WriteString("kind", s.Kind.ToString().ToLowerInvariant());
                w.WriteStartArray("points");
                foreach (var p in s.Points)
                {
                    w.WriteNumberValue(p.X);
                    w.WriteNumberValue(p.Y);
                }
                w.WriteEndArray();
                break;
            case Shape s:
                w.WriteString("kind", s.Kind.ToString().ToLowerInvariant());
                WriteVector(w, "start", s.Start);
                WriteVector(w, "end", s.End);
                if (s.Fill is { } fill) WriteColor(w, "fill", fill);
                WriteAttach(w, "startAttach", s.StartAttach);
                WriteAttach(w, "endAttach", s.EndAttach);
                break;
            case TextObject t:
                WriteVector(w, "position", t.Position);
                w.WriteString("text", t.Text);
                WriteVector(w, "unitSize", t.UnitSize);
                break;
        }
        w.WriteEndObject();
    }

    private static EditorObject ReadObject(JsonElement e)
    {
        int id = Int(Required(e, "id", JsonValueKind.Number));
        var color = ReadColor(Required(e, "color", JsonValueKind.Array));
        float width = Float(Required(e, "width", JsonValueKind.Number));
        if (width <= 0) throw Bad($"object {id} width must be positive");
        float rotation = Float(Required(e, "rotation", JsonValueKind.Number));
        EditorObject o = Required(e, "type", JsonValueKind.String).GetString() switch
        {
            "stroke" => ReadStroke(e, id, color, width),
            "shape" => ReadShape(e, id, color, width),
            "text" => ReadText(e, id, color, width),
            var type => throw Bad($"unknown object type '{type}'"),
        };
        return o with { Rotation = rotation };
    }

    private static TextObject ReadText(JsonElement e, int id, Rgba color, float width)
    {
        var unit = ReadVector(Required(e, "unitSize", JsonValueKind.Array));
        if (unit.X <= 0 || unit.Y <= 0) throw Bad($"object {id} text size must be positive");
        return new TextObject(id, color, width, ReadVector(Required(e, "position", JsonValueKind.Array)),
            Required(e, "text", JsonValueKind.String).GetString()!, unit);
    }

    private static Stroke ReadStroke(JsonElement e, int id, Rgba color, float width)
    {
        var kind = Kind<StrokeKind>(e);
        var flat = Required(e, "points", JsonValueKind.Array);
        int n = flat.GetArrayLength();
        if (n == 0 || n % 2 != 0) throw Bad($"object {id} needs x, y point pairs");
        var points = ImmutableArray.CreateBuilder<Vector2>(n / 2);
        for (int i = 0; i < n; i += 2)
            points.Add(new Vector2(Float(flat[i]), Float(flat[i + 1])));
        return new Stroke(id, kind, color, width, points.MoveToImmutable());
    }

    private static Shape ReadShape(JsonElement e, int id, Rgba color, float width)
    {
        var kind = Kind<ShapeKind>(e);
        var fill = Optional(e, "fill") is { } fe ? ReadColor(fe) : (Rgba?)null;
        var start = ReadAttach(Optional(e, "startAttach"));
        var end = ReadAttach(Optional(e, "endAttach"));
        bool segment = kind is ShapeKind.Line or ShapeKind.Arrow;
        if (fill is not null && segment) throw Bad($"object {id}: lines and arrows have no fill");
        if ((start is not null || end is not null) && !segment) throw Bad($"object {id}: only lines and arrows attach");
        return new Shape(id, kind, color, width, ReadVector(Required(e, "start", JsonValueKind.Array)), ReadVector(Required(e, "end", JsonValueKind.Array)))
        {
            Fill = fill,
            StartAttach = start,
            EndAttach = end,
        };
    }

    private static T Kind<T>(JsonElement e) where T : struct, Enum =>
        Enum.TryParse<T>(Required(e, "kind", JsonValueKind.String).GetString(), ignoreCase: true, out var k) && Enum.IsDefined(k)
            ? k : throw Bad($"unknown {typeof(T).Name}");

    private static void WriteColor(Utf8JsonWriter w, string name, Rgba c)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(c.R);
        w.WriteNumberValue(c.G);
        w.WriteNumberValue(c.B);
        w.WriteNumberValue(c.A);
        w.WriteEndArray();
    }

    private static Rgba ReadColor(JsonElement e)
    {
        var v = Ints(e, 4);
        if (v.Any(x => x is < 0 or > 255)) throw Bad("color channels must be 0-255");
        return new Rgba((byte)v[0], (byte)v[1], (byte)v[2], (byte)v[3]);
    }

    private static void WriteVector(Utf8JsonWriter w, string name, Vector2 v)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(v.X);
        w.WriteNumberValue(v.Y);
        w.WriteEndArray();
    }

    private static Vector2 ReadVector(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 ? new Vector2(Float(e[0]), Float(e[1])) : throw Bad("expected [x, y]");

    private static void WriteAttach(Utf8JsonWriter w, string name, Attachment? a)
    {
        if (a is not { } x) return;
        w.WriteStartObject(name);
        w.WriteNumber("target", x.Target);
        WriteVector(w, "anchor", x.Anchor);
        w.WriteEndObject();
    }

    private static Attachment? ReadAttach(JsonElement? e) => e is { } x
        ? new Attachment(Int(Required(x, "target", JsonValueKind.Number)), ReadVector(Required(x, "anchor", JsonValueKind.Array)))
        : null;

    private static JsonElement Required(JsonElement e, string name, JsonValueKind kind) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == kind
            ? v : throw Bad($"'{name}' is missing or not a {kind.ToString().ToLowerInvariant()}");

    /// <summary>The property, or null when it is absent or JSON null.</summary>
    private static JsonElement? Optional(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static int[] Ints(JsonElement e, int count) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == count
            ? [.. e.EnumerateArray().Select(Int)] : throw Bad($"expected {count} integers");

    private static int Int(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int i) ? i : throw Bad("expected an integer");

    // Coordinates, sizes and the image size stay within ±1M px, so bounds and render targets cannot overflow.
    private static float Float(JsonElement e) =>
        e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out float f) && float.IsFinite(f) && MathF.Abs(f) <= Limit
            ? f : throw Bad("expected a number within ±1048576");

    private static InvalidDataException Bad(string what) => new(Invalid(what + "."));

    /// <summary>The detail stays English: it names JSON fields.</summary>
    private static string Invalid(string detail) =>
        string.Format(CultureInfo.CurrentCulture, CoreText.T("EditableDataInvalid", "The editable data is invalid: {0}"), detail);
}
