using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class AboutSettingsView : ContentView
{
    public string Version => AppInfo.Current.VersionString;

    public AboutSettingsView()
    {
        Content = new SettingsSection(
            "About",
            new Border
            {
                Padding = new Thickness(28, 32),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                Content = new VerticalStackLayout
                {
                    Spacing = 10,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 76,
                            HeightRequest = 76,
                            HorizontalOptions = LayoutOptions.Center,
                            StrokeThickness = 0,
                            StrokeShape = new RoundRectangle { CornerRadius = 38 },
                            Content = new Image
                            {
                                Source = "fennec_icon.png",
                                Aspect = Aspect.AspectFit,
                            },
                        },
                        new Label
                        {
                            Text = "Fennec",
                            FontSize = 28,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalTextAlignment = TextAlignment.Center,
                        },
                        new Border
                        {
                            Padding = new Thickness(10, 4),
                            HorizontalOptions = LayoutOptions.Center,
                            StrokeThickness = 0,
                            StrokeShape = new RoundRectangle { CornerRadius = 12 },
                            Content = new Label { FontSize = 12 }.Bind(
                                Label.TextProperty,
                                nameof(Version),
                                source: this
                            ),
                        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
                        new Label
                        {
                            Text =
                                "Cross-platform [Matrix] Client in C# / .NET MAUI using Matrix-SDK-Rust",
                            Opacity = .7,
                            HorizontalTextAlignment = TextAlignment.Center,
                        },
                    },
                },
            }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2"),
            CreateLinkCard(
                "Source code",
                "Browse issues, contribute, or build Fennec yourself.",
                nameof(OpenSourceCodeCommand)
            ),
            CreateLinkCard(
                "Support Fennec",
                "Your support is greatly appreciated 💖",
                nameof(OpenDonateCommand)
            )
        );
    }

    [RelayCommand]
    private Task OpenSourceCodeAsync() =>
        Launcher.Default.OpenAsync("https://github.com/Naamloos/fennec");

    [RelayCommand]
    private Task OpenDonateAsync() => Launcher.Default.OpenAsync("https://ko-fi.com/naamloos");

    private View CreateLinkCard(string title, string description, string command) =>
        new Border
        {
            Padding = new Thickness(16, 12),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 12,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 3,
                        Children =
                        {
                            new Label { Text = title, FontAttributes = FontAttributes.Bold },
                            new Label
                            {
                                Text = description,
                                FontSize = 12,
                                Opacity = .7,
                            },
                        },
                    },
                    new Button
                    {
                        Text = "Open",
                        BackgroundColor = Colors.Transparent,
                        Padding = new Thickness(8, 4),
                        FontSize = 12,
                    }
                        .DynamicResource(Button.TextColorProperty, "Primary")
                        .BindCommand(command, source: this)
                        .Column(1),
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2");
}
