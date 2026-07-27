namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ChatEventGroup : ObservableModel
{
    private bool _isCollapsed = true;

    public ChatEventGroup(int count) => Count = count;

    public int Count { get; }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => Set(ref _isCollapsed, value);
    }
}
