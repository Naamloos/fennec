using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class SyntaxHighlighter : ContentView
{
    [BindableProperty]
    public partial string Text { get; set; } = string.Empty;

    public SyntaxHighlighter()
    {
        Content = new Editor
        {
            IsReadOnly = true,
            AutoSize = EditorAutoSizeOption.TextChanges,
            FontFamily = "Consolas",
            FontSize = 12,
            BackgroundColor = Colors.Transparent,
        }.Bind(Editor.TextProperty, nameof(Text), source: this);
    }
}
