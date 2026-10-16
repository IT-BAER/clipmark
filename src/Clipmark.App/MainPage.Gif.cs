using Clipmark.App.Output;
using Clipmark.App.Recording;
using Clipmark.Core.Recording;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

namespace Clipmark.App;

/// <summary>
/// Create GIF from the shown recording, as in Snipping Tool: Low / High quality, size info, Export or Copy. The dialog stays
/// open with a progress bar while the GIF is written; Cancel or Escape stops it and leaves no file.
/// </summary>
public sealed partial class MainPage
{
    private void OnGif(object sender, RoutedEventArgs e) => _ = CreateGifAsync();

    private async Task CreateGifAsync()
    {
        if (_video is not { } video || _confirming)
            return;
        _confirming = true;
        try
        {
            (int Width, int Height, TimeSpan Duration) info;
            try
            {
                info = await Task.Run(() => GifExport.Probe(video));
            }
            catch (Exception e)
            {
                ShowError(Loc.Format("GifNotCreated", e.Message));
                return;
            }
            await ShowGifDialogAsync(video, info.Width, info.Height, info.Duration);
        }
        finally
        {
            _confirming = false;
        }
    }

    private async Task ShowGifDialogAsync(string video, int width, int height, TimeSpan duration)
    {
        var caption = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
        var divider = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        RadioButton Choice(string key)
        {
            var choice = new RadioButton
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Loc.Get(key) },
                        new TextBlock { Text = Loc.Get(key + "Description"), Style = caption, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            AutomationProperties.SetName(choice, Loc.Get(key));
            AutomationProperties.SetHelpText(choice, Loc.Get(key + "Description"));
            return choice;
        }
        var low = Choice("GifLow");
        var high = Choice("GifHigh");
        var quality = new RadioButtons { Header = Loc.Get("GifQuality"), Items = { low, high }, SelectedIndex = 0 };
        GifQuality Selected() => high.IsChecked == true ? GifQuality.High : GifQuality.Low;
        var fps = new TextBlock { Style = caption };
        quality.SelectionChanged += (_, _) => fps.Text = Loc.Format("GifFps", GifPlan.Fps(Selected()));
        fps.Text = Loc.Format("GifFps", GifPlan.Fps(Selected()));
        var (w, h) = GifPlan.Size(width, height, GifPlan.MaxEdge);
        int seconds = Math.Max(1, (int)Math.Round(Math.Min(GifPlan.MaxSeconds, duration.TotalSeconds)));
        var progress = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 16, 0, 0), Visibility = Visibility.Collapsed };
        var content = new StackPanel
        {
            Children =
            {
                quality,
                new Border { Height = 1, Margin = new Thickness(0, 16, 0, 16), Background = divider },
                new TextBlock { Text = Loc.Get("GifSizeInfo"), Margin = new Thickness(0, 0, 0, 8) },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = Loc.Format("GifSeconds", seconds), Style = caption },
                        new Border { Width = 1, Background = divider },
                        new TextBlock { Text = Loc.Format("GifPixels", w, h), Style = caption },
                        new Border { Width = 1, Background = divider },
                        fps,
                    },
                },
                progress,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = Loc.Get("GifTitle"),
            Content = content,
            PrimaryButtonText = Loc.Get("GifExport"),
            SecondaryButtonText = Loc.Get("GifCopy"),
            CloseButtonText = Loc.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        CancellationTokenSource? work = null;
        dialog.PrimaryButtonClick += (sender, args) =>
        {
            args.Cancel = true;
            _ = WriteAsync(copy: false);
        };
        dialog.SecondaryButtonClick += (sender, args) =>
        {
            args.Cancel = true;
            _ = WriteAsync(copy: true);
        };
        dialog.Closing += (_, _) => work?.Cancel();
        await dialog.ShowAsync();

        async Task WriteAsync(bool copy)
        {
            if (work is not null)
                return;
            string? target = copy ? Path.Combine(CaptureOutput.TempFolder, Path.GetFileNameWithoutExtension(video) + ".gif") : await PickGifAsync(video);
            if (target is null)
                return;
            work = new CancellationTokenSource();
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = quality.IsEnabled = false;
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;
            string temp = Path.Combine(CaptureOutput.TempFolder, $"clipmark-{Guid.NewGuid():N}.gif");
            try
            {
                Directory.CreateDirectory(CaptureOutput.TempFolder);
                await GifExport.WriteAsync(video, temp, Selected(), new Progress<double>(v => progress.Value = v), work.Token);
                work.Token.ThrowIfCancellationRequested();
                await Task.Run(() => File.Move(temp, target, overwrite: true));
                dialog.Hide();
                if (copy)
                {
                    if (!CaptureOutput.CopyFileToClipboard(Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), target))
                        ShowError(Loc.Get("ClipboardBusyGif"));
                    else if (AppSettings.Current.CloseAfterCopy)
                        ((App)Application.Current).CloseWindow();
                }
            }
            catch (OperationCanceledException)
            {
                DeleteQuietly(temp); // the dialog closed: Cancel or Escape
            }
            catch (Exception e) // ThrowExceptionForHR also maps HRESULTs to ArgumentException, NotImplementedException and others
            {
                DeleteQuietly(temp);
                dialog.Hide();
                ShowError(Loc.Format("GifNotCreated", e.Message));
            }
            finally
            {
                var done = work;
                work = null;
                done.Dispose();
            }
        }
    }

    private async Task<string?> PickGifAsync(string video)
    {
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(video),
            DefaultFileExtension = ".gif",
        };
        picker.FileTypeChoices.Add(Loc.Get("FileTypeGif"), [".gif"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // left for CleanTempFolder
        }
    }
}
