using Dev.Naamloos.Fennec.Sdk.Helpers;
using System.Globalization;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RoomListGroup(string id, string name, string? avatarUrl) : List<RoomEntry>
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string? AvatarUrl { get; } = avatarUrl;
    public bool IsSpace => Id is not "" and not "$dms";
}

public sealed class InitialConverter : IValueConverter
{
    public static InitialConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && !string.IsNullOrWhiteSpace(name)
            ? StringInfo.GetNextTextElement(name).ToUpperInvariant()
            : "#";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
