using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class AccountButton : ContentView
{
    [BindableProperty]
    public partial ImageSource? AvatarSource { get; set; }

    [BindableProperty]
    public partial string DisplayName { get; set; } = "Account";

    [BindableProperty]
    public partial string UserId { get; set; } = string.Empty;

    [BindableProperty]
    public partial string Initial { get; set; } = "@";

    [BindableProperty]
    public partial bool ShowUserId { get; set; }

    [BindableProperty]
    public partial bool TransparentBackground { get; set; }

    [BindableProperty]
    public partial ICommand? OpenCommand { get; set; }

    public AccountButton()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new Border
        {
            Padding = new Thickness(8, 6),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Triggers =
            {
                new DataTrigger(typeof(Border))
                {
                    Binding = new Binding(nameof(TransparentBackground)),
                    Value = true,
                    Setters = { new Setter { Property = BackgroundColorProperty, Value = Colors.Transparent } },
                },
            },
            Behaviors =
            {
                new TouchBehavior
                {
                    BindingContext = this,
                    ShouldMakeChildrenInputTransparent = true,
                }
                .Bind(TouchBehavior.CommandProperty, nameof(OpenCommand))
                .DynamicResource(TouchBehavior.HoveredBackgroundColorProperty, "SurfaceVariant")
                .DynamicResource(TouchBehavior.PressedBackgroundColorProperty, "SurfaceContainer"),
            },
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 30,
                        HeightRequest = 30,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 15 },
                        Content = new Grid
                        {
                            Children =
                            {
                                new Label
                                {
                                    FontAttributes = FontAttributes.Bold,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    VerticalTextAlignment = TextAlignment.Center,
                                }.Bind(Label.TextProperty, nameof(Initial)),
                                new Image
                                {
                                    Aspect = Aspect.AspectFill,
                                    WidthRequest = 30,
                                    HeightRequest = 30,
                                }.Bind(Image.SourceProperty, nameof(AvatarSource)),
                            },
                        },
                    }.DynamicResource(BackgroundColorProperty, "PrimaryContainer"),
                    new Label
                    {
                        MaximumWidthRequest = 180,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        VerticalTextAlignment = TextAlignment.Center,
                    }
                    .Bind(Label.TextProperty, nameof(DisplayName))
                    .Bind(IsVisibleProperty, nameof(ShowUserId), converter: new InvertedBoolConverter()),
                    new VerticalStackLayout
                    {
                        Spacing = 0,
                        Children =
                        {
                            new Label
                            {
                                MaximumWidthRequest = 180,
                                LineBreakMode = LineBreakMode.TailTruncation,
                                VerticalTextAlignment = TextAlignment.Center,
                            }.Bind(Label.TextProperty, nameof(DisplayName)),
                            new Label
                            {
                                FontSize = 12,
                                Opacity = .7,
                                LineBreakMode = LineBreakMode.TailTruncation,
                            }.Bind(Label.TextProperty, nameof(UserId)),
                        },
                    }.Bind(IsVisibleProperty, nameof(ShowUserId)),
                },
            },
        }
        .DynamicResource(BackgroundColorProperty, "Surface2")
        .Invoke(view => SemanticProperties.SetDescription(view, "Open user settings"));
    }
}
