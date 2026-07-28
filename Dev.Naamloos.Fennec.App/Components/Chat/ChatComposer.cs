using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk.Entities;
using MauiIcons.Core;
using MauiIcons.Material;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatComposer : ContentView
{
    private Entry? _entry;
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
                new RowDefinition(GridLength.Auto)
            },
            Children =
            {
                new Label
                {
                    TextColor = Colors.Red,
                    Margin = new Thickness(12, 4),
                    LineBreakMode = LineBreakMode.WordWrap
                }
                .Bind(
                    Label.TextProperty,
                    nameof(ErrorMessage),
                    source: this
                )
                .Bind(
                    IsVisibleProperty,
                    nameof(HasError),
                    source: this
                )
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
                        new Label
                        {
                            FontSize = 12,
                            Opacity = .75,
                        }
                        .Bind(Label.TextProperty, $"{nameof(ReplyTo)}.{nameof(ChatTimelineItem.Sender)}",
                            stringFormat: "Replying to {0}", source: this)
                        .Column(0),

                        new MauiIcon
                        {
                            Icon = MaterialIcons.Close,
                            IconSize = 20,
                            IconColor = Colors.Red,
                            WidthRequest = 32,
                            HeightRequest = 32,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer()
                                    .BindCommand(nameof(CancelReplyCommand), source: this),
                            },
                        }
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
                        new Label
                        {
                            FontSize = 12,
                            Opacity = .75,
                        }
                        .Bind(Label.TextProperty, $"{nameof(EditTarget)}.{nameof(ChatTimelineItem.Sender)}",
                            stringFormat: "Editing {0}", source: this)
                        .Column(0),

                        new MauiIcon
                        {
                            Icon = MaterialIcons.Close,
                            IconSize = 20,
                            IconColor = Colors.Red,
                            WidthRequest = 32,
                            HeightRequest = 32,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer()
                                    .BindCommand(nameof(CancelEditCommand), source: this),
                            },
                        }
                        .Column(1),
                    },
                }
                .Bind(IsVisibleProperty, $"{nameof(EditTarget)}",
                    converter: new NotNullConverter(), source: this)
                .Row(1),

                new MatrixHtmlView
                {
                    Margin = new Thickness(12, 0, 12, 4),
                    Opacity = .72,
                    MaximumHeightRequest = 84,
                }
                .Bind(MatrixHtmlView.HtmlProperty, nameof(PreviewHtml), source: this)
                .Bind(IsVisibleProperty, nameof(Text),
                    converter: new IsStringNotNullOrEmptyConverter(), source: this)
                .Row(2),

                new Grid
                {
                    Padding = 12,
                    Children =
                    {
                        new Grid
                        {
                            ColumnSpacing = 8,
                            ColumnDefinitions =
                            {
                                    new ColumnDefinition(GridLength.Auto),
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Auto),
                                    new ColumnDefinition(GridLength.Auto)
                            },
                            Children =
                            {
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.AttachFile,
                                    IconSize = 24,
                                    WidthRequest = 40,
                                    HeightRequest = 40,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer()
                                            .BindCommand(nameof(AttachCommand), source: this),
                                    },
                                }
                                .Column(0),
                                CreateEntry().Column(1),
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.Add,
                                    IconSize = 24,
                                    WidthRequest = 40,
                                    HeightRequest = 40,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer()
                                            .BindCommand(nameof(MoreCommand), source: this),
                                    },
                                }
                                .Column(2),
                                new MauiIcon
                                {
                                    Icon = MaterialIcons.Send,
                                    IconSize = 24,
                                    WidthRequest = 40,
                                    HeightRequest = 40,
                                    VerticalOptions = LayoutOptions.Center,
                                    GestureRecognizers =
                                    {
                                        new TapGestureRecognizer()
                                            .BindCommand(nameof(SendCommand), source: this),
                                    },
                                }
                                .Column(3)
                            }
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
                        .Bind(ComposerAutocomplete.MembersProperty, nameof(Members), source: this)
                        .Bind(ComposerAutocomplete.EmotesProperty, nameof(Emotes), source: this)
                        .Bind(ComposerAutocomplete.RoomsProperty, nameof(Rooms), source: this)
                        .Bind(ComposerAutocomplete.QueryProperty, nameof(AutocompleteQuery), source: this)
                        .Bind(ComposerAutocomplete.ModeProperty, nameof(AutocompleteMode), source: this)
                        .Bind(IsVisibleProperty, nameof(IsAutocompleteOpen), source: this),
                    }
                }.Row(3)
            }
        };
    }

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
            .Bind(AttachmentEntry.AttachmentCommandProperty,
                nameof(InlineAttachmentCommand), source: this);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        PreviewHtml = Preview(args.NewTextValue ?? string.Empty);
        var position = Math.Clamp(_entry?.CursorPosition ?? 0, 0, (args.NewTextValue ?? string.Empty).Length);
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
        AutocompleteMode = beforeCursor[trigger] == '@'
            ? ComposerAutocompleteMode.Mentions
            : beforeCursor[trigger] == ':'
                ? ComposerAutocompleteMode.Emotes
                : ComposerAutocompleteMode.Rooms;
        AutocompleteQuery = query;
        IsAutocompleteOpen = true;
    }

    private void PickMember(RoomMember? member) => Insert(member?.UserId);

    private void PickEmote(MatrixEmote? emote) => Insert(emote is null ? null : $":{emote.Name}:");

    private void PickRoom(ManagedRoom? room) => Insert(room?.Id is { Length: > 0 } id
        ? $"https://matrix.to/#/{id}"
        : null);

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

    private void CloseAutocomplete()
    {
        IsAutocompleteOpen = false;
        AutocompleteMode = ComposerAutocompleteMode.None;
        AutocompleteQuery = string.Empty;
        _triggerPosition = -1;
    }

    private string Preview(string text)
    {
        var html = WebUtility.HtmlEncode(text).ReplaceLineEndings("<br />");
        html = Bold.Replace(html, "<strong>$1</strong>");
        html = Italic.Replace(html, "<em>$1</em>");
        html = Emote.Replace(html, match => Emotes?.FirstOrDefault(emote =>
            string.Equals(emote.Name, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) is { } emote
                ? $"<img data-mx-emoticon src=\"{WebUtility.HtmlEncode(emote.Source)}\" alt=\"{match.Value}\" />"
                : match.Value);
        html = Mention.Replace(html, match => Members?.FirstOrDefault(member =>
            member.UserId == match.Value ||
            string.Equals(member.DisplayName, match.Value[1..], StringComparison.OrdinalIgnoreCase)) is { } member
                ? $"<a href=\"https://matrix.to/#/{member.UserId}\">{match.Value}</a>"
                : match.Value);
        return html;
    }

    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<!\*)\*(.+?)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex Emote = new(@":([\w+\-]+):", RegexOptions.Compiled);
    private static readonly Regex Mention = new(
        @"(?<![\w@])@[\w.=/\-]+(?::[\w.\-]+)?", RegexOptions.Compiled);
}
