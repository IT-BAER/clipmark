using Clipmark.App.Editor;
using Clipmark.Core.Editor;
using Clipmark.Core.Emoji;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Clipmark.App;

/// <summary>Emoji picker: search box, Recent and group tabs, and a grid of the emoji this PC's font can draw.</summary>
public sealed partial class MainPage
{
    private const int MaxEmojiHits = 200; // bounds the buttons one search builds

    private static readonly (string Key, string Icon, EmojiGroup? Group)[] EmojiTabs =
    [
        ("EmojiGroupRecent", "🕘", null), ("EmojiGroupSmileys", "😀", EmojiGroup.Smileys), ("EmojiGroupPeople", "👋", EmojiGroup.People),
        ("EmojiGroupAnimals", "🐶", EmojiGroup.Animals), ("EmojiGroupFood", "🍔", EmojiGroup.Food), ("EmojiGroupTravel", "✈️", EmojiGroup.Travel),
        ("EmojiGroupActivities", "⚽", EmojiGroup.Activities), ("EmojiGroupObjects", "💡", EmojiGroup.Objects),
        ("EmojiGroupSymbols", "❤️", EmojiGroup.Symbols), ("EmojiGroupFlags", "🏁", EmojiGroup.Flags),
    ];

    private string _emoji = "";
    private Flyout? _emojiFlyout;
    private TextBox? _emojiSearch;
    private VariableSizedWrapGrid? _emojiGrid;
    private TextBlock? _emojiNone;
    private readonly List<Button> _emojiTabButtons = [];
    private int _emojiTab;
    private EmojiIndex? _emojiIndex;
    private List<EmojiEntry>? _drawable;
    private Dictionary<string, EmojiEntry>? _emojiByText;

    /// <summary>The emoji the Emoji tool places: the last picked one.</summary>
    private string CurrentEmoji => _emoji.Length > 0 ? _emoji : AppSettings.Current.Recent.FirstOrDefault() ?? "";

    private void OpenEmojiPicker()
    {
        if (_emojiFlyout is null)
            BuildEmojiPicker();
        _emojiSearch!.Text = "";
        _emojiTab = 0;
        ShowEmojis();
        _emojiFlyout!.ShowAt(ShapeButton);
    }

    private void BuildEmojiPicker()
    {
        _emojiIndex = EmojiIndex.Load(Loc.Get("EmojiLanguage"));
        _drawable = [.. _emojiIndex.All.Where(e => EmojiFont.CanDraw(e.Emoji))];
        _emojiByText = _drawable.ToDictionary(e => e.Emoji);

        _emojiSearch = new TextBox { PlaceholderText = Loc.Get("EmojiSearch") };
        AutomationProperties.SetName(_emojiSearch, Loc.Get("EmojiSearch"));
        _emojiSearch.TextChanged += (_, _) => ShowEmojis();
        _emojiSearch.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && _emojiGrid!.Children.FirstOrDefault() is Button { Tag: string first })
            {
                PickEmoji(first);
                e.Handled = true;
            }
        };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (int i = 0; i < EmojiTabs.Length; i++)
        {
            int tab = i;
            var button = EmojiButton(EmojiTabs[i].Icon, Loc.Get(EmojiTabs[i].Key), 30, 16); // 10 x 30 + 9 x 2 fits the 328 width
            button.Click += (_, _) =>
            {
                _emojiTab = tab;
                _emojiSearch.Text = ""; // TextChanged shows the tab
                ShowEmojis();
            };
            _emojiTabButtons.Add(button);
            tabs.Children.Add(button);
        }

        _emojiGrid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal, ItemWidth = 40, ItemHeight = 40, MaximumRowsOrColumns = 8,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
        };
        _emojiNone = new TextBlock { Text = Loc.Get("EmojiNone"), Margin = new Thickness(4, 8, 4, 8), Visibility = Visibility.Collapsed };
        _emojiFlyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = new StackPanel
            {
                Width = 328, Spacing = 8,
                Children =
                {
                    _emojiSearch, tabs, _emojiNone,
                    new ScrollViewer { Height = 248, Content = _emojiGrid, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                },
            },
        };
        AutomationProperties.SetName(_emojiFlyout.Content, Loc.Get("EmojiPicker"));
        _emojiFlyout.Opened += (_, _) => _emojiSearch.Focus(FocusState.Programmatic);
        PassToolbarClicks(_emojiFlyout);
    }

    /// <summary>Fills the grid with the search hits, or with the current tab when the search box is empty.</summary>
    private void ShowEmojis()
    {
        string query = _emojiSearch!.Text;
        IEnumerable<EmojiEntry> shown = query.Trim().Length > 0
            ? _emojiIndex!.Search(query).Where(e => _emojiByText!.ContainsKey(e.Emoji)).Take(MaxEmojiHits)
            : EmojiTabs[_emojiTab].Group is { } group ? _drawable!.Where(e => e.Group == group)
            : AppSettings.Current.Recent.Select(r => _emojiByText!.GetValueOrDefault(r) ?? new EmojiEntry(r, EmojiGroup.Symbols, r));
        _emojiGrid!.Children.Clear();
        foreach (var entry in shown)
        {
            var button = EmojiButton(entry.Emoji, entry.Name, 40, 22);
            button.Tag = entry.Emoji;
            button.BorderThickness = new Thickness(0);
            button.Click += (_, _) => PickEmoji(entry.Emoji);
            _emojiGrid.Children.Add(button);
        }
        _emojiNone!.Visibility = _emojiGrid.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _emojiTabButtons.Count; i++)
            MarkButton(_emojiTabButtons[i], query.Trim().Length == 0 && i == _emojiTab);
    }

    private static Button EmojiButton(string emoji, string name, double size, double fontSize)
    {
        var button = new Button
        {
            Content = emoji, Width = size, Height = size, Padding = new Thickness(0), FontSize = fontSize,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(EmojiFont.Family),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    /// <summary>Makes the emoji current for the Emoji tool and the first of Recent.</summary>
    private void PickEmoji(string emoji)
    {
        _emoji = emoji;
        AppSettings.Update(s => s.WithRecent(emoji));
        _emojiFlyout?.Hide();
        SelectTool(EditorTool.Emoji);
    }
}
