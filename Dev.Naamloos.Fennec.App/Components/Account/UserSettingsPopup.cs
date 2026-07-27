using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Converters;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public partial class UserSettingsPopup : ContentView
{
    [BindableProperty]
    public partial string DisplayName { get; set; }

    [BindableProperty]
    public partial string UserId { get; set; }

    [BindableProperty]
    public partial ImageSource? AvatarSource { get; set; }

    [BindableProperty]
    public partial ICommand LogoutCommand { get; set; }

    [BindableProperty]
    public partial ToastService ToastService { get; set; }

    public UserSettingsPopup()
    {
        // testing self-binding services
        this.BindService<ToastService, UserSettingsPopup>(ToastServiceProperty);

        build();
    }

    [RelayCommand]
    private async Task CloseUserSettingsPopup()
    {
        if (Parent is Popup popup)
        {
            await popup.CloseAsync();
        }
    }

    [RelayCommand]
    private async Task TestToast()
    {
        if(ToastService is not null)
        {
            await ToastService.ShowToastAsync("This is a test toast message!");
        }
    }

    private void build()
    {
        Content = new VerticalStackLayout
        {
            Padding = 20,
            Spacing = 12,
            MaximumWidthRequest = 500,
            Children =
            {
                // User avatar with fallback label
                new Border
                {
                    WidthRequest = 64,
                    HeightRequest = 64,
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 32 },
                    Content = new Grid
                    {
                        // Fallback label
                        new Label
                        {
                            Text = "A",
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment = TextAlignment.Center
                        }
                        .Bind(Label.TextProperty, nameof(UserSettingsPopup.DisplayName), converter: new SubstringConverter(1, 0), source: this),

                        // Avatar Image
                        new Image
                        {
                            Aspect = Aspect.AspectFill,
                        }
                        .Bind(Image.SourceProperty, nameof(UserSettingsPopup.AvatarSource), source: this)
                    }
                }.DynamicResource(VisualElement.BackgroundColorProperty, "PrimaryContainer"),

                // Display name
                new Label
                {
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold
                }
                .Bind(Label.TextProperty, nameof(UserSettingsPopup.DisplayName), source: this),

                // User ID
                new Label
                {
                    Opacity = .7
                }
                .Bind(Label.TextProperty, nameof(UserSettingsPopup.UserId), source: this),

                // Logout button
                new Button
                {
                    Text = "Log out",
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.Red,
                }.BindCommand(nameof(UserSettingsPopup.LogoutCommand), source: this),

                // Close button
                new Button
                {
                    Text = "Close"
                }.BindCommand(nameof(UserSettingsPopup.CloseUserSettingsPopupCommand), source: this),

                new Button
                {
                    Text = "TestToast"
                }.BindCommand(nameof(UserSettingsPopup.TestToastCommand), source: this)
            }
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}
