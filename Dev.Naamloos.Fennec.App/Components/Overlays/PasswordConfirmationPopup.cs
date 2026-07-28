using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class PasswordConfirmationPopup : Popup<string?>
{
    private readonly Entry _password = new() { IsPassword = true, Placeholder = "Password" };

    public PasswordConfirmationPopup(string title, string message, string accept)
    {
        CanBeDismissedByTappingOutsideOfPopup = true;
        BackgroundColor = Colors.Transparent;
        Content = new Border
        {
            Padding = 20,
            MaximumWidthRequest = 420,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold },
                    new Label { Text = message },
                    _password,
                    new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                        ColumnSpacing = 8,
                        Children =
                        {
                            new Button { Text = "Cancel", BackgroundColor = Colors.Transparent, Command = new Command(async () => await CloseAsync(null)) }
                                .DynamicResource(Button.TextColorProperty, "Primary"),
                            new Button { Text = accept, Command = new Command(async () => await CloseAsync(_password.Text)) }
                                .DynamicResource(VisualElement.BackgroundColorProperty, "Error")
                                .DynamicResource(Button.TextColorProperty, "OnError").Column(1),
                        },
                    },
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}
