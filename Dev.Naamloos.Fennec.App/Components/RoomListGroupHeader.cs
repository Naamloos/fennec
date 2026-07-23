using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomListGroupHeader : ContentView
{
    [BindableProperty(PropertyChangedMethodName = nameof(OnGroupChanged))]
    public partial RoomListGroup? Group { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnMatrixClientChanged))]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial ImageSource? AvatarSource { get; set; }

    public RoomListGroupHeader()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new Grid
        {
            Margin = new Thickness(8, 12, 8, 4),
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Children =
            {
                new Border
                {
                    WidthRequest = 18,
                    HeightRequest = 18,
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 9 },
                    Content = new Grid
                    {
                        Children =
                        {
                            new Label
                            {
                                FontSize = 9,
                                FontAttributes = FontAttributes.Bold,
                                HorizontalTextAlignment = TextAlignment.Center,
                                VerticalTextAlignment = TextAlignment.Center,
                            }.Bind(
                                Label.TextProperty,
                                $"{nameof(Group)}.{nameof(RoomListGroup.Name)}",
                                converter: InitialConverter.Instance),
                            new Image { Aspect = Aspect.AspectFill }
                                .Bind(Image.SourceProperty, nameof(AvatarSource))
                                .Bind(
                                    IsVisibleProperty,
                                    nameof(AvatarSource),
                                    converter: new IsNotNullConverter()),
                        },
                    },
                }
                .Bind(
                    IsVisibleProperty,
                    $"{nameof(Group)}.{nameof(RoomListGroup.IsSpace)}")
                .DynamicResource(BackgroundColorProperty, "SurfaceVariant")
                .Column(0),
                new Label
                {
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalTextAlignment = TextAlignment.Center,
                }
                .Bind(
                    Label.TextProperty,
                    $"{nameof(Group)}.{nameof(RoomListGroup.Name)}")
                .DynamicResource(Label.TextColorProperty, "OnSurfaceVariant")
                .Column(1),
            },
        };
    }

    private static void OnGroupChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((RoomListGroupHeader)bindable).LoadAvatarAsync();

    private static void OnMatrixClientChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((RoomListGroupHeader)bindable).LoadAvatarAsync();

    private async Task LoadAvatarAsync()
    {
        AvatarSource = null;

        if (Group is not { AvatarUrl: { Length: > 0 } avatarUrl } group ||
            MatrixClient is null)
        {
            return;
        }

        try
        {
            var bytes = await MatrixClient.GetThumbnailAsync(
                avatarUrl,
                36,
                36,
                isJson: false);

            if (Group?.Id == group.Id &&
                Group.AvatarUrl == avatarUrl)
            {
                AvatarSource = ImageSource.FromStream(
                    () => new MemoryStream(bytes));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not load space avatar: {exception}");
        }
    }
}
