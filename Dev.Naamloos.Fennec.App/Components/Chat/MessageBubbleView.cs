using System.ComponentModel;
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
    private readonly Border _bubble;
    private readonly ContentView _attachmentHost = new() { IsVisible = false };
    private readonly SwipeItems _editItems;
    private readonly SwipeItems _emptyRightItems = new() { Mode = SwipeMode.Execute };
    private AttachmentKind _attachmentKind;

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
        _bubble = new Border
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
                    _attachmentHost,
                    new MessageReactionsView()
                        .Bind(MessageReactionsView.ItemProperty, nameof(Item), source: this)
                        .Bind(
                            MessageReactionsView.AddReactionCommandProperty,
                            nameof(AddReactionCommand),
                            source: this
                        ),
                    new HorizontalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.End }
                        .Bind(
                            BindableLayout.ItemsSourceProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.ReadReceipts)}",
                            source: this
                        )
                        .Invoke(layout =>
                            BindableLayout.SetItemTemplate(
                                layout,
                                new DataTemplate(() =>
                                    new MatrixAvatar { Size = 20 }
                                        .Bind(
                                            MatrixAvatar.MatrixSourceProperty,
                                            nameof(ChatReadReceipt.AvatarUrl)
                                        )
                                        .Bind(
                                            MatrixAvatar.DisplayNameProperty,
                                            nameof(ChatReadReceipt.Name)
                                        )
                                )
                            )
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
            Content = new Grid { Children = { _bubble } },
        };
        _swipeView.LeftItems.Add(CreateSwipeItem(nameof(ReplyCommand)));
        _editItems = new SwipeItems { Mode = SwipeMode.Execute };
        _editItems.Add(CreateSwipeItem(nameof(EditCommand)));

        Content = _swipeView;
    }

    private static void OnItemChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (MessageBubbleView)bindable;
        if (oldValue is ChatTimelineItem oldItem)
        {
            oldItem.PropertyChanged -= view.OnItemPropertyChanged;
        }
        if (newValue is ChatTimelineItem newItem)
        {
            newItem.PropertyChanged += view.OnItemPropertyChanged;
        }

        view.UpdateSwipeActions();
        view.UpdateAttachment();
        view.UpdateBubbleColor();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(ChatTimelineItem.Media)
                or nameof(ChatTimelineItem.Poll)
        )
        {
            UpdateAttachment();
        }
        else if (eventArgs.PropertyName == nameof(ChatTimelineItem.IsOwn))
        {
            UpdateSwipeActions();
            UpdateBubbleColor();
        }
    }

    private void UpdateBubbleColor() =>
        _bubble.SetDynamicResource(
            BackgroundColorProperty,
            Item?.IsOwn == true ? "PrimaryContainer" : "SurfaceContainer"
        );

    private void UpdateAttachment()
    {
        var kind = Item switch
        {
            { Poll: not null } => AttachmentKind.Poll,
            { Media.Kind: ChatMediaKind.Audio } => AttachmentKind.Audio,
            { Media: not null } => AttachmentKind.Media,
            _ => AttachmentKind.None,
        };
        if (kind == _attachmentKind)
        {
            return;
        }

        _attachmentKind = kind;
        _attachmentHost.Content = kind switch
        {
            AttachmentKind.Poll => new PollMessageView()
                .Bind(PollMessageView.ItemProperty, nameof(Item), source: this)
                .Bind(PollMessageView.VoteCommandProperty, nameof(PollVoteCommand), source: this),
            AttachmentKind.Audio => new InlineAudioPlayer()
                .Bind(
                    InlineAudioPlayer.MediaProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.Media)}",
                    source: this
                )
                .Bind(InlineAudioPlayer.ClientProperty, nameof(Client), source: this),
            AttachmentKind.Media => new MatrixMedia()
                .Bind(
                    MatrixMedia.MediaProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.Media)}",
                    source: this
                )
                .Bind(MatrixMedia.ClientProperty, nameof(Client), source: this)
                .Bind(MatrixMedia.OpenCommandProperty, nameof(OpenMediaCommand), source: this),
            _ => null,
        };
        _attachmentHost.IsVisible = kind != AttachmentKind.None;
    }

    private void UpdateSwipeActions()
    {
        var items = Item?.IsOwn == true ? _editItems : _emptyRightItems;
        if (!ReferenceEquals(_swipeView.RightItems, items))
        {
            _swipeView.RightItems = items;
        }
    }

    private SwipeItem CreateSwipeItem(string command) =>
        new SwipeItem { Text = string.Empty, BackgroundColor = Colors.Transparent }
            .BindCommand(command, source: this)
            .Bind(SwipeItem.CommandParameterProperty, nameof(Item), source: this);

    private enum AttachmentKind
    {
        None,
        Media,
        Audio,
        Poll,
    }
}
