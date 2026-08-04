using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class FloatingActionMenu : FloatingOverlay<string>
{
    private sealed record MenuAction(string Text, bool IsDestructive);

    public FloatingActionMenu(string title, IEnumerable<string> actions, string? message = null)
    {
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
                    Padding = new Thickness(12, 8, 12, 12),
                    MaximumWidthRequest = 400,
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
                        CornerRadius = 24,
                    },
                    Content = new Grid
                    {
                        RowSpacing = 4,
                        RowDefinitions =
                        {
                            new RowDefinition(GridLength.Auto),
                            new RowDefinition(GridLength.Auto),
                            new RowDefinition(GridLength.Auto),
                            new RowDefinition(GridLength.Star),
                        },
                        Children =
                        {
                            new BoxView
                            {
                                WidthRequest = 36,
                                HeightRequest = 4,
                                CornerRadius = 2,
                                Opacity = .35,
                                HorizontalOptions = LayoutOptions.Center,
                                IsVisible = DeviceInfo.Current.Idiom == DeviceIdiom.Phone,
                            }
                                .DynamicResource(BoxView.ColorProperty, "OnSurfaceVariant")
                                .Row(0),
                            new Grid
                            {
                                Padding = new Thickness(6, 2),
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Auto),
                                },
                                Children =
                                {
                                    new Label
                                    {
                                        Text = title,
                                        FontSize = 18,
                                        FontAttributes = FontAttributes.Bold,
                                        VerticalTextAlignment = TextAlignment.Center,
                                    },
                                    new Button
                                    {
                                        Text = "×",
                                        FontSize = 24,
                                        WidthRequest = 44,
                                        HeightRequest = 44,
                                        Padding = 0,
                                        BackgroundColor = Colors.Transparent,
                                        Command = new Command(() => Complete(null)),
                                    }
                                        .DynamicResource(
                                            Button.TextColorProperty,
                                            "OnSurfaceVariant"
                                        )
                                        .Invoke(view =>
                                            SemanticProperties.SetDescription(view, "Close menu")
                                        )
                                        .Column(1),
                                },
                            }.Row(1),
                            new Label
                            {
                                Text = message,
                                Margin = new Thickness(6, 0, 6, 6),
                                Opacity = .72,
                                IsVisible = !string.IsNullOrWhiteSpace(message),
                                MaxLines = 2,
                                LineBreakMode = LineBreakMode.TailTruncation,
                            }.Row(2),
                            new CollectionView
                            {
                                ItemsSource = actions
                                    .Select(action => new MenuAction(
                                        action,
                                        IsDestructiveAction(action)
                                    ))
                                    .ToArray(),
                                SelectionMode = SelectionMode.None,
                                VerticalScrollBarVisibility = ScrollBarVisibility.Default,
                                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
                                {
                                    ItemSpacing = 2,
                                },
                                ItemTemplate = new DataTemplate(() =>
                                    new TemplateSwitchView<MenuAction, bool>(value =>
                                        value.IsDestructive
                                    )
                                    {
                                        FallbackTemplate = CreateActionButton(false),
                                    }
                                        .Add(value => value, CreateActionButton(true))
                                        .Bind(
                                            TemplateSwitchView<MenuAction, bool>.ValueProperty,
                                            "."
                                        )
                                ),
                            }.Row(3),
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainerHigh"),
            },
        };
    }

    private View CreateActionButton(bool destructive) =>
        new Button
        {
            Padding = new Thickness(14, 10),
            MinimumHeightRequest = 48,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent,
        }
            .Bind(Button.TextProperty, nameof(MenuAction.Text))
            .Invoke(button =>
                button.Command = new Command(() =>
                    Complete((button.BindingContext as MenuAction)?.Text)
                )
            )
            .Invoke(button =>
            {
                if (destructive)
                    button.TextColor = Colors.Red;
                else
                    button.DynamicResource(Button.TextColorProperty, "OnSurface");
            });

    private static bool IsDestructiveAction(string action) =>
        action
            is "Delete"
                or "Leave room"
                or "Kick"
                or "Ban"
                or "Remove from room"
                or "Ban from room"
                or "Block user"
                or "Report message"
                or "Report room";
}
