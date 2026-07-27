namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ChatReadReceipt : ObservableModel
{
    private string _name = string.Empty;
    private string? _avatarUrl;

    public ChatReadReceipt(string userId, string name, string? avatarUrl)
    {
        UserId = userId;
        Name = name;
        AvatarUrl = avatarUrl;
    }

    public string UserId { get; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string? AvatarUrl
    {
        get => _avatarUrl;
        set => Set(ref _avatarUrl, value);
    }
}
