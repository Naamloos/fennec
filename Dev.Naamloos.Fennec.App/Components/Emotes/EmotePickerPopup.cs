using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class EmotePickerPopup : Popup
{
    private readonly ChatSession _session;
    private readonly ChatTimelineItem _item;

    public EmotePickerPopup(ChatSession session, ChatTimelineItem item)
    {
        _session = session;
        _item = item;
        CanBeDismissedByTappingOutsideOfPopup = true;
        Padding = 0;
        Margin = 0;
        BackgroundColor = Colors.Transparent;

        Content = new Border
        {
            Padding = 16,
            MaximumWidthRequest = 420,
            HeightRequest = 480,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = new EmotePicker
            {
                Emotes = session.Emotes,
                PickCommand = new AsyncRelayCommand<string>(PickAsync),
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }

    private async Task PickAsync(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            await _session.ToggleReactionAsync(_item, key);
        }

        await CloseAsync();
    }
}
