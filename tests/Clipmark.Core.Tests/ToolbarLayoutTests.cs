using Clipmark.Core.Layout;

namespace Clipmark.Core.Tests;

public class ToolbarLayoutTests
{
    [Fact]
    public void Items_that_fit_stay_on_one_line() => Assert.Equal([0], WrapLayout.LineStarts([40, 40, 60], 148, 4));

    [Fact]
    public void A_new_line_starts_at_the_first_item_that_does_not_fit() =>
        Assert.Equal([0, 2, 3], WrapLayout.LineStarts([40, 40, 60, 40, 40], 100, 4));

    [Fact]
    public void An_item_wider_than_the_line_gets_a_line_of_its_own() =>
        Assert.Equal([0, 1, 2], WrapLayout.LineStarts([40, 300, 40], 100, 4));

    [Fact]
    public void No_items_no_lines() => Assert.Empty(WrapLayout.LineStarts([], 100, 4));

    [Fact]
    public void One_row_keeps_the_edit_tools_centered_between_two_equal_side_columns()
    {
        Assert.True(ToolbarFit.Fits(oneRow: true, available: 916, gap: 8, capture: 300, edit: 300, output: 200));
        Assert.False(ToolbarFit.Fits(oneRow: true, available: 915, gap: 8, capture: 300, edit: 300, output: 200));
    }

    [Fact]
    public void Two_rows_need_room_for_the_capture_and_output_groups_only()
    {
        Assert.True(ToolbarFit.Fits(oneRow: false, available: 516, gap: 8, capture: 300, edit: 900, output: 200));
        Assert.False(ToolbarFit.Fits(oneRow: false, available: 515, gap: 8, capture: 300, edit: 900, output: 200));
    }
}
