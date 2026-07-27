using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MessageReactionsView : ContentView
{
    [BindableProperty]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ICommand? AddReactionCommand { get; set; }

    public MessageReactionsView()
    {
        Content = new HorizontalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Button
                {
                    Text = "+",
                    Padding = new Thickness(6, 0),
                    HeightRequest = 28,
                    MinimumHeightRequest = 28,
                }
                .BindCommand(nameof(AddReactionCommand), source: this)
                .Bind(Button.CommandParameterProperty, nameof(Item), source: this),
            },
        }
        .Bind(BindableLayout.ItemsSourceProperty,
            $"{nameof(Item)}.{nameof(ChatTimelineItem.Reactions)}",
            source: this)
        .Invoke(layout => BindableLayout.SetItemTemplate(
            layout,
            new DataTemplate(() => new Border
            {
                Padding = new Thickness(6, 2),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = 12,
                },
                Content = new HorizontalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label().Bind(Label.TextProperty, nameof(ChatReaction.Key)),
                        new Label { FontSize = 11 }
                            .Bind(Label.TextProperty, nameof(ChatReaction.Count)),
                    },
                },
            }.DynamicResource(BackgroundColorProperty, "SecondaryContainer"))));
    }
}
