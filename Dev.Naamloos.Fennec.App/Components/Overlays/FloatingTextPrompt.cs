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
        bool isPassword = false
    )
    {
        _input = multiline
            ? new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 110 }
            : new Entry { IsPassword = isPassword };
        _input.Placeholder = placeholder;
        _input.Text = initialValue;
        if (_input is Entry entry)
            entry.Completed += (_, _) => Complete(_input.Text);

        Content = new Grid
        {
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#66000000"),
                    GestureRecognizers = { DismissGesture() },
                },
                new Border
                {
                    Margin = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? new Thickness(8)
                        : new Thickness(24),
                    Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? new Thickness(16)
                        : new Thickness(20),
                    MaximumWidthRequest = 420,
                    HorizontalOptions = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? LayoutOptions.Fill
                        : LayoutOptions.Center,
                    VerticalOptions = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? LayoutOptions.End
                        : LayoutOptions.Center,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = 20,
                    },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 12,
                        Children =
                        {
                            new Label
                            {
                                Text = title,
                                FontSize = 20,
                                FontAttributes = FontAttributes.Bold,
                            },
                            new Label
                            {
                                Text = message,
                                Opacity = .72,
                                LineBreakMode = LineBreakMode.WordWrap,
                            },
                            _input,
                            new Grid
                            {
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Star),
                                },
                                ColumnSpacing = 8,
                                Children =
                                {
                                    new Button
                                    {
                                        Text = "Cancel",
                                        BackgroundColor = Colors.Transparent,
                                        Command = new Command(() => Complete(null)),
                                    }.DynamicResource(Button.TextColorProperty, "Primary"),
                                    new Button
                                    {
                                        Text = accept,
                                        Command = new Command(() => Complete(_input.Text)),
                                    }
                                        .DynamicResource(
                                            VisualElement.BackgroundColorProperty,
                                            "Primary"
                                        )
                                        .DynamicResource(Button.TextColorProperty, "OnPrimary")
                                        .Column(1),
                                },
                            },
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
            },
        };
        Loaded += (_, _) => _input.Focus();
    }
}
