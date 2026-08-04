using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class StickerPickerPopup : Popup
{
    private readonly ChatSession _session;

    public StickerPickerPopup(ChatSession session)
    {
        _session = session;
        CanBeDismissedByTappingOutsideOfPopup = true;
        Padding = 0;
        Margin = 0;
        BackgroundColor = Colors.Transparent;
        Content = new Border
        {
            Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 12 : 16,
            MaximumWidthRequest = 420,
            HeightRequest = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 420 : 480,
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
                    new Label
                    {
                        Text = "Stickers",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                    }.Row(0),
                    new CollectionView
                    {
                        SelectionMode = SelectionMode.None,
                        EmptyView = new Label
                        {
                            Text = "No stickers yet. Add custom images in Settings → Emotes.",
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment = TextAlignment.Center,
                            Opacity = .7,
                        },
                        ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical)
                        {
                            HorizontalItemSpacing = 8,
                            VerticalItemSpacing = 8,
                        },
                        ItemTemplate = new DataTemplate(() =>
                            new MatrixImage
                            {
                                IsJson = false,
                                Aspect = Aspect.AspectFit,
                                HeightRequest =
                                    DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 68 : 84,
                                GestureRecognizers =
                                {
                                    new TapGestureRecognizer()
                                        .BindCommand(nameof(PickStickerCommand), source: this)
                                        .Bind(TapGestureRecognizer.CommandParameterProperty, "."),
                                },
                            }.Bind(MatrixImage.MatrixSourceProperty, nameof(MatrixEmote.Source))
                        ),
                    }
                        .Bind(
                            ItemsView.ItemsSourceProperty,
                            nameof(ChatSession.Emotes),
                            source: session
                        )
                        .Row(1),
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }

    [RelayCommand]
    private async Task PickStickerAsync(MatrixEmote? emote)
    {
        if (emote is not null)
            await _session.SendStickerAsync(emote);
        await CloseAsync();
    }
}
