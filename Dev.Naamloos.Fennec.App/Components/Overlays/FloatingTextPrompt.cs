using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class FloatingTextPrompt : FloatingOverlay<string>
{
    private readonly InputView _input;

    public FloatingTextPrompt(
        string title,
        string message,
        string accept = "Continue",
        string? placeholder = null,
        string? initialValue = null,
        bool multiline = false,
        bool isPassword = false)
    {
        _input = multiline
            ? new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 110 }
            : new Entry { IsPassword = isPassword };
        _input.Placeholder = placeholder;
        _input.Text = initialValue;

        Content = new Grid
        {
            Children =
            {
                new BoxView { Color = Color.FromArgb("#66000000"), GestureRecognizers = { DismissGesture() } },
                new Border
                {
                    Margin = 24,
                    Padding = 20,
                    MaximumWidthRequest = 420,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 12,
                        Children =
                        {
                            new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold },
                            new Label { Text = message, Opacity = .72 },
                            _input,
                            new Grid
                            {
                                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                                ColumnSpacing = 8,
                                Children =
                                {
                                    new Button { Text = "Cancel", BackgroundColor = Colors.Transparent,
                                        Command = new Command(() => Complete(null)) }
                                        .DynamicResource(Button.TextColorProperty, "Primary"),
                                    new Button { Text = accept,
                                        Command = new Command(() => Complete(_input.Text)) }
                                        .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
                                        .DynamicResource(Button.TextColorProperty, "OnPrimary")
                                        .Column(1),
                                },
                            },
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
            },
        };
    }
}
