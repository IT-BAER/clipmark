using System.Numerics;
using Clipmark.App.Editor;
using Clipmark.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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

    // Open: each button over 200 ms, 8 ms after the one before it; close: 120 ms; hover and press scale: 120 ms.
    private const int RingOpenMs = 200, RingStaggerMs = 8, RingCloseMs = 120, RingHoverMs = 120;
    private static readonly Windows.UI.ViewManagement.UISettings RingSettings = new();

    private readonly List<(EditorTool Tool, Button Button, FontIcon Lock)> _ringButtons = [];
    private int _ringFocus;
    private EditorCanvas? _ringEditor;
    private Vector2 _ringAt;
    private Storyboard? _ringStory;
    private bool _ringClosing; // the close animation runs; the popup is still open but takes no input

    /// <summary>Null follows the Windows "Animation effects" setting (off = the ring opens and closes without motion).</summary>
    internal bool? RingMotion { get; set; }

    private bool Motion => RingMotion ?? RingSettings.AnimationsEnabled;

    internal bool ToolRingOpen => ToolRing.IsOpen && !_ringClosing;

    /// <summary>The popup is on screen, the close animation included.</summary>
    internal bool ToolRingShown => ToolRing.IsOpen;

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
        // Page coordinates: the popup and its layer start at the page origin.
        var center = editor.Control.TransformToVisual(this).TransformPoint(at);
        var view = ImageView.TransformToVisual(this).TransformPoint(default);
        double size = ToolRingPanel.Width;
        ToolRingLayer.Width = ActualWidth;
        ToolRingLayer.Height = ActualHeight;
        Canvas.SetLeft(ToolRingPanel, Math.Clamp(center.X - size / 2, view.X, view.X + Math.Max(0, ImageView.ActualWidth - size)));
        Canvas.SetTop(ToolRingPanel, Math.Clamp(center.Y - size / 2, view.Y, view.Y + Math.Max(0, ImageView.ActualHeight - size)));
        _ringClosing = false;
        ToolRingLayer.IsHitTestVisible = true;
        ToolRing.IsOpen = true;
        AnimateRing(open: true);
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
                EditorTool.Pen => Toolbar(PenButton, "\uEE56"),
                EditorTool.Highlighter => Toolbar(HighlighterButton, "\uE7E6"),
                EditorTool.Text => Toolbar(TextButton, "\uE8D2"),
                EditorTool.Eyedropper => Toolbar(EyedropperButton, "\uEF3C"),
                EditorTool.Eraser => Toolbar(EraserButton, "\uE75C"),
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
            // The slot takes the open / close animation; the button the hover and press Scale (WinUI allows no Scale beside a RenderTransform).
            var slot = new Grid { Children = { button }, RenderTransform = new CompositeTransform(), RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
            button.CenterPoint = new Vector3((float)RingButtonSize / 2, (float)RingButtonSize / 2, 0);
            button.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(RingHoverMs) };
            button.PointerEntered += (_, _) => HoverScale(button, 1.08f);
            button.PointerExited += (_, _) => HoverScale(button, 1);
            button.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => HoverScale(button, 0.94f)), true);
            button.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => HoverScale(button, 1.08f)), true);
            double angle = (i * 360.0 / RingTools.Length - 90) * Math.PI / 180;
            Canvas.SetLeft(slot, middle + RingRadius * Math.Cos(angle) - RingButtonSize / 2);
            Canvas.SetTop(slot, middle + RingRadius * Math.Sin(angle) - RingButtonSize / 2);
            ToolRingButtons.Children.Add(slot);
            _ringButtons.Add((tool, button, badge));
        }
        // Tunneling, so the focused button does not take Enter first.
        ToolRingPanel.PreviewKeyDown += (_, e) => e.Handled = ToolRingKey(e.Key);
        // A press on the layer, not on the ring (the hole included), closes it.
        ToolRingLayer.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, ToolRingLayer))
            {
                CloseToolRing();
                e.Handled = true;
            }
        };

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
        if (!ToolRingOpen)
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
            CloseToolRing();
        else
            return false;
        return true;
    }

    /// <summary>Tool ring button by its automation name, for the test driver.</summary>
    internal Button? ToolRingButton(string name) => _ringButtons.FirstOrDefault(b => AutomationProperties.GetName(b.Button) == name).Button;

    /// <summary>Selects at once; the close animation runs meanwhile.</summary>
    private void PickRingTool(EditorTool tool)
    {
        if (!ToolRingOpen)
            return; // a button still focused during the close animation
        CloseToolRing();
        if (_editor is null || _editor != _ringEditor)
            return; // another capture opened meanwhile
        SelectTool(tool);
        if (_tool == tool && tool is EditorTool.Text or EditorTool.Emoji)
            _editor.PlaceAt(_ringAt);
    }

    private void HoverScale(Button button, float scale)
    {
        if (Motion)
            button.Scale = new Vector3(scale, scale, 1);
    }

    /// <summary>Closes the ring with a short reverse animation; it takes no input meanwhile.</summary>
    private void CloseToolRing()
    {
        if (!ToolRingOpen)
            return;
        _ringClosing = true;
        ToolRingLayer.IsHitTestVisible = false;
        AnimateRing(open: false);
    }

    /// <summary>
    /// Open: the disk grows and the buttons scale and fade in from the clicked point, one after another clockwise. Close: all
    /// shrink back and fade, then the popup closes. Without motion the ring shows or goes at once.
    /// </summary>
    private void AnimateRing(bool open)
    {
        _ringStory?.Stop(); // back to the base values: the open look
        _ringStory = null;
        foreach (var (_, button, _) in _ringButtons)
            button.Scale = Vector3.One;
        if (!Motion)
        {
            if (!open)
                FinishClose();
            return;
        }
        var story = new Storyboard();
        // Named in XAML: under AOT, RenderTransform of a XAML-built transform is the base Transform and the cast fails.
        var disk = ToolRingDiskScale;
        int ms = open ? RingOpenMs : RingCloseMs;
        double shown = open ? 0 : 1, gone = 1 - shown;
        Animate(story, ToolRingDisk, "Opacity", shown, gone, 0, open ? ms * 2 / 3 : ms);
        Animate(story, disk, "ScaleX", open ? 0.7 : 1, open ? 1 : 0.8, 0, ms);
        Animate(story, disk, "ScaleY", open ? 0.7 : 1, open ? 1 : 0.8, 0, ms);
        Animate(story, ToolRingDot, "Opacity", shown, gone, 0, open ? ms * 2 / 3 : ms);
        double middle = ToolRingPanel.Width / 2;
        for (int i = 0; i < _ringButtons.Count; i++)
        {
            var slot = (FrameworkElement)_ringButtons[i].Button.Parent;
            var transform = (CompositeTransform)slot.RenderTransform;
            // From the ring center toward the button's place.
            double dx = middle - (Canvas.GetLeft(slot) + RingButtonSize / 2), dy = middle - (Canvas.GetTop(slot) + RingButtonSize / 2);
            int delay = open ? i * RingStaggerMs : 0;
            Animate(story, slot, "Opacity", shown, gone, delay, open ? ms * 2 / 3 : ms);
            Animate(story, transform, "ScaleX", open ? 0.4 : 1, open ? 1 : 0.6, delay, ms);
            Animate(story, transform, "ScaleY", open ? 0.4 : 1, open ? 1 : 0.6, delay, ms);
            Animate(story, transform, "TranslateX", open ? 0.7 * dx : 0, open ? 0 : 0.3 * dx, delay, ms);
            Animate(story, transform, "TranslateY", open ? 0.7 * dy : 0, open ? 0 : 0.3 * dy, delay, ms);
        }
        if (!open)
            story.Completed += (_, _) => FinishClose();
        _ringStory = story;
        story.Begin();
    }

    private void FinishClose()
    {
        if (!_ringClosing)
            return; // opened again meanwhile
        _ringClosing = false;
        ToolRing.IsOpen = false;
    }

    /// <summary>
    /// Holds FROM until DELAY ms, then eases out to TO over MS: key frames, so a staggered button stays hidden until its turn.
    /// Opacity and transforms run on the compositor (independent animations).
    /// </summary>
    private static void Animate(Storyboard story, DependencyObject target, string property, double from, double to, int delayMs, int ms)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        if (delayMs > 0)
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs)), Value = from });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + ms)), Value = to,
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 },
        });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }
}
