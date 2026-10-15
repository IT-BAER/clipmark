using System.Text;
using Clipmark.App.Ai;
using Clipmark.App.Capture;
using Clipmark.App.Output;
using Clipmark.Core.Ai;
using Clipmark.Core.Licensing;
using Clipmark.Core.Relay;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Clipmark.App;

/// <summary>
/// Ask AI: a chat about the current capture in a toolbar flyout, sent to the Messages API with the user's own key, or for
/// Premium without an own key through IT-BAER's relay (<see cref="RelayChat"/>).
/// One conversation per editor session, only in memory; it ends with the capture. Closing the flyout does not cancel.
/// </summary>
public sealed partial class MainPage
{
    /// <summary>Base64 limit per image on the Claude API is 10 MB; raw bytes grow by 4/3.</summary>
    private const int MaxImageBytes = 10_000_000 / 4 * 3;

    private readonly ChatConversation _chat = new();
    private readonly List<(ChatRole Role, string Text, FrameworkElement View, RichTextBlock? Answer, string Thinking, TextBlock? ThinkingText)> _aiViews = [];
    // Text and thinking deltas from the network thread wait here for the 50 ms render tick; the lock on _aiPending also guards _aiRun and _aiPendingThinking.
    private readonly StringBuilder _aiPending = new();
    private readonly StringBuilder _aiPendingThinking = new();
    private int _aiRun;
    private CancellationTokenSource? _aiCts;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _aiTimer;
    private Flyout? _aiFlyout;
    private StackPanel _aiChatPanel = null!, _aiNoKeyPanel = null!, _aiTurns = null!;
    private ScrollViewer _aiScroll = null!;
    private TextBox _aiInput = null!;
    private Button _aiSend = null!, _aiNewChat = null!;
    private ComboBox _aiEffort = null!;
    private TextBlock _aiModelText = null!, _aiStatus = null!;
    private string? _aiSentAt;

    /// <summary>True while an answer is requested or streaming (test driver).</summary>
    internal bool AiBusy => _chat.IsBusy || _aiPreparing;

    // From Send until the request starts: consent and image encoding; a second Send is ignored.
    private bool _aiPreparing;

    /// <summary>The flyout was light-dismissed because another window took the foreground (test driver reopens it).</summary>
    internal bool AiDismissedInBackground { get; private set; }
    private bool _aiHiding;

    private void OnAskAi(object sender, RoutedEventArgs e) => OpenAskAi();

    internal void OpenAskAi()
    {
        if (_editor is null || _video is not null)
            return;
        if (!AppLicense.Current.IsEnabled(PremiumFeature.AiChat))
        {
            _ = ShowPremiumAsync(PremiumFeature.AiChat);
            return;
        }
        BuildAiFlyout();
        SyncAiFlyout();
        _aiFlyout!.ShowAt(Anchor(AskAiButton));
    }

    private void BuildAiFlyout()
    {
        if (_aiFlyout is not null)
            return;
        // Tight line bounds: centring uses the glyphs, not the line box, so the two text sizes line up.
        var title = new TextBlock { Text = Loc.Get("AskAiTitle"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center, TextLineBounds = TextLineBounds.Tight };
        _aiModelText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextLineBounds = TextLineBounds.Tight, Foreground = SecondaryBrush, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        _aiNewChat = new Button { Content = Loc.Get("AskAiNewChat"), IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right };
        ToolTipService.SetToolTip(_aiNewChat, Loc.Get("AskAiNewChatTip"));
        _aiNewChat.Click += (_, _) => ResetAiChat();
        // Same saved value as Settings > Ask AI > Answer depth; applies to the next question.
        // Plain like the model name: caption text, no fill or border.
        _aiEffort = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent), Foreground = SecondaryBrush, FontSize = 12, MinHeight = 0,
            Padding = new Thickness(4, 0, 0, 0),
        };
        foreach (string effort in AiEfforts.All)
            _aiEffort.Items.Add(EffortText(effort));
        AutomationProperties.SetName(_aiEffort, Loc.Get("AskAiEffortName"));
        ToolTipService.SetToolTip(_aiEffort, Loc.Get("AskAiEffortName"));
        _aiEffort.SelectionChanged += (_, _) =>
        {
            if (_aiEffort.SelectedIndex >= 0 && AiEfforts.All[_aiEffort.SelectedIndex] is var effort && effort != AppSettings.Current.AiEffort)
                AppSettings.Update(s => s with { AiEffort = effort });
        };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_aiModelText, 1);
        Grid.SetColumn(_aiEffort, 2);
        Grid.SetColumn(_aiNewChat, 3);
        header.Children.Add(title);
        header.Children.Add(_aiModelText);
        header.Children.Add(_aiEffort);
        header.Children.Add(_aiNewChat);

        var openSettings = new Button { Content = Loc.Get("OpenSettings") };
        openSettings.Click += (_, _) => { HideAiFlyout(); Frame.Navigate(typeof(SettingsPage), "ai"); };
        _aiNoKeyPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Loc.Get("AskAiNoKey"), TextWrapping = TextWrapping.Wrap },
                openSettings,
            },
        };

        _aiTurns = new StackPanel { Spacing = 12 };
        _aiScroll = new ScrollViewer { Content = _aiTurns, MaxHeight = 420, Padding = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(_aiScroll, Loc.Get("AskAiConversationName"));
        _aiStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush, Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(_aiStatus, Loc.Get("AskAiStatusName"));
        _aiInput = new TextBox
        {
            PlaceholderText = Loc.Get("AskAiPlaceholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 120,
        };
        AutomationProperties.SetName(_aiInput, Loc.Get("AskAiQuestionName"));
        ToolTipService.SetToolTip(_aiInput, Loc.Get("AskAiInputTip"));
        _aiInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && (Modifiers() & VirtualKeyModifiers.Shift) == 0)
            {
                e.Handled = true;
                _ = AskAsync();
            }
        };
        _aiSend = new Button { VerticalAlignment = VerticalAlignment.Bottom, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _aiSend.Click += (_, _) =>
        {
            if (_chat.IsBusy)
                _aiCts?.Cancel();
            else
                _ = AskAsync();
        };
        var input = new Grid { ColumnSpacing = 8 };
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_aiSend, 1);
        input.Children.Add(_aiInput);
        input.Children.Add(_aiSend);
        _aiChatPanel = new StackPanel { Spacing = 8, Children = { _aiScroll, _aiStatus, input } };

        _aiFlyout = new Flyout
        {
            Content = new StackPanel { Spacing = 12, Width = 480, Children = { header, _aiNoKeyPanel, _aiChatPanel } },
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter)) { Setters = { new Setter(MaxWidthProperty, 560) } },
        };
        PassToolbarClicks(_aiFlyout);
        _aiFlyout.Closing += (_, _) => AiDismissedInBackground = !_aiHiding
            && Interop.Native.GetForegroundWindow() != Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _aiFlyout.Opened += (_, _) =>
        {
            AiDismissedInBackground = false;
            if (_aiChatPanel.Visibility == Visibility.Visible)
                _aiInput.Focus(FocusState.Programmatic);
            ScrollAiToEnd();
        };
        ShowAiBusy();
    }

    // Closing runs inside Hide(), so the flag marks closes that are ours.
    private void HideAiFlyout()
    {
        _aiHiding = true;
        _aiFlyout?.Hide();
        _aiHiding = false;
    }

    private static Brush SecondaryBrush => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    /// <summary>Label for an <see cref="AiEfforts"/> value: the API term, "medium" shortened; not translated.</summary>
    internal static string EffortText(string effort) => effort == "medium" ? "med" : effort;

    /// <summary>Key hint or chat, model name and answer depth; a key saved or removed in Settings shows on the next open.</summary>
    private void SyncAiFlyout()
    {
        bool hasKey = AnthropicKeyStore.HasKey || AppLicense.CanUseRelay;
        _aiNoKeyPanel.Visibility = hasKey ? Visibility.Collapsed : Visibility.Visible;
        _aiChatPanel.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        _aiModelText.Text = AiModels.Find(AppSettings.Current.AiModel).Name;
        _aiEffort.SelectedIndex = AiEfforts.All.ToList().IndexOf(AppSettings.Current.AiEffort);
    }

    private void ShowAiBusy()
    {
        bool busy = _chat.IsBusy;
        _aiSend.Content = new FontIcon { Glyph = busy ? "" : "", FontSize = 16 };
        AutomationProperties.SetName(_aiSend, Loc.Get(busy ? "AskAiStopName" : "AskAiSendName"));
        ToolTipService.SetToolTip(_aiSend, Loc.Get(busy ? "AskAiStopTip" : "AskAiSendTip"));
        _aiNewChat.IsEnabled = _chat.Turns.Count > 0 || busy;
    }

    private void ShowAiStatus(string? text, bool error = false)
    {
        _aiStatus.Text = text ?? "";
        _aiStatus.Foreground = error ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] : SecondaryBrush;
        _aiStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool HasAiConsent(bool relay) => relay ? AppSettings.Current.AiConsentRelay : AppSettings.Current.AiConsent;

    /// <summary>Consent once per path (direct or relay): what is sent where. False keeps the question unsent.</summary>
    private async Task<bool> ConfirmAiConsentAsync(bool relay)
    {
        if (HasAiConsent(relay))
            return true;
        if (_confirming)
            return false;
        _confirming = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = Loc.Get("AiConsentTitle"),
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = relay ? Loc.Format("AiConsentBodyRelay", RelayChat.Host) : Loc.Format("AiConsentBody", ClaudeChat.Host),
                },
                PrimaryButtonText = Loc.Get("AiConsentSend"),
                CloseButtonText = Loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return false;
            AppSettings.Update(s => relay ? s with { AiConsentRelay = true } : s with { AiConsent = true });
            return true;
        }
        finally
        {
            _confirming = false;
        }
    }

    private async Task AskAsync()
    {
        string question = _aiInput.Text.Trim();
        if (_chat.IsBusy || _aiPreparing || question.Length == 0 || _editor is not { } editor)
            return;
        if (!AppLicense.Current.IsEnabled(PremiumFeature.AiChat))
        {
            HideAiFlyout(); // the trial ended while the flyout was open
            _ = ShowPremiumAsync(PremiumFeature.AiChat);
            return;
        }
        // An own key always goes direct; Premium by entitlement without one uses the relay.
        string? key = AnthropicKeyStore.Read();
        bool relay = key is null && AppLicense.CanUseRelay;
        if (key is null && !relay)
        {
            SyncAiFlyout();
            return;
        }
        _aiPreparing = true;
        try
        {
            if (!await PrepareAiAsync(editor, relay))
                return;
        }
        finally
        {
            _aiPreparing = false;
        }
        int run = _aiRun;
        _chat.Ask(question);
        _aiInput.Text = "";
        var model = AiModels.Find(AppSettings.Current.AiModel);
        byte[] body = relay ? RelayRequest.BuildAsk(_chat, model.Id, AppSettings.Current.AiEffort) : ChatRequest.Build(_chat, model.Id, AppSettings.Current.AiEffort);
        var cts = _aiCts = new CancellationTokenSource();
        SyncAiTurns();
        ShowAiBusy();
        ShowAiStatus(Loc.Format("AskAiAnswering", model.Name, _aiSentAt));
        StartAiTimer();

        string? stop = null, error = null;
        bool cancelled = false;
        try
        {
            void OnText(string text)
            {
                lock (_aiPending)
                {
                    if (run == _aiRun)
                        _aiPending.Append(text);
                }
            }
            void OnThinking(string thinking)
            {
                lock (_aiPending)
                {
                    if (run == _aiRun)
                        _aiPendingThinking.Append(thinking);
                }
            }
            stop = relay
                ? await RelayChat.SendAsync(body, OnText, cts.Token, OnThinking)
                : await ClaudeChat.SendAsync(key!, body, OnText, cts.Token, OnThinking);
        }
        catch (AiException e)
        {
            error = e.Message;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            cts.Dispose();
        }
        if (run != _aiRun)
            return; // the conversation was reset; its views are gone
        _aiCts = null;
        _aiTimer?.Stop();
        DrainAiText();
        // Complete and Fail both end the turn; an answer without text (thinking only) returns its question.
        string? unanswered = _chat.Fail();
        if (unanswered is not null && _aiInput.Text.Length == 0)
            _aiInput.Text = unanswered; // no text came back: the question returns to the box
        SyncAiTurns();
        ShowAiBusy();
        string? sent = relay && RelayChat.RemainingMonthlyUsd is { } usd
            ? string.Join(" · ", new[] { _aiSentAt, Loc.Format("AskAiRemaining", usd.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)) }.Where(s => !string.IsNullOrEmpty(s)))
            : _aiSentAt;
        ShowAiStatus(error ?? (cancelled ? Loc.Get("AskAiStopped") : ApiError.StopMessage(stop) ?? sent), error is not null);
    }

    /// <summary>Consent (once), then the image for a new conversation; false when nothing should be sent.</summary>
    private async Task<bool> PrepareAiAsync(Editor.EditorCanvas editor, bool relay)
    {
        if (!HasAiConsent(relay))
        {
            HideAiFlyout(); // one popup at a time: the dialog, then the chat again
            bool agreed = await ConfirmAiConsentAsync(relay);
            _aiFlyout!.ShowAt(Anchor(AskAiButton));
            if (!agreed)
                return false;
        }
        if (_chat.ImageBase64 is not null)
            return true;
        if (editor.Export() is not { } image)
            return false;
        int run = _aiRun;
        var size = ImageFit.Fit(image.Width, image.Height);
        var (bytes, mediaType) = await EncodeForAiAsync(image, size);
        if (run != _aiRun || _editor != editor)
            return false; // New chat or another capture meanwhile
        if (bytes is null)
        {
            ShowAiStatus(Loc.Get("AiCaptureTooLarge"), error: true);
            return false;
        }
        _chat.Start(Convert.ToBase64String(bytes), mediaType);
        _aiSentAt = Loc.Format("AskAiImageSent", size.Width, size.Height);
        return true;
    }

    /// <summary>The flattened export, scaled to the API limit: PNG, or JPEG when the PNG is over the size limit; null if both are.</summary>
    private static async Task<(byte[]? Bytes, string MediaType)> EncodeForAiAsync(CapturedImage image, (int Width, int Height) size)
    {
        var png = await CaptureOutput.EncodePngAsync(image, size);
        if (png.Length <= MaxImageBytes)
            return (png, "image/png");
        var jpeg = await CaptureOutput.EncodeJpegAsync(image, size);
        return (jpeg.Length <= MaxImageBytes ? jpeg : null, "image/jpeg");
    }

    private void StartAiTimer()
    {
        if (_aiTimer is null)
        {
            _aiTimer = DispatcherQueue.CreateTimer();
            _aiTimer.Interval = TimeSpan.FromMilliseconds(50);
            _aiTimer.Tick += (_, _) =>
            {
                if (DrainAiText())
                    SyncAiTurns();
            };
        }
        _aiTimer.Start();
    }

    /// <summary>Moves the thinking and text received so far into the conversation; true when there was any.</summary>
    private bool DrainAiText()
    {
        string text, thinking;
        lock (_aiPending)
        {
            text = _aiPending.ToString();
            thinking = _aiPendingThinking.ToString();
            _aiPending.Clear();
            _aiPendingThinking.Clear();
        }
        if (thinking.Length > 0)
            _chat.AppendThinking(thinking);
        if (text.Length > 0)
            _chat.AppendDelta(text);
        return text.Length > 0 || thinking.Length > 0;
    }

    /// <summary>Ends the conversation (New chat, a new capture, closing the capture); a running answer is cancelled and dropped.</summary>
    private void ResetAiChat()
    {
        lock (_aiPending)
        {
            _aiRun++;
            _aiPending.Clear();
            _aiPendingThinking.Clear();
        }
        _aiCts?.Cancel();
        _aiCts = null;
        _aiTimer?.Stop();
        _chat.Reset();
        _aiSentAt = null;
        AiDismissedInBackground = false;
        if (_aiFlyout is null)
            return;
        SyncAiTurns();
        ShowAiBusy();
        ShowAiStatus(null);
    }

    /// <summary>Brings the views in line with the turns: new turns get a view, a growing answer is rendered again, dropped turns go.</summary>
    private void SyncAiTurns()
    {
        var turns = _chat.Turns;
        int keep = 0;
        while (keep < _aiViews.Count && keep < turns.Count && _aiViews[keep].Role == turns[keep].Role
               && (_aiViews[keep].Role == ChatRole.Assistant || _aiViews[keep].Text == turns[keep].Text))
            keep++;
        while (_aiViews.Count > keep)
        {
            _aiTurns.Children.Remove(_aiViews[^1].View);
            _aiViews.RemoveAt(_aiViews.Count - 1);
        }
        bool changed = false;
        for (int i = 0; i < turns.Count; i++)
        {
            if (i == _aiViews.Count)
            {
                _aiViews.Add(TurnView(i, turns[i]));
                _aiTurns.Children.Add(_aiViews[i].View);
                changed = true;
            }
            var view = _aiViews[i];
            if (view.Answer is not null && (view.Text != turns[i].Text || view.Thinking != turns[i].Thinking))
            {
                var turn = turns[i];
                if (view.Text != turn.Text)
                    MarkdownView.Render(view.Answer, turn.Text);
                ShowAnswerParts((StackPanel)view.View, view.ThinkingText!, turn);
                _aiViews[i] = view with { Text = turn.Text, Thinking = turn.Thinking };
                changed = true;
            }
        }
        if (changed)
            ScrollAiToEnd();
    }

    private (ChatRole, string, FrameworkElement, RichTextBlock?, string, TextBlock?) TurnView(int index, ChatTurn turn)
    {
        if (turn.Role == ChatRole.User)
        {
            var bubble = new Border
            {
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 380,
                Child = new TextBlock { Text = turn.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            };
            AutomationProperties.SetName(bubble, Loc.Format("AskAiQuestionBubbleName", turn.Text));
            return (ChatRole.User, turn.Text, bubble, null, "", null);
        }
        var answer = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetName(answer, Loc.Get("AskAiAnswerName"));
        var copy = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(copy, Loc.Get("AskAiCopyAnswer"));
        ToolTipService.SetToolTip(copy, Loc.Get("AskAiCopyAnswer"));
        copy.Click += (_, _) =>
        {
            if (index < _chat.Turns.Count && !CaptureOutput.CopyTextToClipboard(
                    Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId), _chat.Turns[index].Text))
                ShowAiStatus(Loc.Get("ClipboardBusyAnswer"), error: true);
        };
        var thinking = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = SecondaryBrush,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var expander = new Expander
        {
            Header = new TextBlock { Text = Loc.Get("AskAiThinking"), Foreground = SecondaryBrush, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] },
            IsExpanded = false, Content = thinking, MinHeight = 0, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        // Plain like the header: lightweight styling drops the card fill, border and height.
        var clear = new SolidColorBrush(Colors.Transparent);
        foreach (string key in (string[])["ExpanderHeaderBackground", "ExpanderHeaderBorderBrush", "ExpanderContentBackground", "ExpanderContentBorderBrush"])
            expander.Resources[key] = clear;
        expander.Resources["ExpanderMinHeight"] = 0d;
        expander.Resources["ExpanderHeaderPadding"] = new Thickness(0, 0, 8, 0);
        expander.Resources["ExpanderContentPadding"] = new Thickness(0, 4, 0, 4);
        AutomationProperties.SetName(expander, Loc.Get("AskAiThinking"));
        var panel = new StackPanel { Spacing = 2, Children = { expander, answer, copy } };
        ShowAnswerParts(panel, thinking, new ChatTurn(ChatRole.Assistant, "", ""));
        return (ChatRole.Assistant, "", panel, answer, "", thinking);
    }

    /// <summary>The panel holds the thinking expander, the answer and the copy button; a part without content is hidden.</summary>
    private static void ShowAnswerParts(StackPanel panel, TextBlock thinking, ChatTurn turn)
    {
        thinking.Text = turn.Thinking;
        panel.Children[0].Visibility = turn.Thinking.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        panel.Children[2].Visibility = turn.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollAiToEnd() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _aiScroll?.ChangeView(null, _aiScroll.ScrollableHeight, null, true));
}
