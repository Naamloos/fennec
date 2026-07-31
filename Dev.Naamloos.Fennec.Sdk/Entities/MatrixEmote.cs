namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class MatrixEmote : ObservableModel
{
    public MatrixEmote(string name, string body, string source, string? packId = null)
    {
        Name = name;
        Body = body;
        Source = source;
        PackId = packId;
    }

    public string Name { get; }

    public string Body { get; }

    public string Source { get; }

    public string? PackId { get; }
}
