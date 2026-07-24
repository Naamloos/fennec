using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.Specialized;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomList : ContentView, IDisposable
{
    [BindableProperty]
    public partial ManagedMatrixClient MatrixClient { get; set; }

    [BindableProperty]
    public partial ManagedRoom? SelectedRoom { get; set; }

    [BindableProperty]
    public partial ObservableRoomList? ObservableRoomList { get; set; }

    public RoomList()
    {
        // Bind the MatrixClient property to the ManagedMatrixClient service
        this.BindService<ManagedMatrixClient, RoomList>(MatrixClientProperty);

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;

        this.build();
    }

    private async void OnLoaded(object sender, EventArgs e)
    {
        this.ObservableRoomList = await MatrixClient.GetObservableRoomListAsync();
        this.ObservableRoomList?.CaptureCurrentContext();
    }

    private void OnUnloaded(object sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        var oldRoomList = this.ObservableRoomList;
        this.ObservableRoomList = null;
        oldRoomList?.Dispose();
    }

    private void build()
    {
        Content = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "No rooms available.",
                        FontSize = 16,
                    }
                }
            },
            ItemTemplate = new DataTemplate(() =>
                new ContentView // TODO: Make into its own component
                {
                    HorizontalOptions = LayoutOptions.Start,
                    VerticalOptions = LayoutOptions.Center,
                    HeightRequest = 60,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Padding = new Thickness(10, 0),
                        Children =
                        {
                            new Border
                            {
                                WidthRequest = 40,
                                HeightRequest = 40,
                                StrokeShape = new RoundRectangle
                                {
                                    CornerRadius = 20,
                                },
                                StrokeThickness = 1,
                                Stroke = Colors.Gray,
                                Content = new Grid
                                {
                                    Children =
                                    {
                                        new Label
                                        {
                                            FontSize = 18,
                                            FontAttributes = FontAttributes.Bold,
                                            HorizontalTextAlignment = TextAlignment.Center,
                                            VerticalTextAlignment = TextAlignment.Center,
                                        }.Bind<Label, string?, string, string>( // Multi binding, show display name if available, otherwise show room ID
                                            Label.TextProperty,
                                            new Binding(nameof(ManagedRoom.DisplayName)),
                                            new Binding(nameof(ManagedRoom.Id)),
                                            convert: static values => (values.Item1 ?? values.Item2)?.Trim()?.Substring(0, 1) ?? "#"),

                                        new MatrixImage
                                        {
                                            Aspect = Aspect.AspectFill,
                                        }
                                        .Bind(
                                            MatrixImage.MatrixSourceProperty,
                                            nameof(ManagedRoom.AvatarUrl))
                                        .Bind(
                                            IsVisibleProperty,
                                            nameof(ManagedRoom.AvatarUrl),
                                            converter: new IsStringNotNullOrEmptyConverter()),
                                    },
                                },
                            },
                            new Label
                            {
                                FontSize = 16,
                                VerticalOptions = LayoutOptions.Center,
                            }.Bind<Label, string?, string, string>( // Multi binding, show display name if available, otherwise show room ID
                                Label.TextProperty,
                                new Binding(nameof(ManagedRoom.DisplayName)),
                                new Binding(nameof(ManagedRoom.Id)),
                                convert: static values => values.Item1 ?? values.Item2)
                        }
                    }
                }
            )
        }
        .Bind(CollectionView.SelectedItemProperty, nameof(SelectedRoom), source: this)
        .Bind(CollectionView.ItemsSourceProperty, nameof(ObservableRoomList), source: this);
    }
}
