using System.Numerics;
using Clipmark.Core.Editor;

namespace Clipmark.Core.Tests;

public class SelectionSessionTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255);
    private static readonly Rgba Blue = new(0, 0, 255, 255);

    // Three objects: a pen stroke, a rectangle outline 100..200 x 100..150, a line at y = 250.
    private static (EditorDocument Doc, SelectionSession Sel, int Pen, int Box, int Line) New()
    {
        var doc = new EditorDocument(400, 300);
        int pen = doc.NewId(), box = doc.NewId(), line = doc.NewId();
        doc.Add(new Stroke(pen, StrokeKind.Pen, Red, 4, [new(20, 20), new(60, 20)]));
        doc.Add(new Shape(box, ShapeKind.Rectangle, Red, 2, new(100, 100), new(200, 150)));
        doc.Add(new Shape(line, ShapeKind.Line, Red, 2, new(20, 250), new(120, 250)));
        return (doc, new SelectionSession(doc), pen, box, line);
    }

    private static void Drag(SelectionSession s, bool toggle, bool shift, params Vector2[] points)
    {
        s.Begin(points[0], toggle);
        foreach (var p in points.Skip(1))
            s.Move(p, shift);
        s.End();
    }

    private static void Click(SelectionSession s, Vector2 p, bool toggle = false) => Drag(s, toggle, false, p);

    [Fact]
    public void Click_on_an_object_selects_only_it_and_click_on_empty_space_clears()
    {
        var (_, s, pen, box, _) = New();
        Click(s, new(40, 20));
        Assert.Equal([pen], s.Selected);
        Click(s, new(150, 100));
        Assert.Equal([box], s.Selected);
        Click(s, new(300, 200));
        Assert.Empty(s.Selected);
    }

    [Fact]
    public void Toggle_click_adds_and_removes_and_toggle_on_empty_space_keeps_the_selection()
    {
        var (_, s, pen, box, _) = New();
        Click(s, new(40, 20));
        Click(s, new(150, 100), toggle: true);
        Assert.Equal([pen, box], s.Selected.Order());
        Click(s, new(40, 20), toggle: true);
        Assert.Equal([box], s.Selected);
        Click(s, new(300, 200), toggle: true);
        Assert.Equal([box], s.Selected);
    }

    [Fact]
    public void Drag_on_an_object_moves_it_by_the_rounded_offset_as_one_undo_step()
    {
        var (doc, s, pen, _, _) = New();
        s.Begin(new(40, 20), false);
        s.Move(new(50.4f, 29.6f), false);
        Assert.Equal(new Vector2(10, 10), s.Offset);
        Assert.Equal(new Vector2(20, 20), ((Stroke)doc.Objects[0]).Points[0]); // document unchanged while dragging
        s.End();

        Assert.Equal(Vector2.Zero, s.Offset);
        Assert.Equal([pen], s.Selected);
        Assert.Equal([new(30, 30), new(70, 30)], ((Stroke)doc.Objects[0]).Points.ToArray());
        doc.Undo();
        Assert.Equal(new Vector2(20, 20), ((Stroke)doc.Objects[0]).Points[0]);
    }

    [Fact]
    public void Drag_on_an_unselected_object_selects_and_moves_only_that_object()
    {
        var (doc, s, _, box, _) = New();
        Click(s, new(40, 20));
        Drag(s, false, false, new(150, 100), new(160, 110));
        Assert.Equal([box], s.Selected);
        Assert.Equal(new Vector2(20, 20), ((Stroke)doc.Objects[0]).Points[0]);
        Assert.Equal(new Vector2(110, 110), ((Shape)doc.Objects[1]).Start);
    }

    [Fact]
    public void Drag_inside_the_selection_box_moves_the_whole_selection()
    {
        var (doc, s, pen, box, _) = New();
        Click(s, new(40, 20));
        Click(s, new(150, 100), toggle: true);
        Drag(s, false, false, new(150, 125), new(155, 130)); // rectangle interior, not on the outline
        Assert.Equal([pen, box], s.Selected.Order());
        Assert.Equal(new Vector2(25, 25), ((Stroke)doc.Objects[0]).Points[0]);
        Assert.Equal(new Vector2(105, 105), ((Shape)doc.Objects[1]).Start);
    }

    [Fact]
    public void Shift_while_moving_locks_to_the_larger_axis()
    {
        var (doc, s, _, _, _) = New();
        Drag(s, false, true, new(40, 20), new(70, 28));
        Assert.Equal(new Vector2(50, 20), ((Stroke)doc.Objects[0]).Points[0]);
    }

    [Fact]
    public void Click_without_move_changes_nothing_in_the_document()
    {
        var (doc, s, _, _, _) = New();
        int changes = 0;
        doc.Changed += () => changes++;
        Click(s, new(40, 20));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Rubber_band_selects_objects_fully_inside_and_live_band_follows_the_pointer()
    {
        var (_, s, pen, box, _) = New();
        s.Begin(new(10, 10), false);
        s.Move(new(210, 160), false);
        Assert.Equal(new FloatRect(new(10, 10), new(210, 160)), s.Band);
        s.End();
        Assert.Null(s.Band);
        Assert.Equal([pen, box], s.Selected.Order());

        Drag(s, false, false, new(10, 200), new(100, 280)); // line reaches x = 121 with its cap
        Assert.Empty(s.Selected);
    }

    [Fact]
    public void Toggle_rubber_band_adds_to_the_selection_and_band_works_in_any_direction()
    {
        var (_, s, pen, _, line) = New();
        Click(s, new(40, 20));
        Drag(s, true, false, new(130, 260), new(10, 240));
        Assert.Equal([pen, line], s.Selected.Order());
    }

    [Fact]
    public void Nudge_moves_the_selection_as_one_undo_step_and_does_nothing_without_a_selection()
    {
        var (doc, s, _, _, _) = New();
        int changes = 0;
        doc.Changed += () => changes++;
        s.Nudge(new(1, 0));
        Assert.Equal(0, changes);
        Click(s, new(40, 20));
        s.Nudge(new(0, 10));
        Assert.Equal(1, changes);
        Assert.Equal(new Vector2(20, 30), ((Stroke)doc.Objects[0]).Points[0]);
    }

    [Fact]
    public void Delete_removes_the_selection_and_undo_brings_it_back_unselected()
    {
        var (doc, s, _, box, line) = New();
        s.SelectAll();
        Click(s, new(40, 20), toggle: true);
        Assert.Equal([box, line], s.Selected.Order());
        s.Delete();
        Assert.Empty(s.Selected);
        Assert.Single(doc.Objects);
        doc.Undo();
        Assert.Equal(3, doc.Objects.Count);
    }

    [Fact]
    public void Selected_drops_ids_that_undo_removed()
    {
        var (doc, s, pen, _, _) = New();
        int extra = doc.NewId();
        doc.Add(new Shape(extra, ShapeKind.Line, Red, 2, new(300, 20), new(380, 20)));
        s.SelectAll();
        doc.Undo();
        Assert.DoesNotContain(extra, s.Selected);
        Assert.Contains(pen, s.Selected);
    }

    [Fact]
    public void Reorder_moves_the_selection_in_z_order()
    {
        var (doc, s, pen, box, line) = New();
        Click(s, new(40, 20));
        s.Reorder(ZOrder.Front);
        Assert.Equal([box, line, pen], doc.Objects.Select(o => o.Id));
    }

    [Fact]
    public void Clear_empties_the_selection_and_SelectionBox_covers_the_selected_outlines()
    {
        var (_, s, _, _, _) = New();
        Assert.Null(s.SelectionBox);
        Click(s, new(40, 20));
        Click(s, new(150, 100), toggle: true);
        Assert.Equal(new FloatRect(new(18, 18), new(201, 151)), s.SelectionBox);
        s.Clear();
        Assert.Empty(s.Selected);
    }

    [Fact]
    public void Recolor_applies_to_the_selection_as_one_undo_step_and_keeps_highlighter_alpha()
    {
        var doc = new EditorDocument(400, 300);
        int pen = doc.NewId(), marker = doc.NewId();
        doc.Add(new Stroke(pen, StrokeKind.Pen, Red, 4, [new(20, 20), new(60, 20)]));
        doc.Add(new Stroke(marker, StrokeKind.Highlighter, Red with { A = 128 }, 20, [new(20, 100), new(60, 100)]));
        var s = new SelectionSession(doc);
        s.SelectAll();
        int changes = 0;
        doc.Changed += () => changes++;
        s.Recolor(Blue);
        Assert.Equal(1, changes);
        Assert.Equal(Blue, doc.Objects[0].Color);
        Assert.Equal(Blue with { A = 128 }, doc.Objects[1].Color);
    }

    [Fact]
    public void Resize_width_applies_a_width_per_object()
    {
        var (doc, s, _, _, _) = New();
        s.SelectAll();
        s.Rewidth(o => o is Stroke ? 8 : 3);
        Assert.Equal([8f, 3f, 3f], doc.Objects.Select(o => o.Width));
    }

    private const float Quarter = MathF.PI / 2;

    private static void Near(Vector2 expected, Vector2 actual, float eps = 1e-2f) =>
        Assert.True(Vector2.Distance(expected, actual) <= eps, $"expected {expected}, got {actual}");

    private static Vector2 Polar(Vector2 center, float radius, float radians) =>
        center + radius * new Vector2(MathF.Cos(radians), MathF.Sin(radians));

    [Fact]
    public void Frame_is_the_local_box_for_one_object_the_extent_union_for_several_and_absent_for_a_line()
    {
        var (_, s, _, _, _) = New();
        Assert.Null(s.Frame);
        Assert.Null(s.EndpointHandles);
        Click(s, new(150, 100));
        Assert.Equal(new SelectionFrame(new FloatRect(new(100, 100), new(200, 150)), 0), s.Frame);
        Assert.Equal(new Vector2(200, 100), s.Frame!.Handle(CropHandle.TopRight));
        Assert.Equal(new Vector2(100, 125), s.Frame.Handle(CropHandle.Left));
        Assert.Equal(new Vector2(150, 76), s.RotateHandle);
        Assert.Null(s.EndpointHandles);

        Click(s, new(40, 20), toggle: true);
        Assert.Equal(new SelectionFrame(new FloatRect(new(18, 18), new(201, 151)), 0), s.Frame);

        Click(s, new(70, 250));
        Assert.Null(s.Frame);
        Assert.Null(s.RotateHandle);
        Assert.Equal((new Vector2(20, 250), new Vector2(120, 250)), s.EndpointHandles);
    }

    [Fact]
    public void Frame_of_a_rotated_object_carries_its_rotation_and_rotated_handles()
    {
        var doc = new EditorDocument(400, 300);
        int id = doc.NewId();
        doc.Add(new Shape(id, ShapeKind.Rectangle, Red, 2, new(100, 100), new(200, 140)) { Rotation = Quarter });
        var s = new SelectionSession(doc);
        s.SelectAll();
        Assert.Equal(Quarter, s.Frame!.Rotation);
        Near(new(150, 170), s.Frame.Handle(CropHandle.Right));
        Near(new(150 + 20 + 24, 120), s.RotateHandle!.Value);
    }

    [Fact]
    public void Corner_handle_drag_resizes_instead_of_moving_and_edge_handle_moves_one_edge()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(150, 100));
        Drag(s, false, false, new(200, 100), new(220, 90)); // corner is also on the outline
        Assert.Equal(new FloatRect(new(100, 90), new(220, 150)), EditorGeometry.LocalBox(doc.Objects[1]));
        Drag(s, false, false, new(160, 150), new(160, 170));
        Assert.Equal(new FloatRect(new(100, 90), new(220, 170)), EditorGeometry.LocalBox(doc.Objects[1]));
    }

    [Fact]
    public void Handle_priority_is_rotate_then_corner_then_edge()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(150, 100));
        s.RotateOffset = 2;
        Drag(s, false, false, new(150, 99), new(250, 125)); // top edge handle is just as close
        Assert.NotEqual(0, doc.Objects[1].Rotation);
        doc.Undo();

        s.RotateOffset = 24;
        s.HandleTolerance = 30;
        Drag(s, false, false, new(110, 105), new(90, 95)); // left edge handle is also in reach
        Assert.Equal(new FloatRect(new(90, 95), new(200, 150)), EditorGeometry.LocalBox(doc.Objects[1]));
    }

    [Fact]
    public void Multi_selection_ignores_edge_handles_and_moves_instead()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(40, 20));
        Click(s, new(150, 100), toggle: true);
        Drag(s, false, false, new(201, 84.5f), new(211, 84.5f));
        Assert.Equal(new Vector2(30, 20), ((Stroke)doc.Objects[0]).Points[0]);
        Assert.Equal(new Vector2(110, 100), ((Shape)doc.Objects[1]).Start);
    }

    [Fact]
    public void Rotate_drag_shows_Live_and_commits_one_undo_step_on_End()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(150, 100));
        Assert.Empty(s.Live);
        int changes = 0;
        doc.Changed += () => changes++;
        s.Begin(new(150, 76), false);
        Assert.Empty(s.Live);
        s.Move(new(199, 125), false);
        var live = Assert.Single(s.Live);
        Assert.Equal(Quarter, live.Rotation, 4);
        Assert.Equal(0, doc.Objects[1].Rotation); // document unchanged while dragging
        Assert.Equal(Quarter, s.Frame!.Rotation, 4);
        s.End();

        Assert.Empty(s.Live);
        Assert.Equal(1, changes);
        Assert.Equal(Quarter, doc.Objects[1].Rotation, 4);
        Near(new(150, 125), (((Shape)doc.Objects[1]).Start + ((Shape)doc.Objects[1]).End) / 2);
        doc.Undo();
        Assert.Equal(0, doc.Objects[1].Rotation);
    }

    [Fact]
    public void Shift_or_ctrl_press_on_a_handle_grabs_it_instead_of_toggling()
    {
        var (doc, s, _, box, _) = New();
        Click(s, new(150, 100));
        Drag(s, toggle: true, shift: true, new(200, 150), new(300, 160));
        Assert.Equal([box], s.Selected);
        var resized = (Shape)doc.Objects.Single(o => o.Id == box);
        Assert.Equal(new Vector2(100, 100), resized.Start);
        Assert.Equal(new Vector2(300, 200), resized.End);
    }
    [Fact]
    public void Shift_snaps_the_total_rotation_of_a_single_object_to_15_degrees()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(150, 100));
        var center = new Vector2(150, 125);
        float deg = MathF.PI / 180;
        Drag(s, false, false, new(150, 76), Polar(center, 50, -Quarter + 20 * deg));
        Assert.Equal(20 * deg, doc.Objects[1].Rotation, 3);
        Drag(s, false, true, s.RotateHandle!.Value, Polar(center, 50, -Quarter + 32 * deg));
        Assert.Equal(30 * deg, doc.Objects[1].Rotation, 4); // 20 + 12 = 32 snaps to 30
    }

    [Fact]
    public void Group_rotate_turns_every_object_about_the_frame_center_and_shift_snaps_the_delta()
    {
        var (doc, s, _, _, _) = New();
        s.SelectAll();
        Assert.Equal(new FloatRect(new(18, 18), new(201, 251)), s.Frame!.Box);
        var center = new Vector2(109.5f, 134.5f);
        Drag(s, false, false, new(109.5f, -6), center + new Vector2(100, 0));
        var line = (Shape)doc.Objects[2];
        Near(new(-6, 45), line.Start);
        Assert.Equal(0, line.Rotation);
        Assert.Equal(Quarter, doc.Objects[1].Rotation, 4);
        Assert.Equal(Quarter, doc.Objects[0].Rotation, 4);
        doc.Undo();

        float deg = MathF.PI / 180;
        Drag(s, false, true, new(109.5f, -6), Polar(center, 80, -Quarter + 20 * deg));
        Assert.Equal(15 * deg, doc.Objects[1].Rotation, 4);
        Assert.Equal(0, doc.Objects[2].Rotation);
    }

    [Fact]
    public void Group_corner_drag_scales_about_the_opposite_corner_and_clamps_at_one_pixel()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(40, 20));
        Click(s, new(150, 100), toggle: true);
        Drag(s, false, false, new(201, 151), new(384, 284));
        Near(new(22, 22), ((Stroke)doc.Objects[0]).Points[0]);
        Near(new(102, 22), ((Stroke)doc.Objects[0]).Points[1]);
        Assert.Equal(4, doc.Objects[0].Width);
        doc.Undo();

        Drag(s, false, false, new(201, 151), new(18.1f, 18.1f));
        var box = EditorGeometry.LocalBox(doc.Objects[0]);
        Assert.Equal(1, box.Max.X - box.Min.X, 3); // the 40 px stroke limits the factor to 1/40
    }

    [Fact]
    public void Endpoint_drag_moves_one_end_and_shift_snaps_it_to_45_degrees()
    {
        var (doc, s, _, _, _) = New();
        Click(s, new(70, 250));
        Drag(s, false, false, new(120, 250), new(140, 262));
        var line = (Shape)doc.Objects[2];
        Assert.Equal(new Vector2(20, 250), line.Start);
        Assert.Equal(new Vector2(140, 262), line.End);

        Drag(s, false, true, new(20, 250), new(100, 168));
        line = (Shape)doc.Objects[2];
        Assert.Equal(new Vector2(140, 262), line.End);
        var d = line.Start - line.End;
        Assert.Equal(d.X, d.Y, 3); // 45 degrees about the fixed end
    }

    [Fact]
    public void Move_drag_exposes_the_moved_objects_as_Live()
    {
        var (_, s, pen, _, _) = New();
        s.Begin(new(40, 20), false);
        s.Move(new(50, 30), false);
        var live = Assert.Single(s.Live);
        Assert.Equal(pen, live.Id);
        Assert.Equal(new Vector2(30, 30), ((Stroke)live).Points[0]);
        s.End();
        Assert.Empty(s.Live);
    }
}
