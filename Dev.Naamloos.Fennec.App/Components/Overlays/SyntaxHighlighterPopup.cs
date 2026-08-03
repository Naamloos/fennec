using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using MauiIcons.Core;
using MauiIcons.Material;

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
            Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 8 : 12,
            MaximumWidthRequest = 900,
            MaximumHeightRequest = 800,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children =
                {
                    new MauiIcon
                    {
                        Icon = MaterialIcons.Close,
                        IconSize = 24,
                        WidthRequest = 44,
                        HeightRequest = 44,
                        HorizontalOptions = LayoutOptions.End,
                        GestureRecognizers =
                        {
                            new TapGestureRecognizer
                            {
                                Command = new AsyncRelayCommand(CloseAsync),
                            },
                        },
                    }
                        .Invoke(view =>
                            SemanticProperties.SetDescription(view, "Close source view")
                        )
                        .Row(0),
                    new ScrollView { Content = new SyntaxHighlighter { Text = source } }.Row(1),
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}
