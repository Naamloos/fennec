using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class SettingsSection : VerticalStackLayout
{
    public SettingsSection(string title, params View[] children)
    {
        Spacing = 8;
        Margin = new Thickness(0, 0, 0, 20);
        Children.Add(new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
        }.DynamicResource(Label.TextColorProperty, "Primary"));

        foreach (var child in children)
        {
            Children.Add(child);
        }
    }
}
