using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MessageBubbleView : ContentView
{
    private readonly SwipeView _swipeView;

    [BindableProperty(PropertyChangedMethodName = nameof(OnItemChanged))]
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

    public MessageBubbleView()
    {
        var bubble = new Border
        {
            MaximumWidthRequest = 520,
            Padding = new Thickness(8, 6),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            GestureRecognizers =
            {
                new TapGestureRecognizer { Buttons = ButtonsMask.Secondary }
                    .BindCommand(nameof(MenuCommand), source: this)
                    .Bind(
                        TapGestureRecognizer.CommandParameterProperty,
                        nameof(Item),
                        source: this
                    ),
            },
            Behaviors =
            {
                new TouchBehavior
                {
                    LongPressDuration = 500,
                    ShouldMakeChildrenInputTransparent = false,
                }
                    .Bind(TouchBehavior.LongPressCommandProperty, nameof(MenuCommand), source: this)
                    .Bind(
                        TouchBehavior.LongPressCommandParameterProperty,
                        nameof(Item),
                        source: this
                    ),
            },
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new TextMessageView()
                        .Bind(TextMessageView.ItemProperty, nameof(Item), source: this)
                        .Bind(TextMessageView.ClientProperty, nameof(Client), source: this)
                        .Bind(TextMessageView.MembersProperty, nameof(Members), source: this)
                        .Bind(
                            TextMessageView.LinkCommandProperty,
                            nameof(LinkCommand),
                            source: this
                        ),
                    new MatrixMedia()
                        .Bind(
                            MatrixMedia.MediaProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.Media)}",
                            source: this
                        )
                        .Bind(MatrixMedia.ClientProperty, nameof(Client), source: this)
                        .Bind(
                            MatrixMedia.OpenCommandProperty,
                            nameof(OpenMediaCommand),
                            source: this
                        ),
                    new InlineAudioPlayer()
                        .Bind(
                            InlineAudioPlayer.MediaProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.Media)}",
                            source: this
                        )
                        .Bind(InlineAudioPlayer.ClientProperty, nameof(Client), source: this),
                    new PollMessageView()
                        .Bind(PollMessageView.ItemProperty, nameof(Item), source: this)
                        .Bind(
                            PollMessageView.VoteCommandProperty,
                            nameof(PollVoteCommand),
                            source: this
                        ),
                    new MessageReactionsView()
                        .Bind(MessageReactionsView.ItemProperty, nameof(Item), source: this)
                        .Bind(
                            MessageReactionsView.AddReactionCommandProperty,
                            nameof(AddReactionCommand),
                            source: this
                        ),
                    new CollectionView
                    {
                        HeightRequest = 22,
                        HorizontalOptions = LayoutOptions.End,
                        SelectionMode = SelectionMode.None,
                        ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
                        {
                            ItemSpacing = 2,
                        },
                        ItemTemplate = new DataTemplate(() =>
                            new MatrixAvatar { Size = 20 }
                                .Bind(
                                    MatrixAvatar.MatrixSourceProperty,
                                    nameof(ChatReadReceipt.AvatarUrl)
                                )
                                .Bind(
                                    MatrixAvatar.DisplayNameProperty,
                                    nameof(ChatReadReceipt.Name)
                                )
                        ),
                    }
                        .Bind(
                            ItemsView.ItemsSourceProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.ReadReceipts)}",
                            source: this
                        )
                        .Bind(
                            IsVisibleProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.ReadReceipts)}",
                            converter: new CollectionNotEmptyConverter(),
                            source: this
                        ),
                },
            },
        }
            .Bind(
                HorizontalOptionsProperty,
                $"{nameof(Item)}.{nameof(ChatTimelineItem.IsOwn)}",
                converter: new BooleanToHorizontalOptionsConverter(),
                source: this
            )
            .DynamicResource(BackgroundColorProperty, "SurfaceContainer");

        _swipeView = new SwipeView
        {
            Threshold = 48,
            LeftItems = new SwipeItems { Mode = SwipeMode.Execute },
            Content = new Grid { Children = { bubble } },
        };
        _swipeView.LeftItems.Add(CreateSwipeItem(nameof(ReplyCommand)));

        Content = _swipeView;
    }

    private static void OnItemChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((MessageBubbleView)bindable).UpdateSwipeActions();

    private void UpdateSwipeActions()
    {
        var items = new SwipeItems { Mode = SwipeMode.Execute };
        if (Item?.IsOwn == true)
        {
            items.Add(CreateSwipeItem(nameof(EditCommand)));
        }

        _swipeView.RightItems = items;
    }

    private SwipeItem CreateSwipeItem(string command) =>
        new SwipeItem { Text = string.Empty, BackgroundColor = Colors.Transparent }
            .BindCommand(command, source: this)
            .Bind(SwipeItem.CommandParameterProperty, nameof(Item), source: this);
}
