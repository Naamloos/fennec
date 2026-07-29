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
        Content = new Label
        {
            Margin = new Thickness(18, 4),
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
            HorizontalTextAlignment = TextAlignment.Center,
            FontSize = 12,
            Opacity = .8,
            LineBreakMode = LineBreakMode.WordWrap,
        }.Bind(Label.TextProperty, $"{nameof(Item)}.{nameof(ChatTimelineItem.Body)}", source: this);
    }
}
