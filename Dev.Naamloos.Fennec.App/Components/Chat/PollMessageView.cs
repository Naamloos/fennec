using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class PollMessageView : ContentView
{
    [BindableProperty(PropertyChangedMethodName = nameof(OnItemChanged))]
    public partial ChatTimelineItem? Item { get; set; }

    [BindableProperty]
    public partial ICommand? VoteCommand { get; set; }

    public PollMessageView()
    {
        IsVisible = false;
        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = "POLL",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    Opacity = .7,
                },
                new Label { FontSize = 16, FontAttributes = FontAttributes.Bold }.Bind(
                    Label.TextProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.Poll)}.{nameof(ChatPoll.Question)}",
                    source: this
                ),
                new VerticalStackLayout { Spacing = 6 }
                    .Bind(
                        BindableLayout.ItemsSourceProperty,
                        $"{nameof(Item)}.{nameof(ChatTimelineItem.Poll)}.{nameof(ChatPoll.Answers)}",
                        source: this
                    )
                    .Invoke(layout =>
                        BindableLayout.SetItemTemplate(
                            layout,
                            new DataTemplate(() =>
                                new Button
                                {
                                    HorizontalOptions = LayoutOptions.Fill,
                                    Command = new Command<ChatPollAnswer>(Vote),
                                }
                                    .Bind(Button.TextProperty, nameof(ChatPollAnswer.DisplayText))
                                    .Bind(Button.CommandParameterProperty, ".")
                                    .Bind(
                                        Button.IsEnabledProperty,
                                        $"{nameof(Item)}.{nameof(ChatTimelineItem.Poll)}.{nameof(ChatPoll.IsClosed)}",
                                        converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                                        source: this
                                    )
                                    .DynamicResource(
                                        VisualElement.BackgroundColorProperty,
                                        "SurfaceContainerHigh"
                                    )
                                    .DynamicResource(Button.TextColorProperty, "OnSurface")
                            )
                        )
                    ),
                new Label
                {
                    Text = "Poll closed",
                    FontSize = 12,
                    Opacity = .7,
                }.Bind(
                    IsVisibleProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.Poll)}.{nameof(ChatPoll.IsClosed)}",
                    source: this
                ),
                new Label
                {
                    Text = "Your vote is recorded",
                    FontSize = 12,
                    Opacity = .7,
                }.Bind(
                    IsVisibleProperty,
                    $"{nameof(Item)}.{nameof(ChatTimelineItem.Poll)}.{nameof(ChatPoll.HasVoted)}",
                    source: this
                ),
            },
        };
    }

    private void Vote(ChatPollAnswer? answer)
    {
        if (answer is not null && Item is { } item)
        {
            VoteCommand?.Execute(new ChatPollVote(item, answer.Id));
        }
    }

    private static void OnItemChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((PollMessageView)bindable).IsVisible = newValue is ChatTimelineItem { Poll: not null };
}
