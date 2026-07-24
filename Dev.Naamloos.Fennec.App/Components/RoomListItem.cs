using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.ComponentModel;
using System.Diagnostics;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomListItem : ContentView
{
    [BindableProperty(PropertyChangedMethodName = nameof(OnEntryChanged))]
    public partial RoomEntry? Entry { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnMatrixClientChanged))]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial ImageSource? AvatarSource { get; set; }

    public RoomListItem()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new Border
        {
            Margin = new Thickness(0, 2),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new Grid
            {
                Padding = new Thickness(8, 6),
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children =
                {
                    new Border
                    {
                        WidthRequest = 36,
                        HeightRequest = 36,
                        StrokeShape = new RoundRectangle { CornerRadius = 18 },
                        Content = new Grid
                        {
                            Children =
                            {
                                new Label
                                {
                                    FontSize = 16,
                                    FontAttributes = FontAttributes.Bold,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    VerticalTextAlignment = TextAlignment.Center,
                                }.Bind(
                                    Label.TextProperty,
                                    $"{nameof(Entry)}.{nameof(RoomEntry.Name)}",
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
                    .DynamicResource(BackgroundColorProperty, "SurfaceVariant")
                    .Column(0),
                    new Label
                    {
                        FontSize = 15,
                        LineBreakMode = LineBreakMode.TailTruncation,
                        VerticalTextAlignment = TextAlignment.Center,
                    }
                    .Bind(Label.TextProperty, $"{nameof(Entry)}.{nameof(RoomEntry.Name)}")
                    .Column(1),
                    new Border
                    {
                        WidthRequest = 8,
                        HeightRequest = 8,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 4 },
                        VerticalOptions = LayoutOptions.Center,
                    }
                    .Bind(
                        IsVisibleProperty,
                        $"{nameof(Entry)}.{nameof(RoomEntry.HasUnread)}")
                    .DynamicResource(BackgroundColorProperty, "Primary")
                    .Column(2),
                },
            },
        };
    }

    private static void OnEntryChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var view = (RoomListItem)bindable;

        if (oldValue is RoomEntry oldEntry)
        {
            oldEntry.PropertyChanged -= view.OnEntryPropertyChanged;
        }

        if (newValue is RoomEntry newEntry)
        {
            newEntry.PropertyChanged += view.OnEntryPropertyChanged;
        }

        _ = view.LoadAvatarAsync();
    }

    private static void OnMatrixClientChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((RoomListItem)bindable).LoadAvatarAsync();

    private void OnEntryPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoomEntry.AvatarUrl))
        {
            _ = LoadAvatarAsync();
        }
    }

    private async Task LoadAvatarAsync()
    {
        AvatarSource = null;

        if (Entry is not { AvatarUrl: { Length: > 0 } avatarUrl } entry ||
            MatrixClient is null)
        {
            return;
        }

        try
        {
            var bytes = await MatrixClient.GetThumbnailAsync(
                avatarUrl,
                72,
                72,
                isJson: false);

            if (Entry?.Id == entry.Id &&
                Entry.AvatarUrl == avatarUrl)
            {
                AvatarSource = ImageSource.FromStream(
                    () => new MemoryStream(bytes));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not load room avatar: {exception}");
        }
    }
}
