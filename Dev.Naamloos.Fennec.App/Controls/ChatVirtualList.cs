using System.Collections;

#if ANDROID || IOS || MACCATALYST
using Nalu;
#endif

namespace Dev.Naamloos.Fennec.App.Controls;

public sealed class ChatVirtualList : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ChatVirtualList), propertyChanged: ApplyItemsSource);
    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(ChatVirtualList), propertyChanged: ApplyItemTemplate);

#if ANDROID || IOS || MACCATALYST
    private readonly VirtualScroll _list = new();
#else
    private readonly CollectionView _list = new() { SelectionMode = SelectionMode.None };
#endif
#if ANDROID || IOS || MACCATALYST
    private double _lastY;
#endif

    public ChatVirtualList()
    {
        Content = _list;
#if ANDROID || IOS || MACCATALYST
        _list.OnScrolled += (_, args) => RaiseScrolled(
            args.RemainingScrollY < 80,
            args.ScrollY < 180,
            args.ScrollY - _lastY);
#else
        _list.Scrolled += (_, args) => RaiseScrolled(
            args.LastVisibleItemIndex >= (_list.ItemsSource?.Cast<object>().Count() ?? 0) - 2,
            args.FirstVisibleItemIndex <= 5,
            args.VerticalDelta);
#endif
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public event EventHandler<ChatScrollEventArgs>? Scrolled;

    public void ScrollToEnd(int index)
    {
        if (index < 0) return;
#if ANDROID || IOS || MACCATALYST
        _list.ScrollTo(0, index, ScrollToPosition.End, false);
#else
        _list.ScrollTo(index, position: ScrollToPosition.End, animate: false);
#endif
    }

    public void KeepBottom(bool value)
    {
#if WINDOWS
        _list.ItemsUpdatingScrollMode = value
            ? ItemsUpdatingScrollMode.KeepLastItemInView
            : ItemsUpdatingScrollMode.KeepScrollOffset;
#endif
    }

    private void RaiseScrolled(bool nearBottom, bool nearTop, double delta)
    {
#if ANDROID || IOS || MACCATALYST
        _lastY += delta;
#endif
        Scrolled?.Invoke(this, new ChatScrollEventArgs(nearBottom, nearTop, delta));
    }

    private static void ApplyItemsSource(BindableObject bindable, object oldValue, object newValue) =>
        ((ChatVirtualList)bindable)._list.ItemsSource = (IEnumerable?)newValue;

    private static void ApplyItemTemplate(BindableObject bindable, object oldValue, object newValue) =>
        ((ChatVirtualList)bindable)._list.ItemTemplate = (DataTemplate?)newValue;
}

public sealed record ChatScrollEventArgs(bool IsNearBottom, bool IsNearTop, double Delta);
