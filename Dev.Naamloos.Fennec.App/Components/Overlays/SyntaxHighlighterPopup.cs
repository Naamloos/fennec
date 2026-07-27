using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class SyntaxHighlighterPopup : Popup
{
    public SyntaxHighlighterPopup(string source)
    {
        CanBeDismissedByTappingOutsideOfPopup = true;
        Padding = 0;
        Margin = 0;
        BackgroundColor = Colors.Transparent;

        Content = new Border
        {
            Padding = 12,
            MaximumWidthRequest = 900,
            MaximumHeightRequest = 800,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 12,
            },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children =
                {
                    new Button
                    {
                        Text = "×",
                        FontSize = 24,
                        Padding = 0,
                        WidthRequest = 40,
                        HeightRequest = 40,
                        HorizontalOptions = LayoutOptions.End,
                        Command = new AsyncRelayCommand(CloseAsync),
                    }
                    .Row(0),
                    new ScrollView
                    {
                        Content = new SyntaxHighlighter { Text = source },
                    }
                    .Row(1),
                },
            },
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}
