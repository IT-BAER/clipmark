using Clipmark.App.Editor;
using Clipmark.App.Output;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.History;
using Clipmark.Core.Licensing;
using Clipmark.Core.Settings;
using Clipmark.Core.Sharing;
using Clipmark.Core.TextActions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using System.Numerics;
using Windows.System;

namespace Clipmark.App;

/// <summary>Launcher toolbar (New, mode, delay) and the editor for the last capture.</summary>
public sealed partial class MainPage : Page
{
    internal static readonly (CaptureMode Mode, string Text, string Glyph)[] Modes =
    [
        (CaptureMode.Rectangle, "Rectangle", "\uF407"), (CaptureMode.Window, "Window", "\uE737"),
        (CaptureMode.FullScreen, "Full screen", "\uE7F4"), (CaptureMode.AllMonitors, "All monitors", "\uEBC6"),
        (CaptureMode.Freeform, "Freeform", "\uF408"),
    ];

    private static readonly (EditorTool Tool, string Text, string Glyph, VirtualKey Key)[] Shapes =
    [
        (EditorTool.Rectangle, "Rectangle", "\uE739", VirtualKey.R), (EditorTool.Ellipse, "Oval", "\uEA3A", VirtualKey.C),
        (EditorTool.Line, "Line", "\uE738", VirtualKey.L), (EditorTool.Arrow, "Arrow", "\uE72A", VirtualKey.A),
    ];

    private static readonly (string Name, Rgba Color)[] QuickColors =
    [
        ("Black", new(0, 0, 0, 255)), ("White", new(255, 255, 255, 255)), ("Red", new(232, 17, 35, 255)),
        ("Yellow", new(255, 230, 0, 255)), ("Green", new(16, 124, 16, 255)), ("Blue", new(0, 120, 212, 255)),
    ];

    private static readonly string[] WidthNames = ["Thin", "Medium", "Thick"];
    private static readonly float[] PenWidths = [2, 4, 8], HighlighterWidths = [12, 20, 32], TextSizes = [16, 24, 40];

    private const float MaxStrokeWidth = 96;

    // Per tool group (0 pen, 1 highlighter, 2 shapes, 3 text, 4 fill): color and stroke width (font size for text, unused for fill).
    private readonly Rgba[] _color = [QuickColors[2].Color, QuickColors[3].Color, QuickColors[2].Color, QuickColors[2].Color, QuickColors[3].Color];
    private readonly float[] _width = [PenWidths[1], HighlighterWidths[1], PenWidths[1], TextSizes[1], PenWidths[1]];
    private readonly List<Button> _swatches = [], _presets = [];
    // Built on first open of the style flyout (the color picker costs start time).
    private ColorPicker? _picker;
    private Slider? _widthSlider;
    private static ColorPicker NewPicker() => new()
    {
        ColorSpectrumShape = ColorSpectrumShape.Ring, IsAlphaEnabled = false, IsMoreButtonVisible = false,
        IsColorChannelTextInputVisible = false, IsHexInputVisible = true, Visibility = Visibility.Collapsed,
    };
    private bool _syncingStyle;
    private EditorTool _tool = EditorTool.Pen;
    private EditorTool _shape = EditorTool.Rectangle;

    private int Group => _tool switch { EditorTool.Pen => 0, EditorTool.Highlighter => 1, EditorTool.Text => 3, EditorTool.Bucket => 4, _ => 2 };

    private static float[] PresetsFor(int group) => group switch { 1 => HighlighterWidths, 3 => TextSizes, _ => PenWidths };

    private static float[] WidthsFor(EditorObject o) => o switch
    {
        Stroke { Kind: StrokeKind.Highlighter } => HighlighterWidths,
        TextObject => TextSizes,
        _ => PenWidths,
    };

    public MainPage()
    {
        InitializeComponent();
        foreach (var (mode, text, glyph) in Modes)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => AppSettings.Update(s => s with { Mode = mode });
            ModeMenu.Items.Add(item);
        }
        foreach (int d in ClipmarkSettings.DelayChoices)
        {
            var item = new MenuFlyoutItem { Text = DelayText(d) };
            item.Click += (_, _) => AppSettings.Update(s => s with { DelaySeconds = d });
            DelayMenu.Items.Add(item);
        }
        foreach (var (tool, text, glyph, key) in Shapes)
        {
            var item = new MenuFlyoutItem { Text = $"{text} ({key})", Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => SelectTool(tool);
            ShapeMenu.Items.Add(item);
            Shortcut(key, VirtualKeyModifiers.None, () => SelectTool(tool));
        }
        StyleFlyout.Opening += (_, _) =>
        {
            if (StyleFlyout.Content is null)
            {
                BuildStyleFlyout();
                ApplyTool();
            }
        };
        Shortcut(VirtualKey.V, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Select));
        Shortcut(VirtualKey.A, VirtualKeyModifiers.Control, () =>
        {
            SelectTool(EditorTool.Select);
            if (_tool == EditorTool.Select)
                _editor?.Select(s => s.SelectAll());
        });
        Shortcut(VirtualKey.P, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Pen));
        Shortcut(VirtualKey.H, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Highlighter));
        Shortcut(VirtualKey.E, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Eraser));
        Shortcut(VirtualKey.T, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Text));
        Shortcut(VirtualKey.I, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Eyedropper));
        Shortcut(VirtualKey.G, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Bucket));
        Shortcut(VirtualKey.Z, VirtualKeyModifiers.Control, () => _editor?.Undo());
        Shortcut(VirtualKey.Y, VirtualKeyModifiers.Control, () => _editor?.Redo());
        Shortcut(VirtualKey.C, VirtualKeyModifiers.Control, () => _ = CopyAsync());
        Shortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveAsync());
        Shortcut(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = SaveAsync(editable: true));
        Shortcut(VirtualKey.N, VirtualKeyModifiers.Control, NewCapture, needsCapture: false);
        Shortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = OpenAsync(), needsCapture: false);
        // On the window root: after a canvas click nothing in the page has focus and keys start at the root.
        KeyEventHandler editKeys = (_, e) =>
        {
            if (!e.Handled)
                e.Handled = HandleKey(e.Key, Modifiers());
        };
        Loaded += (_, _) => ((UIElement)XamlRoot.Content).PreviewKeyDown += editKeys;
        Unloaded += (_, _) => ((UIElement)XamlRoot.Content).PreviewKeyDown -= editKeys;
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        ApplyTool();
#if DEBUG
        if (App.TestDir is { } testDir)
        {
            var driver = new TestDriver(this, testDir);
            Loaded += (_, _) => driver.Start(); // keeps running while Settings is shown
        }
#endif

        ImageScroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnScrollerWheel), true);
        ImageScroller.SizeChanged += (_, _) => Relayout();
        ImageScroller.ViewChanged += (_, e) => { if (!e.IsIntermediate) Relayout(); };
        Loaded += (_, _) => AppSettings.Changed += OnSettingsChanged;
        Unloaded += (_, _) => AppSettings.Changed -= OnSettingsChanged;
        Loaded += (_, _) => { AppLicense.Changed += OnLicenseChanged; ApplyLicense(); };
        Unloaded += (_, _) => AppLicense.Changed -= OnLicenseChanged;
        OnSettingsChanged(AppSettings.Current);
    }

    private static string DelayText(int seconds) => seconds == 0 ? "No delay" : $"{seconds} seconds";

    private void OnSettingsChanged(ClipmarkSettings s)
    {
        int modeIndex = Array.FindIndex(Modes, m => m.Mode == s.Mode);
        ModeButton.Content = new FontIcon { Glyph = Modes[modeIndex].Glyph, FontSize = 16 };
        AutomationProperties.SetName(ModeButton, "Capture mode: " + Modes[modeIndex].Text);
        Mark(ModeMenu, 0, Modes.Length, modeIndex);

        int delayIndex = Array.IndexOf(ClipmarkSettings.DelayChoices, s.DelaySeconds);
        DelayButton.Content = s.DelaySeconds == 0 ? new FontIcon { Glyph = "\uE916", FontSize = 16 } : $"{s.DelaySeconds} s";
        AutomationProperties.SetName(DelayButton, "Delay: " + DelayText(s.DelaySeconds));
        Mark(DelayMenu, 0, ClipmarkSettings.DelayChoices.Length, delayIndex);

        Hint.Text = $"Select New or press {s.Hotkey} to capture, {s.RecordHotkey} to record the screen.";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string path)
            ShowImage(path);
    }

    private EditorCanvas? _editor;

    internal EditorCanvas? Editor => _editor;

    internal EditorTool Tool => _tool;

    private string? _video;

    /// <summary>The recording shown in the player, or null.</summary>
    internal string? VideoPath => _video;

    /// <summary>Shows a recording in the player; Save as, Copy and Share then act on the file.</summary>
    internal void ShowVideo(string path)
    {
        CloseCapture();
        _video = path;
        VideoPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
        VideoPlayer.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Collapsed;
        OutputTools.Visibility = Visibility.Visible;
        SaveEditableItem.Visibility = Visibility.Collapsed;
        AgentButton.Visibility = AskAiButton.Visibility = Visibility.Collapsed; // the agents take images, not video
    }

    private void CloseVideo()
    {
        if (_video is null)
            return;
        _video = null;
        VideoPlayer.MediaPlayer?.Pause();
        VideoPlayer.Source = null;
        VideoPlayer.Visibility = Visibility.Collapsed;
        SaveEditableItem.Visibility = Visibility.Visible;
        AgentButton.Visibility = AskAiButton.Visibility = Visibility.Visible;
    }

    internal void ShowImage(string path) => ShowEditor(new EditorCanvas(path), CaptureHistory.IdFor(path));

    /// <summary>Opens a capture history item; edits are written back to it.</summary>
    internal void ShowHistoryItem(string id, byte[] file) => ShowEditor(new EditorCanvas(file), id);

    private void ShowEditor(EditorCanvas editor, string? historyId)
    {
        AppLicense.Refresh();
        CloseVideo();
        ResetAiChat();
        if (_editor is not null)
        {
            _ = WriteHistoryAsync();
            _editor.Close();
        }
        _editor = editor;
        _historyId = historyId;
        _historyRevision = null;
        _editor.Changed += () => { UpdateHistory(); ApplyTool(); Relayout(); SyncTextActions(); ScheduleHistoryWrite(); };
        _editor.Problem += ShowError;
        _textKey = null;
        SyncTextActions();
        _editor.CropFrameChanged += SyncCropFields;
        _editor.ColorPicked += OnColorPicked;
        _editor.Control.PointerWheelChanged += (_, e) =>
        {
            if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0 || _editor is not { } editor)
                return; // Ctrl+wheel zooms in OnScrollerWheel
            e.Handled = true; // keeps the scroll viewer from scrolling
            Wheel(e.GetCurrentPoint(editor.Control).Properties.MouseWheelDelta, (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0, editor.PointOf(e));
        };
        ApplyTool();
        UpdateHistory();
        ImageScroller.Content = _editor.Host;
        Hint.Visibility = Visibility.Collapsed;
        ImageScroller.Visibility = Visibility.Visible;
        EditTools.Visibility = Visibility.Visible;
        OutputTools.Visibility = Visibility.Visible;
    }

    /// <summary>Ends the editor session and frees its Win2D resources; the page shows the start hint again.</summary>
    internal void CloseCapture()
    {
        if (_video is not null)
        {
            CloseVideo();
            OutputTools.Visibility = Visibility.Collapsed;
            Hint.Visibility = Visibility.Visible;
        }
        if (_editor is null)
            return;
        ResetAiChat();
        HideAiFlyout();
        _ = WriteHistoryAsync();
        _editor.Close();
        _editor = null;
        _historyId = null;
        ImageScroller.Content = null;
        ImageScroller.Visibility = Visibility.Collapsed;
        EditTools.Visibility = Visibility.Collapsed;
        OutputTools.Visibility = Visibility.Collapsed;
        Hint.Visibility = Visibility.Visible;
        UpdateHistory();
        SyncTextActions();
    }

    private bool _confirming; // one dialog at a time

    /// <summary>True when there are no unsaved edits or the user saved or discarded them; false on Cancel.</summary>
    internal async Task<bool> ConfirmDiscardAsync()
    {
        if (_editor?.Document?.IsDirty != true)
            return true;
        if (_confirming)
            return false; // one prompt at a time; ContentDialog allows only one open dialog
        _confirming = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Save changes to this capture?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Don't save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            return await dialog.ShowAsync() switch
            {
                ContentDialogResult.Primary => await SaveAsync(
                    editable: _editor?.FromEditable == true && AppLicense.Current.IsEnabled(PremiumFeature.EditablePng)),
                ContentDialogResult.Secondary => true,
                _ => false,
            };
        }
        finally
        {
            _confirming = false;
        }
    }

    /// <summary>Resizes the canvas to the viewport and document; the image keeps its place on screen.</summary>
    private void Relayout()
    {
        if (_editor is null)
            return;
        bool placed = _editor.Control.ActualWidth > 0;
        var before = _editor.Control.TransformToVisual(ImageScroller).TransformPoint(_editor.ImageOrigin);
        if (!_editor.Layout(ImageScroller.ViewportWidth, ImageScroller.ViewportHeight, ImageScroller.ZoomFactor) || !placed)
            return;
        ImageScroller.UpdateLayout();
        var after = _editor.Control.TransformToVisual(ImageScroller).TransformPoint(_editor.ImageOrigin);
        ImageScroller.ChangeView(ImageScroller.HorizontalOffset + after.X - before.X,
            ImageScroller.VerticalOffset + after.Y - before.Y, null, true);
    }

    private readonly Dictionary<(VirtualKey, VirtualKeyModifiers), Func<bool>> _shortcuts = [];

    /// <summary>Page-wide shortcut; unless <paramref name="needsCapture"/> is false it does nothing until a capture is shown.</summary>
    /// <remarks>Shortcuts are off while text is typed (text tool or a text field), so letters and Ctrl+Z / C / A reach the field.</remarks>
    private void Shortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool needsCapture = true)
    {
        bool Run()
        {
            if (Typing)
                return false;
            if (_editor is not null || _video is not null || !needsCapture)
                action();
            return true;
        }
        _shortcuts[(key, modifiers)] = Run;
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => e.Handled = Run();
        KeyboardAccelerators.Add(accelerator);
    }

    /// <remarks>The Ask AI flyout counts as typing: Ctrl+C there copies selected answer text, not the capture.</remarks>
    private bool Typing => _aiFlyout?.IsOpen == true || _editor?.IsEditingText == true
        || (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox);

    private static VirtualKeyModifiers Modifiers()
    {
        var modifiers = VirtualKeyModifiers.None;
        if (Down(VirtualKey.Shift)) modifiers |= VirtualKeyModifiers.Shift;
        if (Down(VirtualKey.Control)) modifiers |= VirtualKeyModifiers.Control;
        if (Down(VirtualKey.Menu)) modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;

        static bool Down(VirtualKey k) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    /// <summary>
    /// Editing keys, tunneling so a focused button or the scroll viewer does not take them first: Enter / Esc in crop mode;
    /// with a selection arrows nudge (Shift = 10 px), Page Up / Down reorder (Shift = front / back), Del deletes, Esc deselects.
    /// </summary>
    internal bool HandleKey(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (_editor is null || _aiFlyout?.IsOpen == true)
            return false; // Esc in the Ask AI flyout closes the flyout, not the selection
        // Text being edited keeps every key except Esc, which commits it.
        if (_editor.IsEditingText)
        {
            if (key != VirtualKey.Escape)
                return false;
            _editor.CommitText();
            return true;
        }
        // A focused text field keeps its editing keys (NumberBox evaluates on Enter); Esc still leaves the mode.
        if (key != VirtualKey.Escape && FocusManager.GetFocusedElement(XamlRoot) is TextBox)
            return false;
        if (Cropping)
        {
            if (key == VirtualKey.Enter)
                _editor.ApplyCrop();
            else if (key == VirtualKey.Escape)
                _editor.CancelCrop();
            else
                return false;
            return true;
        }
        if (_tool == EditorTool.Eyedropper && key == VirtualKey.Escape)
        {
            SelectTool(_toolBeforePick);
            return true;
        }
        if (_tool != EditorTool.Select || _editor.Selection is not { Selected.Count: > 0 })
            return false;
        bool shift = (modifiers & VirtualKeyModifiers.Shift) != 0;
        float step = shift ? 10 : 1;
        Action<SelectionSession>? command = key switch
        {
            VirtualKey.Left => s => s.Nudge(new(-step, 0)),
            VirtualKey.Right => s => s.Nudge(new(step, 0)),
            VirtualKey.Up => s => s.Nudge(new(0, -step)),
            VirtualKey.Down => s => s.Nudge(new(0, step)),
            VirtualKey.PageUp => s => s.Reorder(shift ? ZOrder.Front : ZOrder.Up),
            VirtualKey.PageDown => s => s.Reorder(shift ? ZOrder.Back : ZOrder.Down),
            VirtualKey.Delete => s => s.Delete(),
            VirtualKey.Escape => s => s.Clear(),
            _ => null,
        };
        if (command is null)
            return false;
        _editor.Select(command);
        return true;
    }

    /// <summary>Runs a key as the keyboard would (editing keys, then shortcuts); false when nothing handles it.</summary>
    internal bool RunKey(VirtualKey key, VirtualKeyModifiers modifiers)
    {
        if (HandleKey(key, modifiers))
            return true;
        return _shortcuts.TryGetValue((key, modifiers), out var run) && run();
    }

    private bool Cropping => _editor?.IsCropping == true;

    private EditorTool _toolBeforePick = EditorTool.Pen;

    private void SelectTool(EditorTool tool)
    {
        if (PremiumFor(tool) is { } feature && !AppLicense.Current.IsEnabled(feature))
        {
            ApplyTool(); // a toolbar click already flipped the button
            _ = ShowPremiumAsync(feature);
            return;
        }
        _editor?.CancelCrop();
        if (tool == EditorTool.Eyedropper && _tool != EditorTool.Eyedropper)
            _toolBeforePick = _tool;
        _tool = tool;
        if (tool >= EditorTool.Rectangle)
            _shape = tool;
        ApplyTool();
    }

    private static PremiumFeature? PremiumFor(EditorTool tool) => tool switch
    {
        EditorTool.Select => PremiumFeature.Select,
        EditorTool.Text => PremiumFeature.Text,
        EditorTool.Eyedropper => PremiumFeature.Eyedropper,
        EditorTool.Bucket => PremiumFeature.Fill,
        _ => null,
    };

    private static string FeatureName(PremiumFeature feature) => feature switch
    {
        PremiumFeature.Select => "selecting and moving objects",
        PremiumFeature.AttachedArrows => "arrows attached to shapes",
        PremiumFeature.Text => "text labels",
        PremiumFeature.Fill => "the fill tool",
        PremiumFeature.Eyedropper => "the eyedropper",
        PremiumFeature.SendToAi => "sending captures to AI assistants",
        PremiumFeature.History => "the capture history",
        PremiumFeature.AiChat => "asking AI about your captures",
        _ => "saving editable PNGs",
    };

    private void OnLicenseChanged(Entitlement _) => ApplyLicense();

    /// <summary>Shows a lock on each locked premium tool and leaves a tool that just got locked.</summary>
    private void ApplyLicense()
    {
        var license = AppLicense.Current;
        foreach (var (button, badge, feature) in new (Microsoft.UI.Xaml.Controls.Primitives.ToggleButton, FontIcon, PremiumFeature)[]
            {
                (SelectButton, SelectLock, PremiumFeature.Select), (TextButton, TextLock, PremiumFeature.Text),
                (EyedropperButton, EyedropperLock, PremiumFeature.Eyedropper), (BucketButton, BucketLock, PremiumFeature.Fill),
            })
        {
            bool locked = !license.IsEnabled(feature);
            badge.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(button, locked ? "Premium feature, locked" : "");
        }
        SaveEditableItem.Text = license.IsEnabled(PremiumFeature.EditablePng)
            ? "Save as editable PNG (Ctrl+Shift+S)" : "Save as editable PNG (Premium)";
        ToolTipService.SetToolTip(AgentButton, license.IsEnabled(PremiumFeature.SendToAi) ? "Send to AI" : "Send to AI (Premium)");
        ToolTipService.SetToolTip(HistoryButton, license.IsEnabled(PremiumFeature.History) ? "Capture history" : "Capture history (Premium)");
        ToolTipService.SetToolTip(AskAiButton, license.IsEnabled(PremiumFeature.AiChat) ? "Ask AI about this capture" : "Ask AI (Premium)");
        if (PremiumFor(_tool) is { } current && !license.IsEnabled(current))
            _tool = EditorTool.Pen; // the editor commits text being edited on the tool change
        ApplyTool();
    }

    /// <summary>Explains a locked premium feature: Buy opens the product page, Enter license key opens Settings.</summary>
    private async Task ShowPremiumAsync(PremiumFeature feature)
    {
        if (_confirming)
            return;
        _confirming = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Clipmark Premium",
                Content = $"Clipmark Premium includes {FeatureName(feature)}. Your {TrialClock.TrialDays}-day trial has ended.",
                PrimaryButtonText = "Buy Premium",
                SecondaryButtonText = "Enter license key",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    await Launcher.LaunchUriAsync(new Uri(AppLicense.BuyUrl));
                    break;
                case ContentDialogResult.Secondary:
                    Frame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e) =>
        SelectTool(Enum.Parse<EditorTool>((string)((FrameworkElement)sender).Tag));

    /// <summary>A click or UIA Toggle flips IsChecked (Toggle raises no Click); any flip away from the tool state selects the shape tool.</summary>
    private void OnShapeCheckedChanged(ToggleSplitButton sender, ToggleSplitButtonIsCheckedChangedEventArgs e)
    {
        if (sender.IsChecked != (!Cropping && _tool >= EditorTool.Rectangle))
            SelectTool(_shape);
    }

    /// <summary>Syncs the toolbar with the current tool and its group's color and width, and hands both to the editor.</summary>
    private void ApplyTool()
    {
        bool cropping = Cropping;
        SelectButton.IsChecked = !cropping && _tool == EditorTool.Select;
        PenButton.IsChecked = !cropping && _tool == EditorTool.Pen;
        HighlighterButton.IsChecked = !cropping && _tool == EditorTool.Highlighter;
        EraserButton.IsChecked = !cropping && _tool == EditorTool.Eraser;
        TextButton.IsChecked = !cropping && _tool == EditorTool.Text;
        EyedropperButton.IsChecked = !cropping && _tool == EditorTool.Eyedropper;
        BucketButton.IsChecked = !cropping && _tool == EditorTool.Bucket;
        ShapeButton.IsChecked = !cropping && _tool >= EditorTool.Rectangle;
        CropButton.IsChecked = cropping;
        ApplyCropButton.Visibility = CancelCropButton.Visibility = CropFields.Visibility = cropping ? Visibility.Visible : Visibility.Collapsed;
        SyncCropFields();
        int shapeIndex = Array.FindIndex(Shapes, s => s.Tool == _shape);
        ShapeButton.Content = new FontIcon { Glyph = Shapes[shapeIndex].Glyph, FontSize = 16 };
        AutomationProperties.SetName(ShapeButton, "Shapes: " + Shapes[shapeIndex].Text);
        Mark(ShapeMenu, 0, Shapes.Length, shapeIndex);

        int group = Group;
        var shown = _color[group];
        float shownWidth = _width[group];
        float[] presets = PresetsFor(group);
        // The style shows the text being edited, else with Select the topmost selected object.
        var top = _editor?.EditingText ?? (_tool == EditorTool.Select && _editor?.Document is { } doc && _editor.Selection is { } sel
            ? doc.Objects.LastOrDefault(o => sel.Selected.Contains(o.Id)) : null);
        if (top is not null)
        {
            shown = top.Color with { A = 255 };
            shownWidth = top.Width;
            presets = WidthsFor(top);
        }
        StyleButton.IsEnabled = _tool != EditorTool.Eraser && (_tool != EditorTool.Select || top is not null);
        // Palette over a bar in the color; the bar grows from Thin (2 px) to Thick (5 px). A plain red dot read as "record".
        double barHeight = Math.Round(Math.Clamp(2 + 3 * (shownWidth - presets[0]) / (presets[^1] - presets[0]), 2, 5));
        StyleButton.Content = new Grid
        {
            Width = 16, Height = 16,
            Children =
            {
                new FontIcon { Glyph = "\uE790", FontSize = 12, VerticalAlignment = VerticalAlignment.Top },
                new Border
                {
                    Height = barHeight, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1),
                    Background = new SolidColorBrush(EditorCanvas.ToColor(shown)),
                    BorderBrush = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"], BorderThickness = new Thickness(0.5),
                },
            },
        };
        int colorIndex = Array.FindIndex(QuickColors, c => c.Color == shown);
        int widthIndex = Array.IndexOf(presets, shownWidth);
        string colorName = colorIndex >= 0 ? QuickColors[colorIndex].Name : $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}";
        string widthName = widthIndex >= 0 ? WidthNames[widthIndex] : $"{shownWidth:0} px";
        AutomationProperties.SetName(StyleButton, $"Color and width: {colorName}, {widthName}");
        _syncingStyle = true;
        for (int i = 0; i < _swatches.Count; i++)
            MarkButton(_swatches[i], i == colorIndex);
        for (int i = 0; i < _presets.Count; i++)
            MarkButton(_presets[i], i == widthIndex);
        if (_widthSlider is not null)
            _widthSlider.Value = shownWidth;
        if (_picker is not null)
            _picker.Color = EditorCanvas.ToColor(shown);
        _syncingStyle = false;

        if (_editor is not null)
        {
            _editor.Tool = _tool;
            _editor.Color = _color[group];
            _editor.StrokeWidth = _width[group];
            _editor.AttachEnabled = AppLicense.Current.IsEnabled(PremiumFeature.AttachedArrows);
        }
    }

    /// <summary>Shows the current choice among menu items first..first+count-1 in the accent color (icons without their own color follow).</summary>
    private static void Mark(MenuFlyout menu, int first, int count, int current)
    {
        var accent = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        for (int i = 0; i < count; i++)
        {
            var item = (MenuFlyoutItem)menu.Items[first + i];
            if (i == current)
            {
                item.Foreground = accent;
                AutomationProperties.SetItemStatus(item, "Selected");
            }
            else
            {
                item.ClearValue(Control.ForegroundProperty);
                item.ClearValue(AutomationProperties.ItemStatusProperty);
            }
        }
    }

    private static void MarkButton(Button button, bool current)
    {
        if (current)
        {
            button.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            AutomationProperties.SetItemStatus(button, "Selected");
        }
        else
        {
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(AutomationProperties.ItemStatusProperty);
        }
    }

    /// <summary>Style flyout: 6 quick colors, "More colors" (color ring), 3 width presets and a width slider.</summary>
    private void BuildStyleFlyout()
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (name, color) in QuickColors)
        {
            var button = new Button
            {
                Width = 40, Height = 36, Padding = new Thickness(0), BorderThickness = new Thickness(2),
                Content = new FontIcon { Glyph = "", FontSize = 20, Foreground = new SolidColorBrush(EditorCanvas.ToColor(color)) },
            };
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => { SetColor(color); StyleFlyout.Hide(); };
            _swatches.Add(button);
            swatches.Children.Add(button);
        }
        var picker = _picker = NewPicker();
        var slider = _widthSlider = new Slider { Minimum = 1, Maximum = MaxStrokeWidth, StepFrequency = 1, Header = "Width (mouse wheel)" };
        var more = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = "More colors" };
        AutomationProperties.SetName(more, "More colors");
        more.Checked += (_, _) => picker.Visibility = Visibility.Visible; // Checked also fires for a UIA Toggle (Click does not)
        more.Unchecked += (_, _) => picker.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(picker, "Custom color");
        picker.ColorChanged += (_, e) =>
        {
            if (!_syncingStyle)
                SetColor(new Rgba(e.NewColor.R, e.NewColor.G, e.NewColor.B, 255));
        };
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < WidthNames.Length; i++)
        {
            int index = i;
            var button = new Button { Content = WidthNames[i], BorderThickness = new Thickness(2) };
            button.Click += (_, _) =>
            {
                SetWidth(o => WidthsFor(o)[index], PresetsFor(Group)[index]);
                StyleFlyout.Hide();
            };
            _presets.Add(button);
            presets.Children.Add(button);
        }
        AutomationProperties.SetName(slider, "Width");
        slider.ValueChanged += (_, e) =>
        {
            if (!_syncingStyle)
                SetWidth(_ => (float)e.NewValue, (float)e.NewValue);
        };
        StyleFlyout.Content = new StackPanel { Spacing = 12, Children = { swatches, more, picker, presets, slider } };
    }

    /// <summary>New color for the text being edited, else the selection (Select tool) or the current tool group.</summary>
    private void SetColor(Rgba color)
    {
        if (_editor?.IsEditingText == true)
        {
            _editor.RestyleText(color: color);
            if (_tool == EditorTool.Text)
                _color[Group] = color;
        }
        else if (_tool == EditorTool.Select)
            _editor?.Select(s => s.Recolor(color));
        else
            _color[Group] = color;
        ApplyTool();
    }

    /// <summary>New width (font size for text) for the text being edited, else per selected object (Select tool) or for the tool group.</summary>
    private void SetWidth(Func<EditorObject, float> forObject, float forTool)
    {
        if (_editor?.EditingText is { } text)
        {
            _editor.RestyleText(size: forObject(text));
            if (_tool == EditorTool.Text)
                _width[Group] = forObject(text);
        }
        else if (_tool == EditorTool.Select)
            _editor?.Select(s => s.Rewidth(forObject));
        else
            _width[Group] = forTool;
        ApplyTool();
    }

    /// <summary>One wheel notch scales the width by about 20 % (at least 1 px), within 1..MaxStrokeWidth.</summary>
    private static float StepWidth(float width, int notches)
    {
        float next = MathF.Round(width * MathF.Pow(1.2f, notches));
        if (next == width)
            next = width + Math.Sign(notches);
        return Math.Clamp(next, 1, MaxStrokeWidth);
    }

    private int _wheelRest;

    /// <summary>Plain wheel over the canvas = width of the tool or the selection (with a short size hint); Shift+wheel scrolls vertically.</summary>
    internal void Wheel(int delta, bool shift, System.Numerics.Vector2 imagePoint)
    {
        if (_editor is null)
            return;
        if (shift)
        {
            ImageScroller.ChangeView(null, ImageScroller.VerticalOffset - delta, null);
            return;
        }
        _wheelRest += delta;
        int notches = _wheelRest / 120;
        _wheelRest %= 120;
        if (notches == 0 || Cropping || _tool is EditorTool.Eraser or EditorTool.Eyedropper or EditorTool.Bucket)
            return;
        if (_editor.EditingText is not null)
        {
            SetWidth(o => StepWidth(o.Width, notches), 0);
            _editor.ShowSizeHint(imagePoint, _editor.EditingText!.Width);
        }
        else if (_tool == EditorTool.Select)
        {
            if (_editor.Selection is not { Selected.Count: > 0 })
                return;
            SetWidth(o => StepWidth(o.Width, notches), 0);
            var top = _editor.Document!.Objects.Last(o => _editor.Selection.Selected.Contains(o.Id));
            _editor.ShowSizeHint(imagePoint, top.Width);
        }
        else
        {
            SetWidth(_ => 0, StepWidth(_width[Group], notches));
            _editor.ShowSizeHint(imagePoint, _width[Group]);
        }
    }

    /// <summary>Eyedropper click: copies #RRGGBB, returns to the previous tool and gives it (or its selection) the color.</summary>
    private void OnColorPicked(Rgba color)
    {
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (!CaptureOutput.CopyTextToClipboard(owner, ColorSample.Hex(color)))
            ShowError("The clipboard is in use by another app; the color was not copied.");
        SelectTool(_toolBeforePick);
        SetColor(color);
    }

    private void UpdateHistory()
    {
        UndoButton.IsEnabled = _editor?.Document?.CanUndo == true;
        RedoButton.IsEnabled = _editor?.Document?.CanRedo == true;
    }

    // Capture history: the item the editor shows, and the document state last written to it.
    private string? _historyId;
    private object? _historyRevision;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _historyTimer;

    /// <summary>Writes edits back to the history item about a second after the last change.</summary>
    private void ScheduleHistoryWrite()
    {
        if (_historyId is null || _editor?.Document is not { } doc)
            return;
        _historyRevision ??= doc.Revision; // the state as loaded is already stored
        if (ReferenceEquals(doc.Revision, _historyRevision))
            return;
        if (_historyTimer is null)
        {
            _historyTimer = DispatcherQueue.CreateTimer();
            _historyTimer.Interval = TimeSpan.FromSeconds(1);
            _historyTimer.IsRepeating = false;
            _historyTimer.Tick += (_, _) =>
            {
                if (_editor is { IsEditingText: true } or { IsCropping: true })
                    _historyTimer.Start(); // Export would commit the text box or show the crop mid-edit
                else
                    _ = WriteHistoryAsync();
            };
        }
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    /// <summary>Stores the current document as an editable PNG in its history item. Reads the editor before the first await.</summary>
    private async Task WriteHistoryAsync()
    {
        _historyTimer?.Stop();
        if (_historyId is not { } id || _editor is not { Document: { } doc } editor || ReferenceEquals(doc.Revision, _historyRevision ?? doc.Revision))
            return;
        if (editor.Export() is not { } image || editor.ExportBase() is not { } baseImage)
            return;
        _historyRevision = doc.Revision;
        string json = DocumentJson.Serialize(doc);
        var previous = _historyWrite;
        _historyWrite = WriteAsync();
        await _historyWrite;

        // Writes land in the order they started, so a slow earlier encode never overwrites a later state.
        async Task WriteAsync()
        {
            var png = EditablePng.Embed(await CaptureOutput.EncodePngAsync(image), json, await CaptureOutput.EncodePngAsync(baseImage));
            try
            {
                await previous;
            }
            catch (Exception) { } // a failed earlier write must not block this one
            await CaptureHistory.UpdateAsync(id, png);
        }
    }

    private Task _historyWrite = Task.CompletedTask;

    /// <summary>Writes pending edits to the history item and waits for every started write (before the app exits).</summary>
    internal async Task FlushHistoryAsync()
    {
        await WriteHistoryAsync();
        await _historyWrite;
    }

    private void OnCropClick(object sender, RoutedEventArgs e)
    {
        if (Cropping)
            _editor?.CancelCrop();
        else
            _editor?.BeginCrop();
        ApplyTool();
    }

    private bool _syncingCrop;

    /// <summary>Shows the crop frame in the X / Y / W / H fields.</summary>
    private void SyncCropFields()
    {
        if (_editor?.CropFrame is not { } f)
            return;
        _syncingCrop = true;
        (CropX.Value, CropY.Value, CropW.Value, CropH.Value) = (f.X, f.Y, f.Width, f.Height);
        _syncingCrop = false;
    }

    /// <summary>A field (after NumberBox evaluated arithmetic such as 800+20) moves that edge or size; the other values stay.</summary>
    private void OnCropField(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncingCrop || _editor?.CropFrame is not { } f)
            return;
        if (!double.IsNaN(sender.Value))
        {
            int v = (int)Math.Round(sender.Value);
            _editor.SetCropFrame((string)sender.Tag switch
            {
                "X" => f with { X = v },
                "Y" => f with { Y = v },
                "W" => f with { Width = Math.Max(1, v) },
                _ => f with { Height = Math.Max(1, v) },
            });
        }
        SyncCropFields(); // shows the clamped result, or restores an empty or invalid field
    }

    private void OnApplyCrop(object sender, RoutedEventArgs e) => _editor?.ApplyCrop();

    private void OnCancelCrop(object sender, RoutedEventArgs e) => _editor?.CancelCrop();

    private void OnUndo(object sender, RoutedEventArgs e) => _editor?.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => _editor?.Redo();

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    /// <summary>Copies the capture (or the recording file); false when nothing was copied.</summary>
    private async Task<bool> CopyAsync()
    {
        if (_video is { } video)
        {
            if (CaptureOutput.CopyFileToClipboard(Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), video))
                return true;
            ShowError("The clipboard is in use by another app; the recording was not copied.");
            return false;
        }
        var editor = _editor;
        if (editor?.Export() is not { } image)
            return false;
        var png = await CaptureOutput.EncodePngAsync(image);
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (CaptureOutput.CopyToClipboard(owner, image, png))
        {
            editor.Document?.MarkClean();
            return true;
        }
        ShowError("The clipboard is in use by another app; the capture was not copied.");
        return false;
    }

    private void OnSendToAgent(SplitButton sender, SplitButtonClickEventArgs e) => _ = SendToAgentAsync(null);

    /// <summary>Lists the running Claude, Codex and ChatGPT sessions, most recently active first.</summary>
    private void OnAgentMenuOpening(object? sender, object e)
    {
        AgentMenu.Items.Clear();
        var targets = AgentSend.FindTargets(out var processes);
        if (targets.Count == 0)
            AgentMenu.Items.Add(new MenuFlyoutItem { Text = "No running AI session found", IsEnabled = false });
        int iconSize = (int)Math.Round(16 * (XamlRoot?.RasterizationScale ?? 1));
        foreach (var target in targets)
        {
            var item = new MenuFlyoutItem
            {
                Text = target.Label,
                Icon = AgentIcons.For(target, processes, iconSize) is { } image ? new ImageIcon { Source = image } : new FontIcon { Glyph = target.Host switch { AgentHost.DesktopApp => "", AgentHost.VsCode => "", _ => "" } },
            };
            item.Click += (_, _) => _ = SendToAgentAsync(target);
            AgentMenu.Items.Add(item);
        }
    }

    /// <summary>Copies the capture and pastes it into the prompt of <paramref name="target"/>, or of the most recently active AI session.</summary>
    private async Task SendToAgentAsync(AgentTarget? target)
    {
        if (_video is not null || _editor is null)
            return;
        if (!AppLicense.Current.IsEnabled(PremiumFeature.SendToAi))
        {
            _ = ShowPremiumAsync(PremiumFeature.SendToAi);
            return;
        }
        target ??= AgentSend.FindTargets().FirstOrDefault();
        if (target is null)
        {
            ShowError("No running AI session found. Start Claude Code or Codex in a terminal or VS Code, or open the Claude or ChatGPT app.");
            return;
        }
        if (await CopyAsync() && await AgentSend.PasteAsync(target) is { } problem)
            ShowError(problem);
    }

    private void OnSave(SplitButton sender, SplitButtonClickEventArgs e) => _ = SaveAsync();

    private void OnSaveEditable(object sender, RoutedEventArgs e) => _ = SaveAsync(editable: true);

    /// <summary>
    /// Save as dialog in the save folder (PNG or JPEG; editable = PNG with the objects kept editable); true when the file was written.
    /// </summary>
    private async Task<bool> SaveAsync(bool editable = false)
    {
        if (_video is not null)
            return !editable && await SaveVideoAsync();
        if (_editor is null)
            return false;
        if (editable && !AppLicense.Current.IsEnabled(PremiumFeature.EditablePng))
        {
            _ = ShowPremiumAsync(PremiumFeature.EditablePng);
            return false;
        }
        string folder = AppSettings.Current.SaveFolder ?? CaptureOutput.DefaultFolder;
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(
                CaptureFileName.Next(DateTime.Now, name => File.Exists(Path.Combine(folder, name)))),
            DefaultFileExtension = ".png",
        };
        if (Directory.Exists(folder))
            picker.SuggestedFolder = folder;
        if (editable)
            picker.FileTypeChoices.Add("Editable PNG image", [".png"]);
        else
        {
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            picker.FileTypeChoices.Add("JPEG image", [".jpg", ".jpeg"]);
        }
        var result = await picker.PickSaveFileAsync();
        return result is not null && await SaveToAsync(result.Path, editable);
    }

    /// <summary>
    /// Writes the export as JPEG (by extension) or PNG and marks the document saved. Editable adds the document and the
    /// base image (redactions burned in) as a private PNG chunk.
    /// </summary>
    internal async Task<bool> SaveToAsync(string path, bool editable = false)
    {
        var editor = _editor;
        if (editor?.Export() is not { } image || editor.Document is not { } doc)
            return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool jpeg = !editable && ext is ".jpg" or ".jpeg";
        var bytes = jpeg ? await CaptureOutput.EncodeJpegAsync(image) : await CaptureOutput.EncodePngAsync(image);
        if (editable)
        {
            if (editor.ExportBase() is not { } baseImage)
                return false;
            bytes = EditablePng.Embed(bytes, DocumentJson.Serialize(doc), await CaptureOutput.EncodePngAsync(baseImage));
        }
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError($"The capture was not saved: {e.Message}");
            return false;
        }
        editor.Document?.MarkClean();
        return true;
    }

    /// <summary>Save as for the recording: copies the MP4 (it is already saved in Videos\Screen Recordings).</summary>
    private async Task<bool> SaveVideoAsync()
    {
        var video = _video!;
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(video),
            DefaultFileExtension = ".mp4",
        };
        picker.FileTypeChoices.Add("MP4 video", [".mp4"]);
        var result = await picker.PickSaveFileAsync();
        return result is not null && await SaveVideoToAsync(result.Path);
    }

    internal async Task<bool> SaveVideoToAsync(string path)
    {
        if (_video is not { } video)
            return false;
        try
        {
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase))
                await Task.Run(() => File.Copy(video, path, overwrite: true));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError($"The recording was not saved: {e.Message}");
            return false;
        }
    }

    private void OnShare(object sender, RoutedEventArgs e) => _ = ShareAsync();

    private async Task ShareAsync()
    {
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (_video is { } video)
        {
            try
            {
                await ShareSheet.ShowFileAsync(owner, video);
            }
            catch (Exception e)
            {
                ShowError($"The share sheet did not open: {e.Message}");
            }
            return;
        }
        if (_editor?.Export() is not { } image)
            return;
        try
        {
            await ShareSheet.ShowAsync(owner, await CaptureOutput.EncodePngAsync(image));
        }
        catch (Exception e)
        {
            ShowError($"The share sheet did not open: {e.Message}");
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => _ = OpenAsync();

    /// <summary>Open dialog for an image; an editable PNG opens with its objects. Asks about unsaved edits first.</summary>
    private async Task OpenAsync()
    {
        var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" })
            picker.FileTypeFilter.Add(ext);
        string folder = AppSettings.Current.SaveFolder ?? CaptureOutput.DefaultFolder;
        if (Directory.Exists(folder))
            picker.SuggestedFolder = folder;
        if (await picker.PickSingleFileAsync() is { } result && await ConfirmDiscardAsync())
        {
            Message.IsOpen = false;
            ShowImage(result.Path);
        }
    }

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowHistoryAsync();
        }
        catch (Exception ex)
        {
            ShowError("The capture history did not open: " + ex.Message);
        }
    }

    /// <summary>Flyout with the stored captures, newest first: click opens one, right-click deletes it.</summary>
    private async Task ShowHistoryAsync()
    {
        if (!AppLicense.Current.IsEnabled(PremiumFeature.History))
        {
            _ = ShowPremiumAsync(PremiumFeature.History);
            return;
        }
        var panel = new StackPanel { Spacing = 8, Width = 352 };
        var flyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        if (!AppSettings.Current.HistoryEnabled)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Capture history is off. Turn it on in Settings to keep your last {HistoryPolicy.MaxItems} captures, "
                    + "encrypted for your Windows account.",
                TextWrapping = TextWrapping.Wrap,
            });
            var settings = new Button { Content = "Open Settings" };
            settings.Click += (_, _) => { flyout.Hide(); Frame.Navigate(typeof(SettingsPage)); };
            panel.Children.Add(settings);
            flyout.ShowAt(HistoryButton);
            return;
        }
        await WriteHistoryAsync(); // the open item shows its latest edits
        var entries = await CaptureHistory.ListAsync();

        var header = new Grid();
        header.Children.Add(new TextBlock { Text = "History", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        if (entries.Count > 0)
        {
            var clear = new Button { Content = "Clear all", HorizontalAlignment = HorizontalAlignment.Right };
            clear.Click += (_, _) => { flyout.Hide(); _ = ClearHistoryAsync(); };
            header.Children.Add(clear);
        }
        panel.Children.Add(header);
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No captures yet.", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            flyout.ShowAt(HistoryButton);
            return;
        }
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 172, ItemHeight = 132, MaximumRowsOrColumns = 2 };
        panel.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 420 });
        var images = new List<(string Id, Image Image)>();
        foreach (var entry in entries)
        {
            string id = entry.Id, taken = entry.TakenUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            var image = new Image { Stretch = Stretch.Uniform };
            var content = new Grid { RowSpacing = 4 };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = taken, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(label, 1);
            content.Children.Add(image);
            content.Children.Add(label);
            var item = new Button
            {
                Content = content, Width = 164, Height = 124, Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            AutomationProperties.SetName(item, "Capture from " + taken);
            ToolTipService.SetToolTip(item, "Open (right-click to delete)");
            item.Click += (_, _) => { flyout.Hide(); _ = OpenHistoryItemAsync(id); };
            var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
            delete.Click += async (_, _) =>
            {
                grid.Children.Remove(item);
                await DeleteHistoryItemAsync(id);
            };
            item.ContextFlyout = new MenuFlyout { Items = { delete } };
            grid.Children.Add(item);
            images.Add((id, image));
        }
        bool closed = false;
        flyout.Closed += (_, _) => closed = true;
        flyout.ShowAt(HistoryButton);
        foreach (var (id, image) in images) // thumbnails one by one; an unreadable item keeps an empty tile
        {
            if (closed)
                break;
            if (await CaptureHistory.LoadAsync(id) is not { } file)
                continue;
            try
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
                {
                    DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical, DecodePixelWidth = 156,
                };
                using var stream = new MemoryStream(file).AsRandomAccessStream();
                await bitmap.SetSourceAsync(stream);
                image.Source = bitmap;
            }
            catch (Exception) { } // not a decodable image
        }
    }

    internal async Task OpenHistoryItemAsync(string id)
    {
        if (!await ConfirmDiscardAsync())
            return;
        await WriteHistoryAsync();
        if (await CaptureHistory.LoadAsync(id) is not { } file)
        {
            ShowError("This capture cannot be read. It is damaged or was encrypted for another Windows account.");
            return;
        }
        Message.IsOpen = false;
        ShowHistoryItem(id, file);
    }

    private async Task DeleteHistoryItemAsync(string id)
    {
        if (_historyId == id)
            _historyId = null; // the editor keeps the image but no longer writes to the deleted item
        await CaptureHistory.DeleteAsync(id);
    }

    private async Task ClearHistoryAsync()
    {
        if (_confirming)
            return;
        _confirming = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Delete all captures in the history?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }
        finally
        {
            _confirming = false;
        }
        _historyId = null;
        await CaptureHistory.ClearAsync();
    }

    // Text actions: inputs of the last run (redactions, crop), a run counter that drops stale results, and the recognized lines.
    private (IReadOnlyList<PixelRect> Redactions, PixelRect? Crop)? _textKey;
    private int _textRun;
    private IReadOnlyList<OcrLine> _lines = [];

    /// <summary>True while text recognition runs (test driver).</summary>
    internal bool TextActionsBusy { get; private set; }

    private void OnTextActions(object sender, RoutedEventArgs e) => SyncTextActions();

    /// <summary>With Text actions on, reads the image again when its redactions or crop changed; off clears the highlights.</summary>
    private void SyncTextActions()
    {
        if (TextActionsButton.IsChecked != true || _editor?.Document is not { } doc)
        {
            _textKey = null;
            _textRun++;
            _lines = [];
            TextActionsBusy = false;
            if (_editor is not null)
                _editor.Highlights = [];
            TextActionsBar.Visibility = Visibility.Collapsed;
            return;
        }
        TextActionsBar.Visibility = Visibility.Visible;
        if (_textKey is { } key && ReferenceEquals(key.Redactions, doc.Redactions) && key.Crop == doc.Crop)
            return;
        _textKey = (doc.Redactions, doc.Crop);
        _ = RecognizeAsync(++_textRun, _editor);
    }

    private async Task RecognizeAsync(int run, EditorCanvas editor)
    {
        TextActionsBusy = true;
        TextActionsStatus.Text = "Reading text…";
        CopyAllTextButton.IsEnabled = QuickRedactButton.IsEnabled = false;
        QrResults.Children.Clear();
        editor.Highlights = [];
        IReadOnlyList<OcrLine>? lines = [];
        IReadOnlyList<QrCode> codes = [];
        string? failure = null;
        try
        {
            if (editor.RecognitionSource() is { } source)
            {
                lines = await TextRecognizer.RecognizeAsync(source.Image, source.Origin);
                codes = await Task.Run(() => TextRecognizer.DecodeQr(source.Image, source.Origin));
            }
        }
        catch (Exception e)
        {
            failure = "Text recognition failed: " + e.Message;
        }
        if (run != _textRun)
            return;
        TextActionsBusy = false;
        _lines = lines ?? [];
        // One box per line, padded to Snipping Tool's look (image pixels); detector line boxes already carry ~9 x 7 px margin.
        editor.Highlights = [
            .. _lines.Where(l => l.Words.Count > 0).Select(l => Pad(l.Words.Select(w => w.Box).Aggregate((a, b) => a.Union(b)), new Vector2(3, 4))),
            .. codes.Select(c => Pad(c.Box, new Vector2(12)))];
        string text = lines is null
            ? "No text recognition language is installed (Settings > Time & language > Language & region)."
            : _lines.Count == 0 ? "No text found." : _lines.Count == 1 ? "1 line of text." : $"{_lines.Count} lines of text.";
        if (codes.Count > 0)
            text += codes.Count == 1 ? " 1 QR code:" : $" {codes.Count} QR codes:";
        TextActionsStatus.Text = failure ?? text;
        CopyAllTextButton.IsEnabled = QuickRedactButton.IsEnabled = _lines.Count > 0;
        for (int i = 0; i < codes.Count; i++)
            QrResults.Children.Add(QrItem(codes[i].Text, codes.Count == 1 ? "" : $" {i + 1}"));

        static FloatRect Pad(FloatRect r, Vector2 by) => new(r.Min - by, r.Max + by);
    }

    /// <summary>Payload (shortened), Copy, and Open link for http / https.</summary>
    private StackPanel QrItem(string payload, string number)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var label = new TextBlock
        {
            Text = payload.Length > 48 ? payload[..47] + "…" : payload,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 360,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetName(label, "QR code: " + payload);
        ToolTipService.SetToolTip(label, payload);
        var copy = new Button { Content = "Copy" };
        AutomationProperties.SetName(copy, "Copy QR code" + number);
        copy.Click += (_, _) => CopyText(payload, "QR code copied.");
        item.Children.Add(label);
        item.Children.Add(copy);
        if (Uri.TryCreate(payload, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var open = new Button { Content = "Open link" };
            AutomationProperties.SetName(open, "Open link" + number);
            ToolTipService.SetToolTip(open, uri.AbsoluteUri);
            open.Click += async (_, _) => await Launcher.LaunchUriAsync(uri);
            item.Children.Add(open);
        }
        return item;
    }

    private void OnCopyAllText(object sender, RoutedEventArgs e) => CopyText(OcrText.Join(_lines), "Text copied.");

    private void CopyText(string text, string done)
    {
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (CaptureOutput.CopyTextToClipboard(owner, text))
            TextActionsStatus.Text = done;
        else
            ShowError("The clipboard is in use by another app; the text was not copied.");
    }

    /// <summary>Blacks out email addresses and phone numbers in the image pixels as one undo step.</summary>
    private void OnQuickRedact(object sender, RoutedEventArgs e)
    {
        var boxes = OcrText.Sensitive(_lines, 2);
        if (boxes.Count == 0)
            TextActionsStatus.Text = "No email addresses or phone numbers found.";
        else
            _editor?.Document?.Redact(boxes); // the change reads the image again
    }

    /// <summary>Ctrl+wheel zooms around the cursor; plain wheel scrolls.</summary>
    private void OnScrollerWheel(object sender, PointerRoutedEventArgs e)
    {
        if ((e.KeyModifiers & VirtualKeyModifiers.Control) == 0)
            return;
        var point = e.GetCurrentPoint(ImageScroller);
        float old = ImageScroller.ZoomFactor;
        float zoom = Math.Clamp(old * MathF.Pow(1.25f, point.Properties.MouseWheelDelta / 120f),
            ImageScroller.MinZoomFactor, ImageScroller.MaxZoomFactor);
        double ratio = zoom / old;
        var p = point.Position;
        ImageScroller.ChangeView((ImageScroller.HorizontalOffset + p.X) * ratio - p.X,
            (ImageScroller.VerticalOffset + p.Y) * ratio - p.Y, zoom, true);
        e.Handled = true;
    }

    internal void ShowError(string text)
    {
        Message.Message = text;
        Message.IsOpen = true;
    }

    private void OnNew(object sender, RoutedEventArgs e) => NewCapture();

    private void OnNewRecording(object sender, RoutedEventArgs e)
    {
        Message.IsOpen = false;
        _ = ((App)Application.Current).CaptureAsync(video: true);
    }

    private void NewCapture()
    {
        Message.IsOpen = false;
        _ = ((App)Application.Current).CaptureAsync();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));
}
