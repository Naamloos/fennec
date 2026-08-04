using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatComposer : ContentView
{
    private Entry? _entry;
    private readonly EmojiPicker _emojiPicker;
    private int _triggerPosition = -1;

    [BindableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [BindableProperty]
    public partial bool HasError { get; set; }

    [BindableProperty]
    public partial ChatTimelineItem? ReplyTo { get; set; }

    [BindableProperty]
    public partial ChatTimelineItem? EditTarget { get; set; }

    [BindableProperty]
    public partial ICommand? CancelReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? CancelEditCommand { get; set; }

    [BindableProperty]
    public partial ICommand? AttachCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MoreCommand { get; set; }

    [BindableProperty]
    public partial ICommand? InlineAttachmentCommand { get; set; }

    [BindableProperty]
    public partial ICommand? SendCommand { get; set; }

    [BindableProperty]
    public partial string Text { get; set; } = string.Empty;

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial IEnumerable<MatrixEmote>? Emotes { get; set; }

    [BindableProperty]
    public partial ChatSession? Session { get; set; }

    [BindableProperty]
    public partial bool IsEmojiPickerOpen { get; set; }

    [BindableProperty]
    public partial IEnumerable<ManagedRoom>? Rooms { get; set; }

    [BindableProperty]
    public partial string PreviewHtml { get; set; } = string.Empty;

    [BindableProperty]
    public partial bool IsAutocompleteOpen { get; set; }

    [BindableProperty]
    public partial ComposerAutocompleteMode AutocompleteMode { get; set; }

    [BindableProperty]
    public partial string AutocompleteQuery { get; set; } = string.Empty;

    public ChatComposer()
    {
        _emojiPicker = CreateEmojiPicker();
        Build();
    }

    private void Build()
    {
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new Label
                {
                    TextColor = Colors.Red,
                    Margin = new Thickness(12, 4),
                    LineBreakMode = LineBreakMode.WordWrap,
                }
                    .Bind(Label.TextProperty, nameof(ErrorMessage), source: this)
                    .Bind(IsVisibleProperty, nameof(HasError), source: this)
                    .Row(0),
                new Grid
                {
                    Margin = new Thickness(12, 0),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    },
                    Children =
                    {
                        new Label { FontSize = 12, Opacity = .75 }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(ReplyTo)}.{nameof(ChatTimelineItem.Sender)}",
                                stringFormat: "Replying to {0}",
                                source: this
                            )
                            .Column(0),
                        new MauiIcon
                        {
                            Icon = MaterialIcons.Close,
                            IconSize = 20,
                            IconColor = Colors.Red,
                            WidthRequest = 44,
                            HeightRequest = 44,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer().BindCommand(
                                    nameof(CancelReplyCommand),
                                    source: this
                                ),
                            },
                        }
                            .Invoke(view => SemanticProperties.SetDescription(view, "Cancel reply"))
                            .Column(1),
                    },
                }
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(ReplyTo)}",
                        converter: new NotNullConverter(),
                        source: this
                    )
                    .Row(1),
                new Grid
                {
                    Margin = new Thickness(12, 0),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    },
                    Children =
                    {
                        new Label { FontSize = 12, Opacity = .75 }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(EditTarget)}.{nameof(ChatTimelineItem.Sender)}",
                                stringFormat: "Editing {0}",
                                source: this
                            )
                            .Column(0),
                        new MauiIcon
                        {
                            Icon = MaterialIcons.Close,
                            IconSize = 20,
                            IconColor = Colors.Red,
                            WidthRequest = 44,
                            HeightRequest = 44,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer().BindCommand(
                                    nameof(CancelEditCommand),
                                    source: this
                                ),
                            },
                        }
                            .Invoke(view => SemanticProperties.SetDescription(view, "Cancel edit"))
                            .Column(1),
                    },
                }
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(EditTarget)}",
                        converter: new NotNullConverter(),
                        source: this
                    )
                    .Row(1),
                new MatrixHtmlView
                {
                    Margin = new Thickness(12, 0, 12, 4),
                    Opacity = .72,
                    MaximumHeightRequest = 84,
                }
                    .Bind(MatrixHtmlView.HtmlProperty, nameof(PreviewHtml), source: this)
                    .Bind(
                        IsVisibleProperty,
                        nameof(PreviewHtml),
                        converter: new IsStringNotNullOrEmptyConverter(),
                        source: this
                    )
                    .Row(2),
                new Grid
                {
                    Padding =
                        DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? new Thickness(6, 8)
                            : new Thickness(12),
                    Children =
                    {
                        new Grid
                        {
                            ColumnSpacing = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 4 : 8,
                            ColumnDefinitions =
                            {
                                new ColumnDefinition(GridLength.Auto),
                                new ColumnDefinition(GridLength.Star),
                                new ColumnDefinition(GridLength.Auto),
                                new ColumnDefinition(GridLength.Auto),
                                new ColumnDefinition(GridLength.Auto),
                            },
                            Children =
                            {
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.AttachFile,
                                    IconSize = 24,
                                    WidthRequest = 44,
                                    HeightRequest = 44,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer().BindCommand(
                                            nameof(AttachCommand),
                                            source: this
                                        ),
                                    },
                                }
                                    .Invoke(view =>
                                        SemanticProperties.SetDescription(view, "Attach file")
                                    )
                                    .Column(0),
                                CreateEntry().Column(1),
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.AddReaction,
                                    IconSize = 24,
                                    WidthRequest = 44,
                                    HeightRequest = 44,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer().BindCommand(
                                            nameof(OpenEmojiPickerCommand),
                                            source: this
                                        ),
                                    },
                                }
                                    .Invoke(view =>
                                        SemanticProperties.SetDescription(view, "Choose emoji")
                                    )
                                    .Column(2),
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.Add,
                                    IconSize = 24,
                                    WidthRequest = 44,
                                    HeightRequest = 44,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer().BindCommand(
                                            nameof(MoreCommand),
                                            source: this
                                        ),
                                    },
                                }
                                    .Bind(
                                        IsVisibleProperty,
                                        nameof(MoreCommand),
                                        converter: new NotNullConverter(),
                                        source: this
                                    )
                                    .Invoke(view =>
                                        SemanticProperties.SetDescription(
                                            view,
                                            "More message options"
                                        )
                                    )
                                    .Column(3),
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.Send,
                                    IconSize = 24,
                                    WidthRequest = 44,
                                    HeightRequest = 44,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer().BindCommand(
                                            nameof(SendCommand),
                                            source: this
                                        ),
                                    },
                                }
                                    .Bind(
                                        IsEnabledProperty,
                                        $"{nameof(Session)}.{nameof(ChatSession.CanSend)}",
                                        source: this
                                    )
                                    .Invoke(view =>
                                        SemanticProperties.SetDescription(view, "Send message")
                                    )
                                    .Column(4),
                            },
                        },
                        new ComposerAutocomplete
                        {
                            VerticalOptions = LayoutOptions.End,
                            HorizontalOptions = LayoutOptions.Start,
                            TranslationY = -42,
                            ZIndex = 1,
                            PickMemberCommand = new Command<RoomMember>(PickMember),
                            PickEmoteCommand = new Command<MatrixEmote>(PickEmote),
                            PickRoomCommand = new Command<ManagedRoom>(PickRoom),
                        }
                            .Bind(
                                ComposerAutocomplete.MembersProperty,
                                nameof(Members),
                                source: this
                            )
                            .Bind(ComposerAutocomplete.EmotesProperty, nameof(Emotes), source: this)
                            .Bind(ComposerAutocomplete.RoomsProperty, nameof(Rooms), source: this)
                            .Bind(
                                ComposerAutocomplete.QueryProperty,
                                nameof(AutocompleteQuery),
                                source: this
                            )
                            .Bind(
                                ComposerAutocomplete.ModeProperty,
                                nameof(AutocompleteMode),
                                source: this
                            )
                            .Bind(IsVisibleProperty, nameof(IsAutocompleteOpen), source: this),
                    },
                }.Row(3),
                _emojiPicker.Row(4),
            },
        };
    }

    private EmojiPicker CreateEmojiPicker() =>
        new EmojiPicker
        {
            Mode = EmojiPickerMode.Composer,
            HeightRequest = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 280 : 320,
            SelectedCommand = new Command<EmojiSelection>(selection =>
            {
                if (
                    selection?.Kind == EmojiKind.Unicode
                    && !string.IsNullOrEmpty(selection.Unicode)
                )
                    InsertAtCursor(selection.Unicode);
                else if (
                    selection?.Kind == EmojiKind.MatrixCustom
                    && !string.IsNullOrWhiteSpace(selection.Shortcode)
                )
                    InsertAtCursor($":{selection.Shortcode}:");
                IsEmojiPickerOpen = false;
            }),
        }
            .Bind(EmojiPicker.SessionProperty, nameof(Session), source: this)
            .Bind(EmojiPicker.IsOpenProperty, nameof(IsEmojiPickerOpen), source: this)
            .Bind(IsVisibleProperty, nameof(IsEmojiPickerOpen), source: this);

    private Entry CreateEntry()
    {
        _entry = new AttachmentEntry
        {
            Placeholder = "Message",
            ReturnType = ReturnType.Send,
            VerticalOptions = LayoutOptions.Center,
        };
        _entry.TextChanged += OnTextChanged;

        return ((AttachmentEntry)_entry)
            .Bind(Entry.TextProperty, nameof(Text), BindingMode.TwoWay, source: this)
            .Bind(Entry.ReturnCommandProperty, nameof(SendCommand), source: this)
            .Bind(
                AttachmentEntry.AttachmentCommandProperty,
                nameof(InlineAttachmentCommand),
                source: this
            );
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        PreviewHtml = Preview(args.NewTextValue ?? string.Empty);
        var position = Math.Clamp(
            _entry?.CursorPosition ?? 0,
            0,
            (args.NewTextValue ?? string.Empty).Length
        );
        var beforeCursor = (args.NewTextValue ?? string.Empty)[..position];
        var trigger = beforeCursor.LastIndexOfAny(['@', ':', '#']);

        if (trigger < 0 || (trigger > 0 && !char.IsWhiteSpace(beforeCursor[trigger - 1])))
        {
            CloseAutocomplete();
            return;
        }

        var query = beforeCursor[(trigger + 1)..];
        if (query.Any(char.IsWhiteSpace))
        {
            CloseAutocomplete();
            return;
        }

        _triggerPosition = trigger;
        AutocompleteMode =
            beforeCursor[trigger] == '@' ? ComposerAutocompleteMode.Mentions
            : beforeCursor[trigger] == ':' ? ComposerAutocompleteMode.Emotes
            : ComposerAutocompleteMode.Rooms;
        AutocompleteQuery = query;
        IsAutocompleteOpen = true;
    }

    private void PickMember(RoomMember? member) => Insert(member?.UserId);

    private void PickEmote(MatrixEmote? emote) => Insert(emote is null ? null : $":{emote.Name}:");

    private void PickRoom(ManagedRoom? room) =>
        Insert(room?.Id is { Length: > 0 } id ? $"https://matrix.to/#/{id}" : null);

    [RelayCommand]
    private void OpenEmojiPicker()
    {
        if (Session is null)
            return;
        _entry?.Unfocus();
        IsEmojiPickerOpen = !IsEmojiPickerOpen;
    }

    private void Insert(string? value)
    {
        if (value is null || _entry is null || _triggerPosition < 0)
        {
            return;
        }

        var text = _entry.Text ?? string.Empty;
        var cursor = Math.Clamp(_entry.CursorPosition, _triggerPosition, text.Length);
        Text = $"{text[.._triggerPosition]}{value}{text[cursor..]}";
        CloseAutocomplete();
        _entry.CursorPosition = _triggerPosition + value.Length;
    }

    private void InsertAtCursor(string value)
    {
        if (_entry is null)
            return;

        var text = _entry.Text ?? string.Empty;
        var cursor = Math.Clamp(_entry.CursorPosition, 0, text.Length);
        Text = $"{text[..cursor]}{value}{text[cursor..]}";
        _entry.CursorPosition = cursor + value.Length;
        _entry.Focus();
    }

    private void CloseAutocomplete()
    {
        IsAutocompleteOpen = false;
        AutocompleteMode = ComposerAutocompleteMode.None;
        AutocompleteQuery = string.Empty;
        _triggerPosition = -1;
    }

    private string Preview(string text)
    {
        if (
            string.IsNullOrWhiteSpace(text)
            || (
                !Bold.IsMatch(text)
                && !Italic.IsMatch(text)
                && !Emote.IsMatch(text)
                && !Mention.IsMatch(text)
            )
        )
            return string.Empty;

        var html = WebUtility.HtmlEncode(text).ReplaceLineEndings("<br />");
        html = Bold.Replace(html, "<strong>$1</strong>");
        html = Italic.Replace(html, "<em>$1</em>");
        html = Emote.Replace(
            html,
            match =>
                Emotes?.FirstOrDefault(emote =>
                    string.Equals(
                        emote.Name,
                        match.Groups[1].Value,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    is { } emote
                    ? $"<img data-mx-emoticon src=\"{WebUtility.HtmlEncode(emote.Source)}\" alt=\"{match.Value}\" />"
                    : match.Value
        );
        html = Mention.Replace(
            html,
            match =>
                Members?.FirstOrDefault(member =>
                    member.UserId == match.Value
                    || string.Equals(
                        member.DisplayName,
                        match.Value[1..],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    is { } member
                    ? $"<a href=\"https://matrix.to/#/{member.UserId}\">{match.Value}</a>"
                    : match.Value
        );
        return html;
    }

    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<!\*)\*(.+?)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex Emote = new(@":([\w+\-]+):", RegexOptions.Compiled);
    private static readonly Regex Mention = new(
        @"(?<![\w@])@[\w.=/\-]+(?::[\w.\-]+)?",
        RegexOptions.Compiled
    );
}
