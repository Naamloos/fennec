namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ChatReaction : ObservableModel
{
    private int _count;
    private bool _isOwn;

    public ChatReaction(string key, int count, bool isOwn)
    {
        Key = key;
        Count = count;
        IsOwn = isOwn;
    }

    public string Key { get; }

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }

    public bool IsOwn
    {
        get => _isOwn;
        set => Set(ref _isOwn, value);
    }
}
