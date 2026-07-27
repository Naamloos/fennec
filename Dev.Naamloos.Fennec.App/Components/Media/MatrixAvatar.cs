using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MatrixAvatar : ContentView
{
    [BindableProperty]
    public partial string? MatrixSource { get; set; }

    [BindableProperty]
    public partial double Size { get; set; } = 36d;

    public MatrixAvatar()
    {
        Content = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 999,
            },
            Content = new MatrixImage
            {
                Aspect = Aspect.AspectFill,
            }
            .Bind(MatrixImage.MatrixSourceProperty, nameof(MatrixSource), source: this),
        }
        .Bind(WidthRequestProperty, nameof(Size), source: this)
        .Bind(HeightRequestProperty, nameof(Size), source: this);
    }
}
