using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Pages;
using Dev.Naamloos.Fennec.Sdk;
using System.Collections.ObjectModel;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimeline : ContentView
{
    private const int BottomFollowThreshold = 2;
    private const int HistoryLoadThreshold = 5;
    private readonly Dictionary<string, double> _messageHeights = [];
    private bool _ignoreScrollEvents;
    private bool _scrollQueued;

    [BindableProperty]
    public partial ObservableCollection<ChatMessage>? Messages { get; set; }

    [BindableProperty]
    public partial ICommand? HistoryCommand { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial Room? Room { get; set; }

    [BindableProperty]
    public partial ICommand? ReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MessageMenuCommand { get; set; }

    [BindableProperty]
    public partial ICommand? SaveMediaCommand { get; set; }

    [BindableProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial bool IsNearBottom { get; set; } = true;

    [BindableProperty(PropertyChangedMethodName = nameof(OnResetRequestChanged))]
    public partial int ResetRequest { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnScrollRequestChanged))]
    public partial int ScrollRequest { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnScrollAfterLayoutRequestChanged))]
    public partial int ScrollAfterLayoutRequest { get; set; }

    public ChatTimeline()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Spacing = 8,
                Children =
                {
                    new Label
                    {
                        Text = "No messages yet",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                    },
                    new Label
                    {
                        Text = "Send the first message.",
                        Opacity = .7,
                        HorizontalTextAlignment = TextAlignment.Center,
                    },
                },
            },
            ItemsUpdatingScrollMode =
                ItemsUpdatingScrollMode.KeepLastItemInView,
            ItemsLayout = new LinearItemsLayout(
                ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 4,
            },
            Header = new BoxView
            {
                BackgroundColor = Colors.Transparent,
            },
            ItemTemplate = new DataTemplate(() =>
                new ContentView
                {
                    Content = new ChatMessageView()
                        .Bind(
                            ChatMessageView.MessageProperty,
                            nameof(BindingContext),
                            source: new RelativeBindingSource(
                                RelativeBindingSourceMode.FindAncestor,
                                typeof(ContentView)))
                        .Bind(
                            ChatMessageView.MatrixClientProperty,
                            nameof(MatrixClient),
                            source: BindingContext)
                        .Bind(
                            ChatMessageView.RoomProperty,
                            nameof(Room),
                            source: BindingContext)
                        .Bind(
                            ChatMessageView.ReplyCommandProperty,
                            nameof(ReplyCommand),
                            source: BindingContext)
                        .Bind(
                            ChatMessageView.MessageMenuCommandProperty,
                            nameof(MessageMenuCommand),
                            source: BindingContext)
                        .Bind(
                            ChatMessageView.SaveMediaCommandProperty,
                            nameof(SaveMediaCommand),
                            source: BindingContext)
                        .Bind(
                            ChatMessageView.MessageMeasuredCommandProperty,
                            nameof(UpdateMessageHeightCommand),
                            source: BindingContext),
                }),
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(CollectionView.Scrolled),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(ScrolledCommand)),
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(SizeChanged),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(TimelineSizeChangedCommand)),
            },
        }.Bind(
            ItemsView.ItemsSourceProperty,
            nameof(Messages));
    }

    private void Reset()
    {
        IsNearBottom = true;
        _ignoreScrollEvents = true;
        _messageHeights.Clear();
        UpdateBottomSpacer();
    }

    [RelayCommand]
    private void UpdateMessageHeight(
        ChatMessageMeasurement measurement)
    {
        _messageHeights[measurement.Message.Id] =
            measurement.Height;
        UpdateBottomSpacer();
    }

    private async Task ScrollToBottomAsync()
    {
        if (Messages is not { Count: > 0 })
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(
            QueueScrollToBottom);
    }

    private async Task ScrollToBottomAfterLayoutAsync()
    {
        if (Messages is not { Count: > 0 })
        {
            return;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Messages is not { Count: > 0 })
            {
                completion.TrySetResult(true);
                return;
            }

            Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(50),
                () =>
                {
                    try
                    {
                        if (Messages is { Count: > 0 })
                        {
                            QueueScrollToBottom();
                        }

                        completion.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                });
        });

        await completion.Task;
    }

    [RelayCommand]
    private void Scrolled(ItemsViewScrolledEventArgs e)
    {
        if (_ignoreScrollEvents ||
            Messages is not { Count: > 0 })
        {
            return;
        }

        IsNearBottom =
            e.LastVisibleItemIndex >=
            Messages.Count - 1 - BottomFollowThreshold;

        ((CollectionView)Content).ItemsUpdatingScrollMode =
            IsNearBottom
                ? ItemsUpdatingScrollMode.KeepLastItemInView
                : ItemsUpdatingScrollMode.KeepScrollOffset;

        if (e.FirstVisibleItemIndex <= HistoryLoadThreshold &&
            HistoryCommand?.CanExecute(null) == true)
        {
            HistoryCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void TimelineSizeChanged()
    {
        UpdateBottomSpacer();

        if (!IsNearBottom ||
            Messages is not { Count: > 0 })
        {
            return;
        }

        Dispatcher.Dispatch(() =>
        {
            if (IsNearBottom &&
                Messages is { Count: > 0 })
            {
                _ = ScrollToBottomAsync();
            }
        });
    }

    private void QueueScrollToBottom()
    {
        if (_scrollQueued)
        {
            return;
        }

        _scrollQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _scrollQueued = false;
            ScrollToBottomCore();
        });
    }

    private void ScrollToBottomCore()
    {
        if (Messages is not { Count: > 0 })
        {
            return;
        }

        _ignoreScrollEvents = true;
        IsNearBottom = true;
        ((CollectionView)Content).ItemsUpdatingScrollMode =
            ItemsUpdatingScrollMode.KeepLastItemInView;
        ((CollectionView)Content).ScrollTo(
            Messages.Count - 1,
            groupIndex: -1,
            position: ScrollToPosition.End,
            animate: false);
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(75),
            () =>
            {
                _ignoreScrollEvents = false;
                IsNearBottom = true;
            });
    }

    private void UpdateBottomSpacer()
    {
        if (Messages is not { Count: > 0 and <= 12 } ||
            Content.Height <= 0)
        {
            ((BoxView)((CollectionView)Content).Header)
                .HeightRequest = 0;
            return;
        }

        var contentHeight = Messages.Sum(
            message => _messageHeights.TryGetValue(
                message.Id,
                out var height)
                ? height
                : 68d);

        ((BoxView)((CollectionView)Content).Header)
            .HeightRequest = Math.Max(
                0,
                Content.Height - contentHeight);
    }

    private static void OnResetRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((ChatTimeline)bindable).Reset();

    private static void OnScrollRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((ChatTimeline)bindable)
            .ScrollToBottomAsync();

    private static void OnScrollAfterLayoutRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((ChatTimeline)bindable)
            .ScrollToBottomAfterLayoutAsync();
}
