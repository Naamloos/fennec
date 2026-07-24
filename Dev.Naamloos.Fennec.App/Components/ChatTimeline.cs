using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.App.Pages;
using Dev.Naamloos.Fennec.Sdk;
using System.Collections;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimeline : ContentView, IDisposable
{
    private const int BottomFollowThreshold = 2;
    private const int HistoryLoadThreshold = 5;

    private readonly CollectionView _collectionView;

    private bool _ignoreScrollEvents;
    private bool _initialPositionApplied;
    private bool _scrollQueued;
    private bool _historyRequestRaised;
    private bool _disposed;

    [BindableProperty]
    public partial IList? Messages { get; set; }

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

    [BindableProperty(
        PropertyChangedMethodName = nameof(OnResetRequestChanged))]
    public partial int ResetRequest { get; set; }

    [BindableProperty(
        PropertyChangedMethodName = nameof(OnScrollRequestChanged))]
    public partial int ScrollRequest { get; set; }

    [BindableProperty(
        PropertyChangedMethodName =
            nameof(OnScrollAfterLayoutRequestChanged))]
    public partial int ScrollAfterLayoutRequest { get; set; }

    public ChatTimeline()
    {
        _collectionView = CreateCollectionView();

        _collectionView.Scrolled +=
            OnCollectionViewScrolled;

        _collectionView.SizeChanged +=
            OnCollectionViewSizeChanged;

        Content = _collectionView;

        InitializePlatformCollectionView();
    }

    partial void InitializePlatformCollectionView();

    partial void DisposePlatformCollectionView();

    private CollectionView CreateCollectionView()
    {
        var itemTemplate =
            new DataTemplate(() =>
                new ChatMessageView()
                    .Bind(
                        ChatMessageView.MessageProperty,
                        ".")
                    .Bind(
                        ChatMessageView.MatrixClientProperty,
                        nameof(MatrixClient),
                        source: this)
                    .Bind(
                        ChatMessageView.RoomProperty,
                        nameof(Room),
                        source: this)
                    .Bind(
                        ChatMessageView.ReplyCommandProperty,
                        nameof(ReplyCommand),
                        source: this)
                    .Bind(
                        ChatMessageView.MessageMenuCommandProperty,
                        nameof(MessageMenuCommand),
                        source: this)
                    .Bind(
                        ChatMessageView.SaveMediaCommandProperty,
                        nameof(SaveMediaCommand),
                        source: this));

        return new CollectionView
        {
            SelectionMode = SelectionMode.None,

            /*
             * Message rows have uneven heights due to wrapping and media.
             * CollectionView still virtualizes the rows natively.
             */
            ItemSizingStrategy =
                ItemSizingStrategy.MeasureAllItems,

            ItemsUpdatingScrollMode =
                ItemsUpdatingScrollMode.KeepLastItemInView,

            ItemsLayout =
                new LinearItemsLayout(
                    ItemsLayoutOrientation.Vertical)
                {
                    ItemSpacing = 0,
                },

            ItemTemplate = itemTemplate,

            EmptyView = new Grid
            {
                Children =
                {
                    new Label
                    {
                        Text = "No messages yet",
                        Opacity = .7,
                        HorizontalOptions =
                            LayoutOptions.Center,
                        VerticalOptions =
                            LayoutOptions.Center,
                    },
                },
            },
        }
        .Bind(
            ItemsView.ItemsSourceProperty,
            nameof(Messages),
            source: this);
    }

    private void OnCollectionViewScrolled(
        object? sender,
        ItemsViewScrolledEventArgs eventArgs)
    {
        if (_disposed ||
            _ignoreScrollEvents ||
            !_initialPositionApplied ||
            Messages is not { Count: > 0 } messages)
        {
            return;
        }

        IsNearBottom =
            eventArgs.LastVisibleItemIndex >=
            messages.Count -
            1 -
            BottomFollowThreshold;

        _collectionView.ItemsUpdatingScrollMode =
            IsNearBottom
                ? ItemsUpdatingScrollMode.KeepLastItemInView
                : ItemsUpdatingScrollMode.KeepScrollOffset;

        if (eventArgs.FirstVisibleItemIndex >
            HistoryLoadThreshold)
        {
            _historyRequestRaised = false;
            return;
        }

        if (_historyRequestRaised ||
            HistoryCommand?.CanExecute(null) != true)
        {
            return;
        }

        _historyRequestRaised = true;
        HistoryCommand.Execute(null);
    }

    private void OnCollectionViewSizeChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (_disposed ||
            Messages is not { Count: > 0 })
        {
            return;
        }

        if (!_initialPositionApplied)
        {
            _ = ApplyInitialBottomPositionAsync();
            return;
        }

        if (IsNearBottom)
        {
            QueueScrollToBottom();
        }
    }

    private void Reset()
    {
        _initialPositionApplied = false;
        _historyRequestRaised = false;
        IsNearBottom = true;

        _collectionView.ItemsUpdatingScrollMode =
            ItemsUpdatingScrollMode.KeepLastItemInView;
    }

    private async Task ApplyInitialBottomPositionAsync()
    {
        if (_disposed ||
            Messages is not { Count: > 0 } messages)
        {
            return;
        }

        /*
         * Wait two dispatcher turns so ItemsSource and the native layout have
         * both received the projected message collection.
         */
        await WaitForLayoutAsync();

        if (_disposed ||
            Messages is not { Count: > 0 } current ||
            !ReferenceEquals(messages, current))
        {
            return;
        }

        ScrollToBottomCore();
        _initialPositionApplied = true;
    }

    private Task WaitForLayoutAsync()
    {
        var completion =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        Dispatcher.Dispatch(() =>
        {
            Dispatcher.Dispatch(() =>
            {
                completion.TrySetResult(true);
            });
        });

        return completion.Task;
    }

    private void QueueScrollToBottom()
    {
        if (_disposed || _scrollQueued)
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
        if (_disposed ||
            Messages is not { Count: > 0 } messages)
        {
            return;
        }

        _ignoreScrollEvents = true;
        IsNearBottom = true;

        _collectionView.ItemsUpdatingScrollMode =
            ItemsUpdatingScrollMode.KeepLastItemInView;

        _collectionView.ScrollTo(
            messages.Count - 1,
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

    private static void OnResetRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((ChatTimeline)bindable).Reset();
    }

    private static void OnScrollRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((ChatTimeline)bindable)
            .QueueScrollToBottom();
    }

    private static void OnScrollAfterLayoutRequestChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = ((ChatTimeline)bindable)
            .ApplyInitialBottomPositionAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _collectionView.Scrolled -=
            OnCollectionViewScrolled;

        _collectionView.SizeChanged -=
            OnCollectionViewSizeChanged;

        DisposePlatformCollectionView();

        GC.SuppressFinalize(this);
    }
}
