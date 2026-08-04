using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed record PollDraft(string Question, IReadOnlyList<string> Answers);

public sealed class PollComposer : FloatingOverlay<PollDraft>
{
    private readonly Entry _question = new() { Placeholder = "Ask a question" };
    private readonly VerticalStackLayout _answers = new() { Spacing = 8 };
    private readonly Label _validation = new() { TextColor = Colors.Red, IsVisible = false };

    public PollComposer()
    {
        AddAnswer();
        AddAnswer();
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
                    Margin =
                        DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? new Thickness(8)
                            : new Thickness(24),
                    Padding =
                        DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? new Thickness(16)
                            : new Thickness(20),
                    MaximumWidthRequest = 460,
                    MaximumHeightRequest = 620,
                    HorizontalOptions =
                        DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? LayoutOptions.Fill
                            : LayoutOptions.Center,
                    VerticalOptions =
                        DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? LayoutOptions.End
                            : LayoutOptions.Center,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = 20,
                    },
                    Content = new Grid
                    {
                        RowDefinitions =
                        {
                            new RowDefinition(GridLength.Auto),
                            new RowDefinition(GridLength.Star),
                            new RowDefinition(GridLength.Auto),
                        },
                        Children =
                        {
                            new VerticalStackLayout
                            {
                                Spacing = 10,
                                Children =
                                {
                                    new Label
                                    {
                                        Text = "Create poll",
                                        FontSize = 20,
                                        FontAttributes = FontAttributes.Bold,
                                    },
                                    _question,
                                    _validation,
                                    new Grid
                                    {
                                        ColumnDefinitions =
                                        {
                                            new ColumnDefinition(GridLength.Star),
                                            new ColumnDefinition(GridLength.Auto),
                                        },
                                        Children =
                                        {
                                            new Label
                                            {
                                                Text = "Answers",
                                                FontAttributes = FontAttributes.Bold,
                                            },
                                            new Button
                                            {
                                                Text = "+ Add answer",
                                                BackgroundColor = Colors.Transparent,
                                                Command = new Command(AddAnswer),
                                            }
                                                .DynamicResource(
                                                    Button.TextColorProperty,
                                                    "Primary"
                                                )
                                                .Column(1),
                                        },
                                    },
                                },
                            }.Row(0),
                            new ScrollView { Content = _answers }.Row(1),
                            new Grid
                            {
                                Margin = new Thickness(0, 12, 0, 0),
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
                                        Text = "Create poll",
                                        Command = new Command(Create),
                                    }
                                        .DynamicResource(
                                            VisualElement.BackgroundColorProperty,
                                            "Primary"
                                        )
                                        .DynamicResource(Button.TextColorProperty, "OnPrimary")
                                        .Column(1),
                                },
                            }.Row(2),
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
            },
        };
    }

    private void AddAnswer()
    {
        var answer = new Entry { Placeholder = $"Answer {_answers.Children.Count + 1}" };
        _answers.Children.Add(
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children =
                {
                    answer.Column(0),
                    new Button
                    {
                        Text = "×",
                        BackgroundColor = Colors.Transparent,
                        WidthRequest = 42,
                        Command = new Command(() =>
                            _answers.Children.Remove(answer.Parent as View)
                        ),
                    }
                        .DynamicResource(Button.TextColorProperty, "OnSurface")
                        .Column(1),
                },
            }
        );
    }

    private void Create()
    {
        var answers = _answers
            .Children.OfType<Grid>()
            .Select(row => row.Children.OfType<Entry>().FirstOrDefault()?.Text?.Trim())
            .Where(answer => !string.IsNullOrWhiteSpace(answer))
            .Cast<string>()
            .ToArray();
        if (string.IsNullOrWhiteSpace(_question.Text) || answers.Length < 2)
        {
            _validation.Text = "Add a question and at least two answers.";
            _validation.IsVisible = true;
            return;
        }

        Complete(new PollDraft(_question.Text.Trim(), answers));
    }
}
