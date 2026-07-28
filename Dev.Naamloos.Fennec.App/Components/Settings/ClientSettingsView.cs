using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Services;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class ClientSettingsView : ContentView
{
    public static readonly BindableProperty UserSettingsProperty = BindableProperty.Create(
        nameof(UserSettings), typeof(UserSettingsService), typeof(ClientSettingsView));

    public UserSettingsService? UserSettings
    {
        get => (UserSettingsService?)GetValue(UserSettingsProperty);
        set => SetValue(UserSettingsProperty, value);
    }

    public ClientSettingsView()
    {
        this.BindService<UserSettingsService, ClientSettingsView>(UserSettingsProperty);

        Content = new SettingsSection("Client",
            new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label { Text = "Experimental feature" },
                            new Label { Text = "Reserved for a future Fennec feature.", Opacity = .7, FontSize = 12 },
                        },
                    },
                    new Switch().Bind(
                        Switch.IsToggledProperty,
                        $"{nameof(UserSettings)}.{nameof(UserSettingsService.ExperimentalFeatureEnabled)}",
                        BindingMode.TwoWay,
                        source: this).Column(1),
                },
            });
    }
}
