using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MatrixAvatar : ContentView
{
    [BindableProperty]
    public partial string? MatrixSource { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnDisplayNameChanged))]
    public partial string? DisplayName { get; set; }

    [BindableProperty]
    public partial double Size { get; set; } = 36d;

    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[..1].ToUpperInvariant();

    public MatrixAvatar()
    {
        Content = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 },
            Content = new Grid
            {
                Children =
                {
                    new Label
                    {
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    }.Bind(Label.TextProperty, nameof(Initial), source: this),
                    new MatrixImage { Aspect = Aspect.AspectFill, UseAvatarCache = true }.Bind(
                        MatrixImage.MatrixSourceProperty,
                        nameof(MatrixSource),
                        source: this
                    ),
                },
            },
        }
            .Bind(WidthRequestProperty, nameof(Size), source: this)
            .Bind(HeightRequestProperty, nameof(Size), source: this)
            .DynamicResource(VisualElement.BackgroundColorProperty, "PrimaryContainer");
    }

    private static void OnDisplayNameChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((MatrixAvatar)bindable).OnPropertyChanged(nameof(Initial));
}
