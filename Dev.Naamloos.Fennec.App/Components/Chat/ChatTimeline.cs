using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimeline : ContentView
{
    private readonly CollectionView _collectionView;
    private INotifyCollectionChanged? _itemsSource;
    private bool _initialScrollPending = true;
    private bool _scrollQueued;

    [BindableProperty(PropertyChangedMethodName = nameof(OnItemsChanged))]
    public partial ObservableCollection<ChatTimelineItem>? Items { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial IEnumerable<RoomMember>? Members { get; set; }

    [BindableProperty]
    public partial ICommand? HistoryCommand { get; set; }

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

    [BindableProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial bool IsNearBottom { get; set; } = true;

    [BindableProperty(PropertyChangedMethodName = nameof(OnLoadMoreVisibilityChanged))]
    public partial bool ShowLoadMore { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnLoadMoreVisibilityChanged))]
    public partial bool HasMoreHistory { get; set; }

    public bool IsLoadMoreVisible => ShowLoadMore && HasMoreHistory;

    public ChatTimeline()
    {
        _collectionView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() => new ChatTimelineItemView()
                .Bind(ChatTimelineItemView.ItemProperty, ".")
                .Bind(ChatTimelineItemView.ClientProperty, nameof(Client), source: this)
                .Bind(ChatTimelineItemView.MembersProperty, nameof(Members), source: this)
                .Bind(ChatTimelineItemView.ReplyCommandProperty, nameof(ReplyCommand), source: this)
                .Bind(ChatTimelineItemView.EditCommandProperty, nameof(EditCommand), source: this)
                .Bind(ChatTimelineItemView.LinkCommandProperty, nameof(LinkCommand), source: this)
                .Bind(ChatTimelineItemView.MenuCommandProperty, nameof(MenuCommand), source: this)
                .Bind(ChatTimelineItemView.AddReactionCommandProperty, nameof(AddReactionCommand), source: this)
                .Bind(ChatTimelineItemView.OpenMediaCommandProperty, nameof(OpenMediaCommand), source: this)),
            EmptyView = new Grid
            {
                Children =
                {
                    new Label
                    {
                        Text = "No messages yet",
                        Opacity = .7,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            },
            Behaviors =
            {
                new CommunityToolkit.Maui.Behaviors.EventToCommandBehavior
                {
                    EventName = nameof(CollectionView.Scrolled),
                }
                .Bind(CommunityToolkit.Maui.Behaviors.EventToCommandBehavior.CommandProperty,
                    nameof(ScrolledCommand), source: this),
            },
        }
        .Bind(ItemsView.ItemsSourceProperty, nameof(Items), source: this);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children =
            {
                new Button
                {
                    Text = "Load older messages",
                    Margin = new Thickness(12, 8),
                    HorizontalOptions = LayoutOptions.Center,
                }
                .Bind(Button.CommandProperty, nameof(HistoryCommand), source: this)
                .Bind(IsVisibleProperty, nameof(IsLoadMoreVisible), source: this)
                .Row(0),
                _collectionView.Row(1),
            },
        };
        Loaded += (_, _) => QueueScrollToLatest();
    }

    [RelayCommand]
    private void Scrolled(ItemsViewScrolledEventArgs? eventArgs)
    {
        if (eventArgs is null || Items is not { Count: > 0 })
        {
            return;
        }

        IsNearBottom = eventArgs.LastVisibleItemIndex >= Items.Count - 1;
        ShowLoadMore = eventArgs.FirstVisibleItemIndex <= 1 ||
            eventArgs.VerticalOffset <= 24;

        if (ShowLoadMore && HistoryCommand?.CanExecute(null) == true)
        {
            HistoryCommand.Execute(null);
        }
    }

    private static void OnItemsChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) => ((ChatTimeline)bindable).SetItemsSource();

    private static void OnLoadMoreVisibilityChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) => ((ChatTimeline)bindable).OnPropertyChanged(
            nameof(IsLoadMoreVisible));

    private void SetItemsSource()
    {
        if (_itemsSource is not null)
        {
            _itemsSource.CollectionChanged -= OnItemsCollectionChanged;
        }

        _itemsSource = Items;
        if (_itemsSource is not null)
        {
            _itemsSource.CollectionChanged += OnItemsCollectionChanged;
        }

        _initialScrollPending = true;
        QueueScrollToLatest();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_initialScrollPending ||
            (IsNearBottom && args.Action == NotifyCollectionChangedAction.Add &&
             args.NewStartingIndex >= Items!.Count - (args.NewItems?.Count ?? 0)))
        {
            QueueScrollToLatest();
        }
    }

    private void QueueScrollToLatest()
    {
        if (_scrollQueued || Items is not { Count: > 0 })
        {
            return;
        }

        _scrollQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _scrollQueued = false;
            if (Items is not { Count: > 0 })
            {
                return;
            }

            _collectionView.ScrollTo(Items.Count - 1, ScrollToPosition.End, animate: false);
            _initialScrollPending = false;
        });
    }
}
