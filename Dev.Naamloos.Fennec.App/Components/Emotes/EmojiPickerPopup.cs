using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class EmojiPickerPopup : Popup
{
    public EmojiPickerPopup(
        ChatSession session,
        EmojiPickerMode mode,
        Func<EmojiSelection, Task> selected
    )
    {
        CanBeDismissedByTappingOutsideOfPopup = true;
        Padding = 0;
        BackgroundColor = Colors.Transparent;
        var picker = new EmojiPicker
        {
            Mode = mode,
            Session = session,
            IsOpen = true,
            SelectedCommand = new AsyncRelayCommand<EmojiSelection>(async selection =>
            {
                if (selection is null)
                    return;
                await selected(selection);
                await CloseAsync();
            }),
        };
        Content = new Border
        {
            Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 8 : 12,
            MaximumWidthRequest = 420,
            HeightRequest = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 420 : 480,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Content = picker,
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }

    public EmojiPickerPopup(ChatSession session, ChatTimelineItem item)
        : this(
            session,
            EmojiPickerMode.Reaction,
            selection =>
            {
                if (selection.Kind == EmojiKind.Unicode && UnicodeEmoji.IsValid(selection.Unicode))
                    return session.ToggleReactionAsync(item, selection.Unicode!);
                return Task.CompletedTask;
            }
        ) { }
}
