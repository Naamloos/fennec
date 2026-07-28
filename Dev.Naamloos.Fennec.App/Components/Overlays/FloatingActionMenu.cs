using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class FloatingActionMenu : FloatingOverlay<string>
{
    public FloatingActionMenu(string title, IEnumerable<string> actions, string? message = null)
    {
        var actionList = new VerticalStackLayout { Spacing = 6 };
        foreach (var action in actions)
        {
            actionList.Children.Add(new Button
            {
                Text = action,
                HorizontalOptions = LayoutOptions.Fill,
                Command = new Command(() => Complete(action)),
            }
            .DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainerHigh")
            .DynamicResource(Button.TextColorProperty, "OnSurface"));
        }

        Content = new Grid
        {
            Children =
            {
                new BoxView { Color = Color.FromArgb("#66000000"), GestureRecognizers = { DismissGesture() } },
                new Border
                {
                    Margin = 16,
                    Padding = 18,
                    MaximumWidthRequest = 420,
                    MaximumHeightRequest = 560,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            new Label { Text = title, FontSize = 19, FontAttributes = FontAttributes.Bold },
                            new Label { Text = message, Opacity = .72, IsVisible = !string.IsNullOrWhiteSpace(message) },
                            new ScrollView { Content = actionList },
                            new Button { Text = "Cancel", BackgroundColor = Colors.Transparent,
                                Command = new Command(() => Complete(null)) }
                                .DynamicResource(Button.TextColorProperty, "Primary"),
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
            },
        };
    }
}
