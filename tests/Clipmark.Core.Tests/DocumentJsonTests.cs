using System.Numerics;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class DocumentJsonTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Half = new(10, 20, 30, 128);

    // Every object type and optional field: pen + highlighter, all shape kinds, fill, both attachments, rotation, text.
    private static EditorDocument Full()
    {
        var doc = new EditorDocument(400, 300);
        int box = doc.NewId(), ring = doc.NewId();
        doc.Add(new Shape(box, ShapeKind.Rectangle, Red, 3.5f, new(50.25f, 40), new(150, 120.75f)) { Fill = Half, Rotation = 0.3f });
        doc.Add(new Shape(ring, ShapeKind.Ellipse, Half, 2, new(200, 150), new(260, 190)));
        doc.Add(new Shape(doc.NewId(), ShapeKind.Line, Red, 1, new(-20, 5), new(30, 7)));
        var arrow = new Shape(doc.NewId(), ShapeKind.Arrow, Red, 4, new(100, 80), new(230, 170))
        {
            StartAttach = new Attachment(box, new(0.5f, 0.5f)),
            EndAttach = new Attachment(ring, new(0.5f, 0.5f)),
        };
        doc.Add(arrow);
        doc.Add(new Stroke(doc.NewId(), StrokeKind.Pen, Red, 4, [new(1.1f, 2.2f), new(3.3f, 4.4f), new(420.5f, 9)]));
        doc.Add(new Stroke(doc.NewId(), StrokeKind.Highlighter, Half, 16, [new(5, 5), new(80, 5)]) { Rotation = -1.25f });
        doc.Add(new TextObject(doc.NewId(), Red, 24, new(10, 200), "Hallo \"Welt\"\nä € 😀", new(4.5f, 1.3f)) { Rotation = 0.1f });
        doc.Fill(new(-100, -100), new Rgba(1, 2, 3, 255), 1);
        doc.SetCrop(new PixelRect(5, 6, 300, 200));
        return doc;
    }

    [Fact]
    public void A_document_round_trips_with_every_object_type_and_field()
    {
        var doc = Full();
        var back = DocumentJson.Parse(DocumentJson.Serialize(doc));

        Assert.Equal(doc.Width, back.Width);
        Assert.Equal(doc.Height, back.Height);
        Assert.Equal(doc.Objects, back.Objects);
        Assert.Equal(doc.Crop, back.Crop);
        Assert.Equal(doc.Bounds, back.Bounds);
        Assert.Equal(doc.Background, back.Background);
    }

    [Fact]
    public void A_loaded_document_is_clean_has_no_history_and_continues_ids()
    {
        var doc = Full();
        var back = DocumentJson.Parse(DocumentJson.Serialize(doc));

        Assert.False(back.IsDirty);
        Assert.False(back.CanUndo);
        Assert.False(back.CanRedo);
        Assert.Equal(doc.Objects.Max(o => o.Id) + 1, back.NewId());
    }

    [Fact]
    public void An_attached_end_still_follows_its_target_after_loading()
    {
        var back = DocumentJson.Parse(DocumentJson.Serialize(Full()));
        var box = back.Objects.OfType<Shape>().Single(s => s.Kind == ShapeKind.Rectangle);
        back.Replace([box.Offset(new(10, 0))]);
        var arrow = back.Objects.OfType<Shape>().Single(s => s.Kind == ShapeKind.Arrow);
        Assert.Equal(EditorGeometry.AnchorPoint(back.Objects.Single(o => o.Id == box.Id), new(0.5f, 0.5f)), arrow.Start);
    }

    [Fact]
    public void An_empty_document_round_trips()
    {
        var back = DocumentJson.Parse(DocumentJson.Serialize(new EditorDocument(10, 20)));
        Assert.Equal((10, 20), (back.Width, back.Height));
        Assert.Empty(back.Objects);
        Assert.Null(back.Crop);
        Assert.Null(back.Background);
        Assert.Equal(1, back.NewId());
    }

    private const string Head = """{"format":"clipmark","version":1,"width":10,"height":10,""";
    private const string Pen = """{"type":"stroke","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"pen","points":[1,1,2,2]}""";

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"format":"other","version":1,"width":10,"height":10,"objects":[]}""")]
    [InlineData("""{"format":"clipmark","version":2,"width":10,"height":10,"objects":[]}""")]
    [InlineData("""{"format":"clipmark","version":1,"width":0,"height":10,"objects":[]}""")]
    [InlineData(Head + "\"objects\":[" + Pen + "," + Pen + "]}")] // duplicate id
    [InlineData(Head + "\"objects\":[" + """{"type":"blob","id":1,"color":[1,2,3,255],"width":2,"rotation":0}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"stroke","id":1,"color":[1,2,3,256],"width":2,"rotation":0,"kind":"pen","points":[1,1]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"stroke","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"pen","points":[1,1,2]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"stroke","id":1,"color":[1,2,3,255],"width":0,"rotation":0,"kind":"pen","points":[1,1]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"stroke","id":1,"color":[1,2,3,255],"width":2,"rotation":1e50,"kind":"pen","points":[1,1]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"shape","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"arrow","start":[0,0],"end":[5,5],"endAttach":{"target":7,"anchor":[0.5,0.5]}}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"shape","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"hexagon","start":[0,0],"end":[5,5]}""" + "]}")]
    [InlineData(Head + "\"crop\":[0,0,0,5],\"objects\":[]}")]
    [InlineData(Head + "\"objects\":[{\"type\":\"text\",\"id\":1,\"color\":[1,2,3,255],\"width\":2,\"rotation\":0,\"position\":[0,0]}]}")] // no text
    [InlineData(Head + "\"crop\":[100000,0,2000000000,5],\"objects\":[]}")] // crop outside the bounds
    [InlineData(Head + "\"objects\":[" + """{"type":"shape","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"rectangle","start":[-1048576,-1048576],"end":[1048576,1048576]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"shape","id":1,"color":[1,2,3,255],"width":1048576,"rotation":0,"kind":"rectangle","start":[0,0],"end":[5,5]}""" + "]}")]
    [InlineData(Head + "\"objects\":[" + """{"type":"shape","id":1,"color":[1,2,3,255],"width":2,"rotation":0,"kind":"line","start":[-1e30,0],"end":[1e30,5]}""" + "]}")]
    [InlineData(Head + "\"objects\":[{\"type\":\"text\",\"id\":1,\"color\":[1,2,3,255],\"width\":2,\"rotation\":0,\"position\":[0,0],\"text\":\"\\ud800\",\"unitSize\":[1,1]}]}")]
    [InlineData(Head + "\"objects\":[{\"type\":\"text\",\"id\":1,\"color\":[1,2,3,255],\"width\":2,\"rotation\":0,\"position\":[0,0],\"text\":\"a\",\"unitSize\":[0,1]}]}")]
    public void Invalid_data_is_rejected_as_a_whole(string json)
    {
        Assert.Throws<InvalidDataException>(() => DocumentJson.Parse(json));
    }

    [Fact]
    public void Data_from_a_newer_version_says_so()
    {
        var e = Assert.Throws<InvalidDataException>(() => DocumentJson.Parse(
            """{"format":"clipmark","version":9,"width":10,"height":10,"objects":[]}"""));
        Assert.Contains("newer Clipmark", e.Message);
    }

    [Fact]
    public void The_head_and_pen_fixture_itself_is_valid()
    {
        var doc = DocumentJson.Parse(Head + "\"objects\":[" + Pen + "]}");
        Assert.Equal([new(1, 1), new Vector2(2, 2)], ((Stroke)doc.Objects.Single()).Points.ToArray());
    }
}
