using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatMessageView : ContentView
{
    [BindableProperty]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial ICommand? ReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? EditCommand { get; set; }

    [BindableProperty]
    public partial ICommand? LinkCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MenuCommand { get; set; }

    [BindableProperty]
    public partial ICommand? AddReactionCommand { get; set; }

    [BindableProperty]
    public partial ICommand? OpenMediaCommand { get; set; }

    [BindableProperty]
    public partial ICommand? OpenProfileCommand { get; set; }

    [BindableProperty]
    public partial ICommand? PollVoteCommand { get; set; }

    public ChatMessageView()
    {
        Content = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(36), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
            Children =
            {
                new MatrixAvatar
                {
                    Size = 36,
                    VerticalOptions = LayoutOptions.End,
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer()
                            .BindCommand(nameof(OpenProfileCommand), source: this)
                            .Bind(
                                TapGestureRecognizer.CommandParameterProperty,
                                $"{nameof(Item)}.{nameof(ChatTimelineItem.SenderId)}",
                                source: this
                            ),
                    },
                }
                    .Bind(
                        MatrixAvatar.MatrixSourceProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.SenderAvatarUrl)}",
                        source: this
                    )
                    .Bind(
                        MatrixAvatar.DisplayNameProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.Sender)}",
                        source: this
                    )
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.ShowAvatar)}",
                        source: this
                    )
                    .Column(0),
                new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions =
                            {
                                new ColumnDefinition(GridLength.Star),
                                new ColumnDefinition(GridLength.Auto),
                            },
                            Children =
                            {
                                new Label { FontAttributes = FontAttributes.Bold }
                                    .Bind(
                                        Label.TextProperty,
                                        $"{nameof(Item)}.{nameof(ChatTimelineItem.Sender)}",
                                        source: this
                                    )
                                    .Column(0),
                            },
                        }.Bind(
                            IsVisibleProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.ShowSender)}",
                            source: this
                        ),
                        new MessageBubbleView()
                            .Bind(MessageBubbleView.ItemProperty, nameof(Item), source: this)
                            .Bind(MessageBubbleView.ClientProperty, nameof(Client), source: this)
                            .Bind(MessageBubbleView.MembersProperty, nameof(Members), source: this)
                            .Bind(
                                MessageBubbleView.ReplyCommandProperty,
                                nameof(ReplyCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.EditCommandProperty,
                                nameof(EditCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.LinkCommandProperty,
                                nameof(LinkCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.MenuCommandProperty,
                                nameof(MenuCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.AddReactionCommandProperty,
                                nameof(AddReactionCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.OpenMediaCommandProperty,
                                nameof(OpenMediaCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.OpenProfileCommandProperty,
                                nameof(OpenProfileCommand),
                                source: this
                            )
                            .Bind(
                                MessageBubbleView.PollVoteCommandProperty,
                                nameof(PollVoteCommand),
                                source: this
                            ),
                    },
                }.Column(1),
            },
        }.Bind(
            MarginProperty,
            $"{nameof(Item)}.{nameof(ChatTimelineItem.IsGroupStart)}",
            converter: new MessageGroupMarginConverter(),
            source: this
        );
    }
}
