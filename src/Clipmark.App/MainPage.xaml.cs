using Clipmark.App.Editor;
using Clipmark.App.Output;
using Clipmark.Core.Capture;
using Clipmark.Core.Editor;
using Clipmark.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
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
    private static readonly float[] PenWidths = [2, 4, 8], HighlighterWidths = [12, 20, 32];

    // Per tool group (0 pen, 1 highlighter, 2 shapes): index into QuickColors and WidthNames.
    private readonly int[] _colorIndex = [2, 3, 2];
    private readonly int[] _widthIndex = [1, 1, 1];
    private EditorTool _tool = EditorTool.Pen;
    private EditorTool _shape = EditorTool.Rectangle;

    private int Group => _tool switch { EditorTool.Pen => 0, EditorTool.Highlighter => 1, _ => 2 };

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
        for (int i = 0; i < QuickColors.Length; i++)
        {
            int index = i;
            var item = new MenuFlyoutItem
            {
                Text = QuickColors[i].Name,
                Icon = new FontIcon { Glyph = "", Foreground = new SolidColorBrush(EditorCanvas.ToColor(QuickColors[i].Color)) },
            };
            item.Click += (_, _) => { _colorIndex[Group] = index; ApplyTool(); };
            StyleMenu.Items.Add(item);
        }
        StyleMenu.Items.Add(new MenuFlyoutSeparator());
        for (int i = 0; i < WidthNames.Length; i++)
        {
            int index = i;
            var item = new MenuFlyoutItem { Text = WidthNames[i] };
            item.Click += (_, _) => { _widthIndex[Group] = index; ApplyTool(); };
            StyleMenu.Items.Add(item);
        }
        Shortcut(VirtualKey.P, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Pen));
        Shortcut(VirtualKey.H, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Highlighter));
        Shortcut(VirtualKey.E, VirtualKeyModifiers.None, () => SelectTool(EditorTool.Eraser));
        Shortcut(VirtualKey.Z, VirtualKeyModifiers.Control, () => _editor?.Undo());
        Shortcut(VirtualKey.Y, VirtualKeyModifiers.Control, () => _editor?.Redo());
        Shortcut(VirtualKey.C, VirtualKeyModifiers.Control, () => _ = CopyAsync());
        Shortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveAsync());
        Shortcut(VirtualKey.N, VirtualKeyModifiers.Control, NewCapture, needsCapture: false);
        PreviewKeyDown += OnCropKey;
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        ApplyTool();
#if DEBUG
        if (App.TestDir is { } testDir)
        {
            var driver = new TestDriver(this, testDir);
            Loaded += (_, _) => driver.Start();
            Unloaded += (_, _) => driver.Stop();
        }
#endif

        ImageScroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnScrollerWheel), true);
        ImageScroller.SizeChanged += (_, _) => Relayout();
        ImageScroller.ViewChanged += (_, e) => { if (!e.IsIntermediate) Relayout(); };
        Loaded += (_, _) => AppSettings.Changed += OnSettingsChanged;
        Unloaded += (_, _) => AppSettings.Changed -= OnSettingsChanged;
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

        Hint.Text = $"Select New or press {s.Hotkey} to capture.";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string path)
            ShowImage(path);
    }

    private EditorCanvas? _editor;

    internal EditorCanvas? Editor => _editor;

    internal void ShowImage(string path)
    {
        _editor?.Close();
        _editor = new EditorCanvas(path);
        _editor.Changed += () => { UpdateHistory(); ApplyTool(); Relayout(); };
        ApplyTool();
        UpdateHistory();
        ImageScroller.Content = _editor.Control;
        Hint.Visibility = Visibility.Collapsed;
        ImageScroller.Visibility = Visibility.Visible;
        EditTools.Visibility = Visibility.Visible;
        OutputTools.Visibility = Visibility.Visible;
    }

    /// <summary>Ends the editor session and frees its Win2D resources; the page shows the start hint again.</summary>
    internal void CloseCapture()
    {
        if (_editor is null)
            return;
        _editor.Close();
        _editor = null;
        ImageScroller.Content = null;
        ImageScroller.Visibility = Visibility.Collapsed;
        EditTools.Visibility = Visibility.Collapsed;
        OutputTools.Visibility = Visibility.Collapsed;
        Hint.Visibility = Visibility.Visible;
        UpdateHistory();
    }

    private bool _confirming;

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
                ContentDialogResult.Primary => await SaveAsync(),
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

    /// <summary>Page-wide shortcut; unless <paramref name="needsCapture"/> is false it does nothing until a capture is shown.</summary>
    private void Shortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool needsCapture = true)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            if (_editor is not null || !needsCapture)
                action();
        };
        KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>Enter applies and Esc cancels a crop; tunneling, so a focused button (often Crop itself) does not take Enter first.</summary>
    private void OnCropKey(object sender, KeyRoutedEventArgs e)
    {
        if (!Cropping || e.Key is not (VirtualKey.Enter or VirtualKey.Escape))
            return;
        e.Handled = true;
        if (e.Key == VirtualKey.Enter)
            _editor?.ApplyCrop();
        else
            _editor?.CancelCrop();
    }

    private bool Cropping => _editor?.IsCropping == true;

    private void SelectTool(EditorTool tool)
    {
        _editor?.CancelCrop();
        _tool = tool;
        if (tool >= EditorTool.Rectangle)
            _shape = tool;
        ApplyTool();
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
        PenButton.IsChecked = !cropping && _tool == EditorTool.Pen;
        HighlighterButton.IsChecked = !cropping && _tool == EditorTool.Highlighter;
        EraserButton.IsChecked = !cropping && _tool == EditorTool.Eraser;
        ShapeButton.IsChecked = !cropping && _tool >= EditorTool.Rectangle;
        CropButton.IsChecked = cropping;
        ApplyCropButton.Visibility = CancelCropButton.Visibility = cropping ? Visibility.Visible : Visibility.Collapsed;
        int shapeIndex = Array.FindIndex(Shapes, s => s.Tool == _shape);
        ShapeButton.Content = new FontIcon { Glyph = Shapes[shapeIndex].Glyph, FontSize = 16 };
        AutomationProperties.SetName(ShapeButton, "Shapes: " + Shapes[shapeIndex].Text);
        Mark(ShapeMenu, 0, Shapes.Length, shapeIndex);

        int group = Group;
        var color = QuickColors[_colorIndex[group]];
        float width = (group == 1 ? HighlighterWidths : PenWidths)[_widthIndex[group]];
        StyleButton.IsEnabled = _tool != EditorTool.Eraser;
        StyleButton.Content = new FontIcon
        {
            Glyph = "", FontSize = 16, Foreground = new SolidColorBrush(EditorCanvas.ToColor(color.Color)),
        };
        AutomationProperties.SetName(StyleButton, $"Color and width: {color.Name}, {WidthNames[_widthIndex[group]]}");
        Mark(StyleMenu, 0, QuickColors.Length, _colorIndex[group]);
        Mark(StyleMenu, QuickColors.Length + 1, WidthNames.Length, _widthIndex[group]);

        if (_editor is not null)
        {
            _editor.Tool = _tool;
            _editor.Color = color.Color;
            _editor.StrokeWidth = width;
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

    private void UpdateHistory()
    {
        UndoButton.IsEnabled = _editor?.Document?.CanUndo == true;
        RedoButton.IsEnabled = _editor?.Document?.CanRedo == true;
    }

    private void OnCropClick(object sender, RoutedEventArgs e)
    {
        if (Cropping)
            _editor?.CancelCrop();
        else
            _editor?.BeginCrop();
        ApplyTool();
    }

    private void OnApplyCrop(object sender, RoutedEventArgs e) => _editor?.ApplyCrop();

    private void OnCancelCrop(object sender, RoutedEventArgs e) => _editor?.CancelCrop();

    private void OnUndo(object sender, RoutedEventArgs e) => _editor?.Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => _editor?.Redo();

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    private async Task CopyAsync()
    {
        var editor = _editor;
        if (editor?.Export() is not { } image)
            return;
        var png = await CaptureOutput.EncodePngAsync(image);
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (CaptureOutput.CopyToClipboard(owner, image, png))
            editor.Document?.MarkClean();
        else
            ShowError("The clipboard is in use by another app; the capture was not copied.");
    }

    private void OnSave(object sender, RoutedEventArgs e) => _ = SaveAsync();

    /// <summary>Save as dialog (PNG or JPEG) in the save folder; true when the file was written.</summary>
    private async Task<bool> SaveAsync()
    {
        if (_editor is null)
            return false;
        string folder = AppSettings.Current.SaveFolder ?? CaptureOutput.DefaultFolder;
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(
                CaptureFileName.Next(DateTime.Now, name => File.Exists(Path.Combine(folder, name)))),
            DefaultFileExtension = ".png",
        };
        if (Directory.Exists(folder))
            picker.SuggestedFolder = folder;
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        picker.FileTypeChoices.Add("JPEG image", [".jpg", ".jpeg"]);
        var result = await picker.PickSaveFileAsync();
        return result is not null && await SaveToAsync(result.Path);
    }

    /// <summary>Writes the export as JPEG (by extension) or PNG and marks the document saved.</summary>
    internal async Task<bool> SaveToAsync(string path)
    {
        var editor = _editor;
        if (editor?.Export() is not { } image)
            return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var bytes = ext is ".jpg" or ".jpeg" ? await CaptureOutput.EncodeJpegAsync(image) : await CaptureOutput.EncodePngAsync(image);
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

    private void OnShare(object sender, RoutedEventArgs e) => _ = ShareAsync();

    private async Task ShareAsync()
    {
        if (_editor?.Export() is not { } image)
            return;
        var owner = Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        try
        {
            await ShareSheet.ShowAsync(owner, await CaptureOutput.EncodePngAsync(image));
        }
        catch (Exception e)
        {
            ShowError($"The share sheet did not open: {e.Message}");
        }
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

    private void NewCapture()
    {
        Message.IsOpen = false;
        _ = ((App)Application.Current).CaptureAsync();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));
}
