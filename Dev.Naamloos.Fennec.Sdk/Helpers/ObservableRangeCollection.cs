using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        var added = items.ToArray();
        if (added.Length == 0)
            return;
        if (added.Length == 1)
        {
            Add(added[0]);
            return;
        }

        CheckReentrancy();
        foreach (var item in added)
            Items.Add(item);
        RaiseReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        var replacement = items.ToArray();
        if (Count == 0 && replacement.Length == 0)
            return;

        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement)
            Items.Add(item);
        RaiseReset();
    }

    public void InsertRange(int index, IEnumerable<T> items)
    {
        var added = items.ToArray();
        if (added.Length == 0)
            return;
        if (added.Length == 1)
        {
            Insert(index, added[0]);
            return;
        }

        CheckReentrancy();
        foreach (var item in added)
            Items.Insert(index++, item);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }
}
