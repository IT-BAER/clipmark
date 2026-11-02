using System.Numerics;
using Clipmark.App.Editor;
using Clipmark.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Clipmark.App;

/// <summary>
/// Right-click tool ring: the drawing tools on a circle around the pointer. A pick selects the tool as the toolbar does;
/// Text then starts editing and Emoji places the current emoji at the clicked point, the drag tools wait for the next press.
/// </summary>
public sealed partial class MainPage
{
    private const double RingRadius = 76; // button centers; 10 buttons of 40 DIPs with 8 DIPs between them
    private const double RingButtonSize = 40;

    // Clockwise from the top.
    private static readonly EditorTool[] RingTools =
    [
        EditorTool.Pen, EditorTool.Highlighter, EditorTool.Text, EditorTool.Rectangle, EditorTool.Ellipse,
        EditorTool.Arrow, EditorTool.Line, EditorTool.Emoji, EditorTool.Eyedropper, EditorTool.Eraser,
    ];

    private readonly List<(EditorTool Tool, Button Button, FontIcon Lock)> _ringButtons = [];
    private int _ringFocus;
    private EditorCanvas? _ringEditor;
    private Vector2 _ringAt;

    internal bool ToolRingOpen => ToolRing.IsOpen;

    /// <summary>Ring center in window DIPs while it is open.</summary>
    internal Windows.Foundation.Point ToolRingCenter => ToolRingPanel.TransformToVisual(null).TransformPoint(new(ToolRingPanel.Width / 2, ToolRingPanel.Height / 2));

    /// <summary>Opens the ring centered on a control DIP point (moved inside the canvas view near its edges) for that image px point.</summary>
    private void OpenToolRing(Vector2 imagePoint, Windows.Foundation.Point at)
    {
        if (_editor is not { } editor)
            return;
        if (_ringButtons.Count == 0)
            BuildToolRing();
        _ringEditor = editor;
        _ringAt = imagePoint;
        var license = AppLicense.Current;
        foreach (var (tool, button, badge) in _ringButtons)
        {
            // The current tool in the accent look of a checked toolbar button.
            if (tool == _tool)
            {
                button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                AutomationProperties.SetItemStatus(button, Loc.Get("ItemSelected"));
            }
            else
            {
                button.ClearValue(FrameworkElement.StyleProperty);
                button.ClearValue(AutomationProperties.ItemStatusProperty);
            }
            bool locked = PremiumFor(tool) is { } feature && !license.IsEnabled(feature);
            badge.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(button, locked ? Loc.Get("PremiumLockedHelp") : "");
        }
        var center = editor.Control.TransformToVisual(ImageScroller).TransformPoint(at);
        double size = ToolRingPanel.Width;
        ToolRing.HorizontalOffset = Math.Clamp(center.X - size / 2, 0, Math.Max(0, ImageScroller.ActualWidth - size));
        ToolRing.VerticalOffset = Math.Clamp(center.Y - size / 2, 0, Math.Max(0, ImageScroller.ActualHeight - size));
        ToolRing.IsOpen = true;
        _ringFocus = Math.Max(0, Array.IndexOf(RingTools, _tool));
        _ringButtons[_ringFocus].Button.Focus(FocusState.Programmatic);
    }

    private void BuildToolRing()
    {
        AutomationProperties.SetName(ToolRing, Loc.Get("ToolRingName"));
        double middle = ToolRingPanel.Width / 2;
        for (int i = 0; i < RingTools.Length; i++)
        {
            var tool = RingTools[i];
            // Names, tooltips and glyphs of the toolbar.
            (string Name, object Tip, string Glyph) look = tool switch
            {
                EditorTool.Pen => Toolbar(PenButton, ""),
                EditorTool.Highlighter => Toolbar(HighlighterButton, ""),
                EditorTool.Text => Toolbar(TextButton, ""),
                EditorTool.Eyedropper => Toolbar(EyedropperButton, ""),
                EditorTool.Eraser => Toolbar(EraserButton, ""),
                _ => Shape(tool),
            };
            var badge = new FontIcon { Style = (Style)Resources["LockBadge"] };
            var button = new Button
            {
                Width = RingButtonSize, Height = RingButtonSize, Padding = new Thickness(0), CornerRadius = new CornerRadius(RingButtonSize / 2),
                Content = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children = { new FontIcon { Glyph = look.Glyph, FontSize = 16 }, badge },
                },
            };
            AutomationProperties.SetName(button, look.Name);
            ToolTipService.SetToolTip(button, look.Tip);
            int index = i;
            button.Click += (_, _) => PickRingTool(tool);
            button.GotFocus += (_, _) => _ringFocus = index;
            double angle = (i * 360.0 / RingTools.Length - 90) * Math.PI / 180;
            Canvas.SetLeft(button, middle + RingRadius * Math.Cos(angle) - RingButtonSize / 2);
            Canvas.SetTop(button, middle + RingRadius * Math.Sin(angle) - RingButtonSize / 2);
            ToolRingButtons.Children.Add(button);
            _ringButtons.Add((tool, button, badge));
        }
        // Tunneling, so the focused button does not take Enter first.
        ToolRingPanel.PreviewKeyDown += (_, e) => e.Handled = ToolRingKey(e.Key);

        static (string, object, string) Toolbar(Control button, string glyph) =>
            (AutomationProperties.GetName(button), ToolTipService.GetToolTip(button), glyph);

        static (string, object, string) Shape(EditorTool tool)
        {
            var shape = Shapes.First(s => s.Tool == tool);
            return (shape.Text, Loc.Format("ToolWithShortcut", shape.Text, shape.Key), shape.Glyph);
        }
    }

    /// <summary>Arrows move around the ring, Enter picks, Esc closes; false for other keys.</summary>
    internal bool ToolRingKey(VirtualKey key)
    {
        if (!ToolRing.IsOpen)
            return false;
        int step = key switch { VirtualKey.Right or VirtualKey.Down => 1, VirtualKey.Left or VirtualKey.Up => -1, _ => 0 };
        if (step != 0)
        {
            _ringFocus = (_ringFocus + step + _ringButtons.Count) % _ringButtons.Count;
            _ringButtons[_ringFocus].Button.Focus(FocusState.Keyboard);
        }
        else if (key == VirtualKey.Enter)
            PickRingTool(_ringButtons[_ringFocus].Tool);
        else if (key == VirtualKey.Escape)
            ToolRing.IsOpen = false;
        else
            return false;
        return true;
    }

    /// <summary>Tool ring button by its automation name, for the test driver.</summary>
    internal Button? ToolRingButton(string name) => _ringButtons.FirstOrDefault(b => AutomationProperties.GetName(b.Button) == name).Button;

    private void PickRingTool(EditorTool tool)
    {
        ToolRing.IsOpen = false;
        if (_editor is null || _editor != _ringEditor)
            return; // another capture opened meanwhile
        SelectTool(tool);
        if (_tool == tool && tool is EditorTool.Text or EditorTool.Emoji)
            _editor.PlaceAt(_ringAt);
    }
}
