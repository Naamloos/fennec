using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class UserSettingsPopup : Popup
{
    public UserSettingsPopup(
        ImageSource? avatarSource,
        string initial,
        string displayName,
        string userId,
        Func<Task> logout)
    {
        var avatar = new Border
        {
            WidthRequest = 64, HeightRequest = 64, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 32 },
            Content = new Grid { Children = { new Label { Text = initial, FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }, new Image { Source = avatarSource, Aspect = Aspect.AspectFill } } },
        };
        avatar.SetDynamicResource(VisualElement.BackgroundColorProperty, "PrimaryContainer");
        var close = new Button { Text = "Close" };
        close.Clicked += async (_, _) => await CloseAsync();
        var logoutButton = new Button
        {
            Text = "Log out",
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Red,
        };
        logoutButton.Clicked += async (_, _) =>
        {
            await CloseAsync();
            await logout();
        };
        var card = new Border
        {
            Padding = 20, MaximumWidthRequest = 380, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    avatar,
                    new Label { Text = displayName, FontSize = 22, FontAttributes = FontAttributes.Bold },
                    new Label { Text = userId, Opacity = .7 },
                    new Label { Text = "Bio", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) },
                    new Label { Text = "No bio", Opacity = .7 },
                    logoutButton,
                    close,
                },
            },
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        Content = card;
    }
}
