using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatEventView : ContentView
{
    [BindableProperty]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ICommand? MenuCommand { get; set; }

    public ChatEventView()
    {
        Content = new VerticalStackLayout
        {
            Margin = new Thickness(18, 4),
            Spacing = 2,
            Children =
            {
                new Label
                {
                    FontSize = 11,
                    Opacity = .7,
                    HorizontalOptions = LayoutOptions.Center,
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer()
                            .BindCommand(nameof(ToggleGroupCommand), source: this),
                    },
                }
                .Bind(Label.TextProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.EventGroupToggleText)}",
                    source: this)
                .Bind(IsVisibleProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.ShowEventGroupToggle)}",
                    source: this),

                new Label
                {
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer { Buttons = ButtonsMask.Secondary }
                            .BindCommand(nameof(MenuCommand), source: this)
                            .Bind(TapGestureRecognizer.CommandParameterProperty, nameof(Item), source: this),
                    },
                    HorizontalTextAlignment = TextAlignment.Center,
                    FontSize = 12,
                    Opacity = .8,
                    LineBreakMode = LineBreakMode.WordWrap,
                }
                .Bind(Label.TextProperty, $"{nameof(Item)}.{nameof(ChatTimelineItem.Body)}", source: this)
                .Bind(IsVisibleProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.IsEventGroupCollapsed)}",
                    converter: new Dev.Naamloos.Fennec.App.Converters.BooleanInverterConverter(), source: this),
            },
        }
        .Bind(IsVisibleProperty,
            $"{nameof(Item)}.{nameof(ChatTimelineItem.IsEventVisible)}",
            source: this);
    }

    [RelayCommand]
    private void ToggleGroup() => Item?.ToggleEventGroup();
}
