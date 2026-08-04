using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Services;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class ClientSettingsView : ContentView
{
    public static readonly BindableProperty UserSettingsProperty = BindableProperty.Create(
        nameof(UserSettings),
        typeof(UserSettingsService),
        typeof(ClientSettingsView)
    );

    public UserSettingsService? UserSettings
    {
        get => (UserSettingsService?)GetValue(UserSettingsProperty);
        set => SetValue(UserSettingsProperty, value);
    }

    public ClientSettingsView()
    {
        this.BindService<UserSettingsService, ClientSettingsView>(UserSettingsProperty);

        Content = new SettingsSection(
            "Client",
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label { Text = "Emoji font" },
                            new Label { Text = "Only applies to emoji glyphs.", Opacity = .7, FontSize = 12 },
                        },
                    },
                    new Picker { Title = "System default", ItemDisplayBinding = new Binding(nameof(EmojiFontOption.DisplayName)) }
                        .Bind(Picker.ItemsSourceProperty, $"{nameof(UserSettings)}.{nameof(UserSettingsService.EmojiFontOptions)}", source: this)
                        .Bind(Picker.SelectedItemProperty, $"{nameof(UserSettings)}.{nameof(UserSettingsService.SelectedEmojiFont)}", BindingMode.TwoWay, source: this)
                        .Column(1),
                },
            }
        );
    }
}
