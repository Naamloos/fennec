namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class MatrixEmote : ObservableModel
{
    public MatrixEmote(string name, string body, string source)
    {
        Name = name;
        Body = body;
        Source = source;
    }

    public string Name { get; }

    public string Body { get; }

    public string Source { get; }
}
