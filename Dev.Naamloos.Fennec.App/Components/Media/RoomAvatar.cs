using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomAvatar : ContentView
{
    [BindableProperty]
    public partial string? AvatarUrl { get; set; }

    [BindableProperty]
    public partial string? DisplayName { get; set; }

    [BindableProperty]
    public partial double Size { get; set; } = 40d;

    public RoomAvatar()
    {
        Content = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 999 },
            Content = new Grid
            {
                Children =
                {
                    new Label
                    {
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    }.Bind(
                        Label.TextProperty,
                        nameof(DisplayName),
                        converter: new SubstringConverter(1, 0),
                        source: this
                    ),
                    new MatrixImage { Aspect = Aspect.AspectFill }
                        .Bind(MatrixImage.MatrixSourceProperty, nameof(AvatarUrl), source: this)
                        .Bind(
                            IsVisibleProperty,
                            nameof(AvatarUrl),
                            converter: new IsStringNotNullOrEmptyConverter(),
                            source: this
                        ),
                },
            },
        }
            .Bind(WidthRequestProperty, nameof(Size), source: this)
            .Bind(HeightRequestProperty, nameof(Size), source: this)
            .DynamicResource(VisualElement.BackgroundColorProperty, "PrimaryContainer");
    }
}
