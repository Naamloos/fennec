using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class AttachmentPreviewPopup : Popup<bool>
{
    public AttachmentPreviewPopup(PickedAttachment attachment)
    {
        CanBeDismissedByTappingOutsideOfPopup = true;
        var isImage = attachment.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        Content = new Border
        {
            Padding = 20,
            MaximumWidthRequest = 420,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Send attachment", FontSize = 20, FontAttributes = FontAttributes.Bold },
                    new Image
                    {
                        Source = ImageSource.FromStream(() => new MemoryStream(attachment.Data)),
                        Aspect = Aspect.AspectFit,
                        MaximumHeightRequest = 240,
                        IsVisible = isImage,
                    },
                    new Label { Text = attachment.FileName, LineBreakMode = LineBreakMode.TailTruncation },
                    new Label { Text = attachment.MimeType, FontSize = 12, Opacity = .7 },
                    new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                        ColumnSpacing = 8,
                        Children =
                        {
                            new Button { Text = "Cancel", BackgroundColor = Colors.Transparent, Command = new Command(async () => await CloseAsync(false)) },
                            new Button { Text = "Send", Command = new Command(async () => await CloseAsync(true)) }.Column(1),
                        },
                    },
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }
}
