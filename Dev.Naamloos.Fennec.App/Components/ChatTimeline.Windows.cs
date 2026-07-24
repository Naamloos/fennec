#if WINDOWS

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ItemsView = Microsoft.UI.Xaml.Controls.ItemsView;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatTimeline
{
    private FrameworkElement? _windowsPlatformView;

    partial void InitializePlatformCollectionView()
    {
        #if !WINDOWS
        return;
        #endif
        _collectionView.HandlerChanged +=
            OnCollectionViewHandlerChanged;

        TryAttachToWindowsCollectionView();
    }

    partial void DisposePlatformCollectionView()
    {
        #if !WINDOWS
        return;
        #endif
        _collectionView.HandlerChanged -=
            OnCollectionViewHandlerChanged;

        DetachFromWindowsPlatformView();
    }

    private void OnCollectionViewHandlerChanged(
        object? sender,
        EventArgs eventArgs)
    {
        TryAttachToWindowsCollectionView();
    }

    private void TryAttachToWindowsCollectionView()
    {
        DetachFromWindowsPlatformView();

        _windowsPlatformView =
            _collectionView.Handler?.PlatformView as
                FrameworkElement;

        if (_windowsPlatformView is null)
        {
            return;
        }

        _windowsPlatformView.Loaded +=
            OnWindowsCollectionViewLoaded;

        /*
         * The handler may already be loaded by the time HandlerChanged
         * fires, so also attempt to disable the transitions immediately.
         */
        Dispatcher.Dispatch(
            DisableWindowsItemAnimations);
    }

    private void DetachFromWindowsPlatformView()
    {
        if (_windowsPlatformView is null)
        {
            return;
        }

        _windowsPlatformView.Loaded -=
            OnWindowsCollectionViewLoaded;

        _windowsPlatformView = null;
    }

    private void OnWindowsCollectionViewLoaded(
        object sender,
        RoutedEventArgs eventArgs)
    {
        /*
         * Wait one dispatcher turn because the ItemsView and its internal
         * ItemsRepeater may only be created after the root native control
         * has completed its Loaded event.
         */
        Dispatcher.Dispatch(
            DisableWindowsItemAnimations);
    }

    private void DisableWindowsItemAnimations()
    {
        if (_windowsPlatformView is null)
        {
            return;
        }

        /*
         * Newer MAUI CollectionView handlers use a WinUI ItemsView.
         * ItemTransitionProvider controls add, remove and move animations.
         */
        if (_windowsPlatformView is ItemsView rootItemsView)
        {
            rootItemsView.ItemTransitionProvider = null;
        }

        var itemsView =
            FindDescendant<ItemsView>(
                _windowsPlatformView);

        if (itemsView is not null)
        {
            itemsView.ItemTransitionProvider = null;
        }

        /*
         * Depending on the MAUI/Windows App SDK version, the platform view
         * may expose an ItemsRepeater directly or nest one inside ItemsView.
         */
        var itemsRepeater =
            FindDescendant<ItemsRepeater>(
                _windowsPlatformView);

        if (itemsRepeater is not null)
        {
            itemsRepeater.ItemTransitionProvider = null;
        }

        /*
         * Keep compatibility with older MAUI CollectionView handlers that
         * still use ListViewBase internally.
         */
        var listView =
            FindDescendant<ListViewBase>(
                _windowsPlatformView);

        listView?.ItemContainerTransitions.Clear();
    }

    private static T? FindDescendant<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        var childCount =
            VisualTreeHelper.GetChildrenCount(
                parent);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper.GetChild(
                    parent,
                    index);

            if (child is T match)
            {
                return match;
            }

            var nested =
                FindDescendant<T>(child);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}

#endif
