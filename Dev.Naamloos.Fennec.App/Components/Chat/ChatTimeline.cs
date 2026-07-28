using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
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
    private bool _followingLatest = true;
    private bool _scrollQueued;
    private int _firstVisibleItemIndex;
    private ChatTimelineItem? _historyAnchor;

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

    [BindableProperty]
    public partial ICommand? OpenProfileCommand { get; set; }

    [BindableProperty]
    public partial ICommand? PollVoteCommand { get; set; }

    [BindableProperty(DefaultBindingMode = BindingMode.TwoWay,
        PropertyChangedMethodName = nameof(OnIsNearBottomChanged))]
    public partial bool IsNearBottom { get; set; } = true;

    [BindableProperty(PropertyChangedMethodName = nameof(OnIsLoadingHistoryChanged))]
    public partial bool IsLoadingHistory { get; set; }

    [BindableProperty]
    public partial bool HasMoreHistory { get; set; }

    public bool IsScrollToBottomVisible => !IsNearBottom && Items is { Count: > 0 };

    public ChatTimeline()
    {
        _collectionView = new CollectionView
        {
            BackgroundColor = Colors.Transparent,
            SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset,
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new ChatTimelineTemplateSelector(this),
            Header = new TemplateSwitchView<bool, bool>(value => value)
            {
                Padding = new Thickness(12, 8),
                FallbackTemplate = new Label
                {
                    Text = "Beginning of chat",
                    Opacity = .6,
                    HorizontalOptions = LayoutOptions.Center,
                },
            }
            .Add(value => value, CreateLoadMoreButton())
            .Bind(TemplateSwitchView<bool, bool>.ValueProperty,
                nameof(HasMoreHistory), source: this),
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
        }
        .Bind(ItemsView.ItemsSourceProperty, nameof(Items), source: this);
        _collectionView.Scrolled += (_, eventArgs) => OnScrolled(eventArgs);
        _collectionView.SizeChanged += (_, _) =>
        {
            if (_followingLatest)
            {
                QueueScrollToLatest();
            }
        };

        Content = new Grid
        {
            BackgroundColor = Colors.Transparent,
            Children =
            {
                _collectionView,
                new ActivityIndicator
                {
                    Margin = new Thickness(12),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Start,
                }
                .Bind(ActivityIndicator.IsRunningProperty, nameof(IsLoadingHistory), source: this)
                .Bind(IsVisibleProperty, nameof(IsLoadingHistory), source: this),
                new Button
                {
                    Text = "↓ Latest",
                    Margin = new Thickness(12),
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.End,
                }
                .Bind(IsVisibleProperty, nameof(IsScrollToBottomVisible), source: this)
                .Invoke(button => button.Clicked += (_, _) =>
                {
                    _followingLatest = true;
                    QueueScrollToLatest();
                }),
            },
        };
        Loaded += (_, _) => QueueScrollToLatest();
    }

    private void OnScrolled(ItemsViewScrolledEventArgs? eventArgs)
    {
        if (eventArgs is null || Items is not { Count: > 0 })
        {
            return;
        }

        IsNearBottom = eventArgs.LastVisibleItemIndex >= Items.Count - 1;
        _firstVisibleItemIndex = Math.Max(0, eventArgs.FirstVisibleItemIndex);
        if (_initialScrollPending && IsNearBottom)
        {
            _initialScrollPending = false;
        }

        if (!_initialScrollPending && _followingLatest &&
            eventArgs.VerticalDelta < 0 && !IsNearBottom)
        {
            _followingLatest = false;
        }

    }

    private static void OnItemsChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) => ((ChatTimeline)bindable).SetItemsSource();

    private static void OnIsNearBottomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) => ((ChatTimeline)bindable).OnPropertyChanged(
            nameof(IsScrollToBottomVisible));

    private static void OnIsLoadingHistoryChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (newValue is false)
        {
            ((ChatTimeline)bindable).RestoreHistoryAnchor();
        }
    }

    private View CreateLoadMoreButton()
    {
        var button = new Button
        {
            Text = string.Empty,
            MinimumWidthRequest = 120,
        }
        .Bind(IsEnabledProperty, nameof(IsLoadingHistory),
            converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
            source: this)
        .Invoke(view => view.Clicked += (_, _) =>
        {
            if (IsLoadingHistory || HistoryCommand?.CanExecute(null) != true)
            {
                return;
            }

            _followingLatest = false;
            _historyAnchor = Items is { Count: > 0 }
                ? Items[Math.Min(_firstVisibleItemIndex, Items.Count - 1)]
                : null;
            HistoryCommand.Execute(null);
        })
        .Invoke(view => SemanticProperties.SetDescription(
            view, "Load older messages"));

        return new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                button,
                new Label
                {
                    Text = "Load more",
                    InputTransparent = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }
                .Bind(IsVisibleProperty, nameof(IsLoadingHistory),
                    converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                    source: this),
                new ActivityIndicator
                {
                    InputTransparent = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }
                .Bind(ActivityIndicator.IsRunningProperty,
                    nameof(IsLoadingHistory), source: this)
                .Bind(IsVisibleProperty, nameof(IsLoadingHistory), source: this),
            },
        };
    }

    private void RestoreHistoryAnchor()
    {
        var anchor = _historyAnchor;
        _historyAnchor = null;
        if (anchor is null)
        {
            if (_followingLatest)
            {
                _scrollQueued = false;
                QueueScrollToLatest();
            }

            return;
        }

        if (Items?.Contains(anchor) != true)
        {
            return;
        }

        Dispatcher.Dispatch(() =>
            _collectionView.ScrollTo(anchor, ScrollToPosition.Start, animate: false));
    }

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
        _followingLatest = true;
        OnPropertyChanged(nameof(IsScrollToBottomVisible));
        QueueScrollToLatest();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_followingLatest ||
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

            if (Items.LastOrDefault() is { } latest)
            {
                _collectionView.ScrollTo(latest, ScrollToPosition.End, animate: false);
                _initialScrollPending = false;
            }
        });
    }

    private sealed class ChatTimelineTemplateSelector : DataTemplateSelector
    {
        private readonly DataTemplate _eventTemplate;
        private readonly DataTemplate _messageTemplate;

        public ChatTimelineTemplateSelector(ChatTimeline owner)
        {
            _eventTemplate = new DataTemplate(() => new ChatEventView()
                .Bind(ChatEventView.ItemProperty, ".")
                .Bind(ChatEventView.MenuCommandProperty,
                    nameof(MenuCommand), source: owner));
            _messageTemplate = new DataTemplate(() => new ChatMessageView()
                .Bind(ChatMessageView.ItemProperty, ".")
                .Bind(ChatMessageView.ClientProperty, nameof(Client), source: owner)
                .Bind(ChatMessageView.MembersProperty, nameof(Members), source: owner)
                .Bind(ChatMessageView.ReplyCommandProperty, nameof(ReplyCommand), source: owner)
                .Bind(ChatMessageView.EditCommandProperty, nameof(EditCommand), source: owner)
                .Bind(ChatMessageView.LinkCommandProperty, nameof(LinkCommand), source: owner)
                .Bind(ChatMessageView.MenuCommandProperty, nameof(MenuCommand), source: owner)
                .Bind(ChatMessageView.AddReactionCommandProperty,
                    nameof(AddReactionCommand), source: owner)
                .Bind(ChatMessageView.OpenMediaCommandProperty,
                    nameof(OpenMediaCommand), source: owner)
                .Bind(ChatMessageView.OpenProfileCommandProperty,
                    nameof(OpenProfileCommand), source: owner)
                .Bind(ChatMessageView.PollVoteCommandProperty,
                    nameof(PollVoteCommand), source: owner));
        }

        protected override DataTemplate OnSelectTemplate(
            object item,
            BindableObject container) => item is ChatTimelineItem { IsMessage: true }
                ? _messageTemplate
                : _eventTemplate;
    }
}
