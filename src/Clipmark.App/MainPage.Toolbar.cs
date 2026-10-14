using Clipmark.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Clipmark.App;

/// <summary>
/// Toolbar fit, as in Snipping Tool: one row while everything fits, else the edit tools get a row of their own (and wrap
/// there) and the top row gives up, in this order, the New label, Send to AI and Ask AI, Open and History (both pairs
/// move to More), the delay label.
/// </summary>
public sealed partial class MainPage
{
    private const int CompactLevels = 5;

    /// <summary>False while a recording is shown: the agents and Ask AI take images.</summary>
    private bool _aiTools = true;

    /// <summary>The "3 s" text on the Delay button, null without a delay.</summary>
    private TextBlock? _delayLabel;

    private int _toolbarLevel;

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width != e.PreviousSize.Width)
            FitToolbar();
    }

    /// <summary>Picks the first level (0 = one row) at which the toolbar fits; call it after a toolbar button appears or goes.</summary>
    private void FitToolbar()
    {
        double available = Toolbar.ActualWidth - Toolbar.Padding.Left - Toolbar.Padding.Right;
        if (available <= 0)
        {
            ApplyToolbarLevel(_toolbarLevel); // not laid out yet: SizeChanged fits it
            return;
        }
        int level = 0;
        while (true)
        {
            ApplyToolbarLevel(level);
            if (level == CompactLevels || ToolbarFit.Fits(level == 0, available, Toolbar.ColumnSpacing,
                    Natural(CaptureTools), Natural(EditTools), Natural(RightTools)))
                break;
            level++;
        }
        _toolbarLevel = level;

        static double Natural(FrameworkElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize.Width;
        }
    }

    private void ApplyToolbarLevel(int level)
    {
        // Two rows: both side groups span the row (left / right aligned), the edit tools span the second row.
        bool twoRows = level >= 1;
        Toolbar.ColumnDefinitions[1].Width = twoRows ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumnSpan(CaptureTools, twoRows ? 3 : 1);
        Grid.SetColumn(RightTools, twoRows ? 0 : 2);
        Grid.SetColumnSpan(RightTools, twoRows ? 3 : 1);
        Grid.SetRow(EditTools, twoRows ? 1 : 0);
        Grid.SetColumn(EditTools, twoRows ? 0 : 1);
        Grid.SetColumnSpan(EditTools, twoRows ? 3 : 1);
        EditTools.Margin = new Thickness(0, twoRows ? 8 : 0, 0, 0);

        NewButtonText.Visibility = Shown(level < 2);
        bool ai = _aiTools && OutputTools.Visibility == Visibility.Visible;
        AgentButton.Visibility = AskAiButton.Visibility = AiSeparator.Visibility = Shown(_aiTools && level < 3);
        AgentItem.Visibility = AskAiItem.Visibility = Shown(ai && level >= 3);
        OpenButton.Visibility = HistoryButton.Visibility = Shown(level < 4);
        OpenItem.Visibility = HistoryItem.Visibility = Shown(level >= 4);
        CompactSeparator.Visibility = Shown((ai && level >= 3) || level >= 4);
        if (_delayLabel is not null)
            _delayLabel.Visibility = Shown(level < 5);

        static Visibility Shown(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Where a flyout of a toolbar button opens: the button, or More while a narrow window keeps the button there.</summary>
    private FrameworkElement Anchor(FrameworkElement button) => button.Visibility == Visibility.Visible ? button : MoreButton;
}
