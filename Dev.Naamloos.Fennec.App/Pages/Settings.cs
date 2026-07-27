using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Settings : ContentPage
{
    public Settings(AppShell shell)
    {
        BindingContext = shell;
        Shell.SetNavBarIsVisible(this, false);

        Content = new Grid
        {
            Padding = 24,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
                        new Button
                        {
                            Text = "‹",
                            FontSize = 28,
                            Padding = new Thickness(8, 0),
                        }.BindCommand(nameof(BackCommand), source: this),
                        new Label
                        {
                            Text = "Settings",
                            FontSize = 20,
                            FontAttributes = FontAttributes.Bold,
                            VerticalTextAlignment = TextAlignment.Center,
                        },
                    },
                },
                new Button
                {
                    Text = "Verify this session",
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }
                .BindCommand(nameof(AppShell.StartVerificationCommand))
                .Row(1),
                new Button
                {
                    Text = "Log out",
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.Red,
                    HorizontalOptions = LayoutOptions.Fill,
                }
                .BindCommand(nameof(AppShell.LogoutCommand))
                .Row(2),
            },
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }

    [RelayCommand]
    private async Task BackAsync() => await Navigation.PopAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();

#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Disabled;
        }
#endif
    }

    protected override void OnDisappearing()
    {
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Flyout;
        }
#endif

        base.OnDisappearing();
    }
}
