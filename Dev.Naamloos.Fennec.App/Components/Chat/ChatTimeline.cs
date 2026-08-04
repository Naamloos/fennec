using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MPowerKit.VirtualizeListView;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimeline : ContentView
{
    private readonly ChatVirtualizeListView _collectionView;
    private readonly Label _emptyView;
    private readonly Dev.Naamloos.Fennec.Sdk.Helpers.ObservableRangeCollection<object> _displayItems =
    [];
    private readonly Dictionary<string, ChatEventGroup> _eventGroups = [];
    private INotifyCollectionChanged? _itemsSource;
    private bool _initialScrollPending = true;
    private bool _followingLatest = true;
    private bool _scrollQueued;
    private bool _scrollPending;
    private double _previousScrollY;

    private double MaximumScrollY =>
        Math.Max(0, _collectionView.ContentSize.Height - _collectionView.Height);

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

    [BindableProperty]
    public partial ICommand? ThreadCommand { get; set; }

    [BindableProperty(
        DefaultBindingMode = BindingMode.TwoWay,
        PropertyChangedMethodName = nameof(OnIsNearBottomChanged)
    )]
    public partial bool IsNearBottom { get; set; } = true;

    [BindableProperty]
    public partial bool IsLoadingHistory { get; set; }

    [BindableProperty]
    public partial bool HasMoreHistory { get; set; }

    [BindableProperty]
    public partial string EmptyMessage { get; set; } = "No messages yet";

    [BindableProperty(PropertyChangedMethodName = nameof(OnFocusedEventIdChanged))]
    public partial string? FocusedEventId { get; set; }

    public bool IsScrollToBottomVisible => !IsNearBottom && Items is { Count: > 0 };

    public ChatTimeline()
    {
        _collectionView = new ChatVirtualizeListView
        {
            BackgroundColor = Colors.Transparent,
            Orientation = ScrollOrientation.Vertical,
            ItemsLayout = new LinearLayout { InitialCachePoolSize = 2 },
            ItemTemplate = new ChatTimelineTemplateSelector(this),
            Header = this,
            HeaderTemplate = new DataTemplate(CreateHeader),
        };
        _collectionView.ItemsSource = _displayItems;
        _collectionView.Scrolled += (_, eventArgs) => OnScrolled(eventArgs);
        _collectionView.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ScrollView.ContentSize) && _followingLatest)
            {
                QueueScrollToLatest();
            }
        };
        _collectionView.SizeChanged += (_, _) =>
        {
            if (_followingLatest)
            {
                QueueScrollToLatest();
            }
        };

        _emptyView = new Label
        {
            Opacity = .7,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        }.Bind(Label.TextProperty, nameof(EmptyMessage), source: this);

        Content = new Grid
        {
            BackgroundColor = Colors.Transparent,
            Children =
            {
                _collectionView,
                _emptyView,
                new ActivityIndicator
                {
                    Margin = new Thickness(12),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Start,
                }
                    .Bind(
                        ActivityIndicator.IsRunningProperty,
                        nameof(IsLoadingHistory),
                        source: this
                    )
                    .Bind(IsVisibleProperty, nameof(IsLoadingHistory), source: this),
                new Button
                {
                    Text = "↓ Latest",
                    Margin = new Thickness(12),
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.End,
                }
                    .Bind(IsVisibleProperty, nameof(IsScrollToBottomVisible), source: this)
                    .Invoke(button =>
                        button.Clicked += (_, _) =>
                        {
                            _followingLatest = true;
                            QueueScrollToLatest();
                        }
                    ),
            },
        };
        Loaded += (_, _) => QueueScrollToLatest();
    }

    private View CreateHeader() =>
        new TemplateSwitchView<bool, bool>(value => value)
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
            .Bind(
                TemplateSwitchView<bool, bool>.ValueProperty,
                nameof(HasMoreHistory),
                source: this
            );

    private void OnScrolled(ScrolledEventArgs eventArgs)
    {
        if (Items is not { Count: > 0 })
        {
            return;
        }

        IsNearBottom = eventArgs.ScrollY >= MaximumScrollY - 1;
        if (_initialScrollPending && IsNearBottom)
        {
            _initialScrollPending = false;
        }

        if (
            !_initialScrollPending
            && _followingLatest
            && eventArgs.ScrollY < _previousScrollY - 1
            && !IsNearBottom
        )
        {
            _followingLatest = false;
            _scrollPending = false;
        }

        _previousScrollY = eventArgs.ScrollY;
    }

    private static void OnItemsChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((ChatTimeline)bindable).SetItemsSource();

    private static void OnIsNearBottomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((ChatTimeline)bindable).OnPropertyChanged(nameof(IsScrollToBottomVisible));

    private static void OnFocusedEventIdChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    )
    {
        var timeline = (ChatTimeline)bindable;
        timeline.SyncDisplayItems();
        timeline.QueueScrollToFocusedEvent();
    }

    private View CreateLoadMoreButton()
    {
        var button = new Button { Text = string.Empty, MinimumWidthRequest = 120 }
            .Bind(
                IsEnabledProperty,
                nameof(IsLoadingHistory),
                converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                source: this
            )
            .Invoke(view =>
                view.Clicked += (_, _) =>
                {
                    if (IsLoadingHistory || HistoryCommand?.CanExecute(null) != true)
                    {
                        return;
                    }

                    _followingLatest = false;
                    HistoryCommand.Execute(null);
                }
            )
            .Invoke(view => SemanticProperties.SetDescription(view, "Load older messages"));

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
                }.Bind(
                    IsVisibleProperty,
                    nameof(IsLoadingHistory),
                    converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                    source: this
                ),
                new ActivityIndicator
                {
                    InputTransparent = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }
                    .Bind(
                        ActivityIndicator.IsRunningProperty,
                        nameof(IsLoadingHistory),
                        source: this
                    )
                    .Bind(IsVisibleProperty, nameof(IsLoadingHistory), source: this),
            },
        };
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

        SyncDisplayItems();
        _initialScrollPending = true;
        _followingLatest = true;
        _previousScrollY = 0;
        _emptyView.IsVisible = Items is not { Count: > 0 };
        OnPropertyChanged(nameof(IsScrollToBottomVisible));
        QueueScrollToLatest();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _emptyView.IsVisible = Items is not { Count: > 0 };
        if (!TryAppendDisplayItems(args))
            SyncDisplayItems();
        if (!string.IsNullOrWhiteSpace(FocusedEventId))
        {
            _followingLatest = false;
            QueueScrollToFocusedEvent();
            return;
        }

        if (
            _followingLatest
            || (
                IsNearBottom
                && args.Action == NotifyCollectionChangedAction.Add
                && args.NewStartingIndex >= Items!.Count - (args.NewItems?.Count ?? 0)
            )
        )
        {
            QueueScrollToLatest();
        }
    }

    private bool TryAppendDisplayItems(NotifyCollectionChangedEventArgs args)
    {
        if (
            args.Action != NotifyCollectionChangedAction.Add
            || args.NewStartingIndex < 0
            || Items is null
            || args.NewItems is null
            || args.NewStartingIndex + args.NewItems.Count != Items.Count
        )
            return false;

        var added = args.NewItems.OfType<ChatTimelineItem>().ToArray();
        if (added.Length != args.NewItems.Count || added.Any(IsGroupableEvent))
            return false;

        _displayItems.AddRange(added);
        return true;
    }

    private void QueueScrollToFocusedEvent()
    {
        if (
            string.IsNullOrWhiteSpace(FocusedEventId)
            || Items?.FirstOrDefault(item => item.EventId == FocusedEventId) is not { } item
            || DisplayItemFor(item) is not { } displayItem
        )
            return;

        if (displayItem is ChatEventGroup group)
            group.IsExpanded = true;

        Dispatcher.Dispatch(async () =>
            await _collectionView.ScrollToItem(displayItem, ScrollToPosition.Center, false)
        );
    }

    private void QueueScrollToLatest()
    {
        if (!_followingLatest || Items is not { Count: > 0 })
        {
            return;
        }

        if (_scrollQueued)
        {
            _scrollPending = true;
            return;
        }

        _scrollQueued = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (!_followingLatest || Items is not { Count: > 0 })
                {
                    return;
                }

                var target = MaximumScrollY;
                _previousScrollY = Math.Min(_previousScrollY, target);
                await _collectionView.ScrollToAsync(0, target, animated: false);
                _previousScrollY = _collectionView.ScrollY;
            }
            finally
            {
                _scrollQueued = false;
                if (_scrollPending)
                {
                    _scrollPending = false;
                    QueueScrollToLatest();
                }
            }
        });
    }

    private void SyncDisplayItems()
    {
        var desired = new List<object>();
        var activeGroupKeys = new HashSet<string>(StringComparer.Ordinal);

        if (Items is not null)
        {
            for (var index = 0; index < Items.Count; )
            {
                if (!IsGroupableEvent(Items[index]))
                {
                    desired.Add(Items[index++]);
                    continue;
                }

                var start = index;
                while (index < Items.Count && IsGroupableEvent(Items[index]))
                    index++;

                var count = index - start;
                if (count == 1)
                {
                    desired.Add(Items[start]);
                    continue;
                }

                var key = Items[start].Id;
                activeGroupKeys.Add(key);
                if (!_eventGroups.TryGetValue(key, out var group))
                {
                    group = new ChatEventGroup();
                    _eventGroups.Add(key, group);
                }

                group.Replace(Items.Skip(start).Take(count));
                desired.Add(group);
            }
        }

        foreach (
            var key in _eventGroups.Keys.Where(key => !activeGroupKeys.Contains(key)).ToArray()
        )
            _eventGroups.Remove(key);

        var prefix = 0;
        while (
            prefix < _displayItems.Count
            && prefix < desired.Count
            && ReferenceEquals(_displayItems[prefix], desired[prefix])
        )
            prefix++;

        var suffix = 0;
        while (
            suffix < _displayItems.Count - prefix
            && suffix < desired.Count - prefix
            && ReferenceEquals(_displayItems[^(suffix + 1)], desired[^(suffix + 1)])
        )
            suffix++;

        var oldMiddleCount = _displayItems.Count - prefix - suffix;
        var newMiddleCount = desired.Count - prefix - suffix;
        if (oldMiddleCount + newMiddleCount > 100 && suffix == 0)
        {
            _displayItems.ReplaceAll(desired);
            return;
        }

        while (oldMiddleCount-- > 0)
            _displayItems.RemoveAt(prefix);
        _displayItems.InsertRange(prefix, desired.Skip(prefix).Take(newMiddleCount));
    }

    private object? DisplayItemFor(ChatTimelineItem item) =>
        _displayItems.FirstOrDefault(candidate =>
            ReferenceEquals(candidate, item)
            || candidate is ChatEventGroup group && group.Items.Contains(item)
        );

    private bool IsGroupableEvent(ChatTimelineItem item) =>
        !item.IsMessage
        && !item.IsReadMarker
        && item.EventId != FocusedEventId
        && item.EventType is not "date divider" and not "timeline start" and not "timeline marker";

    private sealed class ChatTimelineTemplateSelector : DataTemplateSelector
    {
        public DataTemplate EventTemplate { get; }

        public DataTemplate EventGroupTemplate { get; }

        public DataTemplate MessageTemplate { get; }

        public ChatTimelineTemplateSelector(ChatTimeline owner)
        {
            EventTemplate = new DataTemplate(() =>
                new ChatEventView().Bind(
                    ChatEventView.MenuCommandProperty,
                    nameof(MenuCommand),
                    source: owner
                )
            );
            EventGroupTemplate = new DataTemplate(() =>
                new ChatEventGroupView().Bind(
                    ChatEventGroupView.MenuCommandProperty,
                    nameof(MenuCommand),
                    source: owner
                )
            );
            MessageTemplate = new DataTemplate(() =>
                new ChatMessageView()
                    .Bind(ChatMessageView.ClientProperty, nameof(Client), source: owner)
                    .Bind(ChatMessageView.MembersProperty, nameof(Members), source: owner)
                    .Bind(ChatMessageView.ReplyCommandProperty, nameof(ReplyCommand), source: owner)
                    .Bind(ChatMessageView.EditCommandProperty, nameof(EditCommand), source: owner)
                    .Bind(ChatMessageView.LinkCommandProperty, nameof(LinkCommand), source: owner)
                    .Bind(ChatMessageView.MenuCommandProperty, nameof(MenuCommand), source: owner)
                    .Bind(
                        ChatMessageView.AddReactionCommandProperty,
                        nameof(AddReactionCommand),
                        source: owner
                    )
                    .Bind(
                        ChatMessageView.OpenMediaCommandProperty,
                        nameof(OpenMediaCommand),
                        source: owner
                    )
                    .Bind(
                        ChatMessageView.OpenProfileCommandProperty,
                        nameof(OpenProfileCommand),
                        source: owner
                    )
                    .Bind(
                        ChatMessageView.PollVoteCommandProperty,
                        nameof(PollVoteCommand),
                        source: owner
                    )
                    .Bind(
                        ChatMessageView.ThreadCommandProperty,
                        nameof(ThreadCommand),
                        source: owner
                    )
            );
        }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
            item switch
            {
                ChatEventGroup => EventGroupTemplate,
                ChatTimelineItem { IsMessage: true } => MessageTemplate,
                _ => EventTemplate,
            };
    }

    private sealed class ChatVirtualizeListView : VirtualizeListView
    {
        public ChatVirtualizeListView()
        {
            Adapter = new ChatDataAdapter(this);
        }
    }

    private sealed class ChatDataAdapter(VirtualizeListView listView)
        : GroupableDataAdapter(listView)
    {
        private INotifyCollectionChanged? _subscribedSource;

        public override void InitCollection(IEnumerable? itemsSource)
        {
            if (_subscribedSource is not null)
            {
                _subscribedSource.CollectionChanged -= ItemsSourceCollectionChanged;
            }

            _subscribedSource = itemsSource as INotifyCollectionChanged;
            base.InitCollection(itemsSource);
        }

        protected override void Dispose(bool disposing)
        {
            if (_subscribedSource is not null)
            {
                _subscribedSource.CollectionChanged -= ItemsSourceCollectionChanged;
                _subscribedSource = null;
            }

            base.Dispose(disposing);
        }
    }
}
