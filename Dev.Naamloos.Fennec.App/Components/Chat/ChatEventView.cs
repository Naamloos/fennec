using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatEventView : ContentView
{
    [BindableProperty]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ICommand? MenuCommand { get; set; }

    public ChatEventView()
    {
        Content = new TemplateSwitchView<bool, bool>(value => value)
        {
            FallbackTemplate = new Border
            {
                Margin = new Thickness(12, 3),
                HorizontalOptions = LayoutOptions.Center,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = 10,
                },
                GestureRecognizers =
                {
                    new TapGestureRecognizer { Buttons = ButtonsMask.Secondary }
                        .BindCommand(nameof(MenuCommand), source: this)
                        .Bind(
                            TapGestureRecognizer.CommandParameterProperty,
                            nameof(Item),
                            source: this
                        ),
                },
                Behaviors =
                {
                    new TouchBehavior
                    {
                        LongPressDuration = 500,
                        ShouldMakeChildrenInputTransparent = false,
                    }
                        .Bind(TouchBehavior.LongPressCommandProperty, nameof(MenuCommand), source: this)
                        .Bind(
                            TouchBehavior.LongPressCommandParameterProperty,
                            nameof(Item),
                            source: this
                        ),
                },
                Content = new Grid
                {
                    Children =
                    {
                        new BoxView { CornerRadius = 10, Opacity = .25 }
                            .DynamicResource(BoxView.ColorProperty, "SurfaceContainer"),
                        new Label
                        {
                            Padding = new Thickness(10, 6),
                            HorizontalTextAlignment = TextAlignment.Center,
                            FontSize = 12,
                            LineBreakMode = LineBreakMode.WordWrap,
                        }.Bind(
                            Label.TextProperty,
                            $"{nameof(Item)}.{nameof(ChatTimelineItem.Body)}",
                            source: this
                        ),
                    },
                },
            },
        }
            .Add(
                value => value,
                new Grid
                {
                    Margin = new Thickness(12, 8),
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                    },
                    Children =
                    {
                        new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center }
                            .DynamicResource(BoxView.ColorProperty, "Primary"),
                        new Label
                        {
                            Text = "Unread",
                            FontSize = 11,
                            FontAttributes = FontAttributes.Bold,
                        }
                            .DynamicResource(Label.TextColorProperty, "Primary")
                            .Column(1),
                        new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center }
                            .DynamicResource(BoxView.ColorProperty, "Primary")
                            .Column(2),
                    },
                }
            )
            .Bind(
                TemplateSwitchView<bool, bool>.ValueProperty,
                $"{nameof(Item)}.{nameof(ChatTimelineItem.IsReadMarker)}",
                source: this
            );
    }
}
