using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class RoomDiscoveryPage : ContentPage
{
    private readonly ManagedMatrixClient _client;
    private readonly Func<ManagedRoom, Task> _open;
    private string _query = string.Empty;
    private string _server = string.Empty;

    public RoomDirectorySession Session { get; }

    public string Query
    {
        get => _query;
        set
        {
            _query = value;
            OnPropertyChanged();
        }
    }

    public string Server
    {
        get => _server;
        set
        {
            _server = value;
            OnPropertyChanged();
        }
    }

    public RoomDiscoveryPage(ManagedMatrixClient client, Func<ManagedRoom, Task> open)
    {
        _client = client;
        _open = open;
        Session = client.CreateRoomDirectorySession();
        Title = "Discover rooms";
        BindingContext = this;
        SafeAreaEdges = SafeAreaEdges.All;
        Content = new Grid
        {
            Padding =
                DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                    ? new Thickness(12)
                    : new Thickness(16),
            RowSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions =
                            {
                                new ColumnDefinition(GridLength.Star),
                                new ColumnDefinition(GridLength.Auto),
                            },
                            Children =
                            {
                                new SearchBar
                                {
                                    Placeholder = "Search public rooms",
                                    ReturnType = ReturnType.Search,
                                    SearchCommand = SearchCommand,
                                    IsSpellCheckEnabled = false,
                                }.Bind(SearchBar.TextProperty, nameof(Query), BindingMode.TwoWay),
                                new Button
                                {
                                    Text = "Search",
                                    MinimumHeightRequest = 44,
                                    IsVisible = DeviceInfo.Current.Idiom != DeviceIdiom.Phone,
                                }
                                    .BindCommand(nameof(SearchCommand), source: this)
                                    .Bind(
                                        IsEnabledProperty,
                                        $"{nameof(Session)}.{nameof(RoomDirectorySession.IsLoading)}",
                                        converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter()
                                    )
                                    .Column(1),
                            },
                        },
                        new Label
                        {
                            Text = "Room directory",
                            FontSize = 12,
                            Opacity = .7,
                            Margin = new Thickness(4, 4, 4, 0),
                        },
                        new Picker
                        {
                            Title = "Choose a common server",
                            ItemsSource = new[] { "matrix.org", "mozilla.org", "tchncs.de" },
                        }
                            .Bind(Picker.SelectedItemProperty, nameof(Server), BindingMode.TwoWay)
                            .Bind(
                                IsEnabledProperty,
                                $"{nameof(Session)}.{nameof(RoomDirectorySession.IsLoading)}",
                                converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter()
                            )
                            .Invoke(picker =>
                                picker.SelectedIndexChanged += (_, _) =>
                                {
                                    if (picker.SelectedItem is string server)
                                        _ = Session.SearchAsync(Query, server);
                                }
                            ),
                        new Entry
                        {
                            Placeholder = "Or enter another server (optional)",
                            Keyboard = Keyboard.Url,
                            IsSpellCheckEnabled = false,
                            IsTextPredictionEnabled = false,
                        }.Bind(Entry.TextProperty, nameof(Server), BindingMode.TwoWay),
                    },
                }.Row(0),
                new CollectionView
                {
                    SelectionMode = SelectionMode.None,
                    EmptyView = new TemplateSwitchView<bool, bool>(value => value)
                    {
                        FallbackTemplate = new Label
                        {
                            Text = "No public rooms found.",
                            Opacity = .7,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                        },
                    }
                        .Add(
                            value => value,
                            new ActivityIndicator
                            {
                                IsRunning = true,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center,
                            }
                        )
                        .Bind(
                            TemplateSwitchView<bool, bool>.ValueProperty,
                            $"{nameof(Session)}.{nameof(RoomDirectorySession.IsLoading)}",
                            source: this
                        ),
                    ItemTemplate = new DataTemplate(() =>
                        new Border
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            Padding = 12,
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                            {
                                CornerRadius = 10,
                            },
                            Content = new Grid
                            {
                                ColumnSpacing = 12,
                                RowSpacing = 8,
                                RowDefinitions =
                                {
                                    new RowDefinition(GridLength.Auto),
                                    new RowDefinition(GridLength.Auto),
                                },
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Auto),
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Auto),
                                },
                                Children =
                                {
                                    new RoomAvatar { Size = 48 }
                                        .Bind(
                                            RoomAvatar.AvatarUrlProperty,
                                            nameof(RoomDescription.AvatarUrl)
                                        )
                                        .Bind(
                                            RoomAvatar.DisplayNameProperty,
                                            nameof(RoomDescription.Name)
                                        )
                                        .Column(0),
                                    new VerticalStackLayout
                                    {
                                        Spacing = 3,
                                        Children =
                                        {
                                            new Label
                                            {
                                                FontAttributes = FontAttributes.Bold,
                                                FontSize = 16,
                                            }.Bind(
                                                Label.TextProperty,
                                                nameof(RoomDescription.Name)
                                            ),
                                            new Label
                                            {
                                                FontSize = 11,
                                                Opacity = .65,
                                                LineBreakMode = LineBreakMode.TailTruncation,
                                            }.Bind(
                                                Label.TextProperty,
                                                nameof(RoomDescription.Alias)
                                            ),
                                            new Label
                                            {
                                                MaxLines = 2,
                                                LineBreakMode = LineBreakMode.TailTruncation,
                                                Opacity = .75,
                                            }.Bind(
                                                Label.TextProperty,
                                                nameof(RoomDescription.Topic)
                                            ),
                                            new Label { FontSize = 11, Opacity = .6 }.Bind(
                                                Label.TextProperty,
                                                nameof(RoomDescription.JoinedMembers),
                                                stringFormat: "{0} members"
                                            ),
                                        },
                                    }.Column(1),
                                    new Button { Text = "Join", MinimumHeightRequest = 44 }
                                        .BindCommand(nameof(JoinCommand), source: this)
                                        .Bind(Button.CommandParameterProperty, ".")
                                        .Row(DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 1 : 0)
                                        .Column(
                                            DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 1 : 2
                                        ),
                                },
                            },
                        }
                            .Bind(
                                SemanticProperties.DescriptionProperty,
                                nameof(RoomDescription.Name),
                                stringFormat: "Public room: {0}"
                            )
                            .DynamicResource(BackgroundColorProperty, "SurfaceContainer")
                    ),
                }
                    .Bind(
                        ItemsView.ItemsSourceProperty,
                        $"{nameof(Session)}.{nameof(RoomDirectorySession.Rooms)}"
                    )
                    .Row(1),
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Button { Text = "Load more" }
                            .BindCommand(nameof(LoadMoreCommand), source: this)
                            .Bind(
                                IsEnabledProperty,
                                $"{nameof(Session)}.{nameof(RoomDirectorySession.IsLoading)}",
                                converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter()
                            )
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Session)}.{nameof(RoomDirectorySession.HasMore)}"
                            ),
                        new ActivityIndicator { IsRunning = true }.Bind(
                            IsVisibleProperty,
                            $"{nameof(Session)}.{nameof(RoomDirectorySession.IsLoading)}"
                        ),
                        new Label
                        {
                            TextColor = Colors.Red,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap,
                        }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(Session)}.{nameof(RoomDirectorySession.ErrorMessage)}"
                            )
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Session)}.{nameof(RoomDirectorySession.ErrorMessage)}",
                                converter: new CommunityToolkit.Maui.Converters.IsStringNotNullOrEmptyConverter()
                            ),
                    },
                }.Row(2),
            },
        };
        Loaded += (_, _) => _ = Session.SearchAsync(null);
        Unloaded += (_, _) => Session.Dispose();
    }

    [RelayCommand]
    private Task SearchAsync() =>
        Session.SearchAsync(Query, string.IsNullOrWhiteSpace(Server) ? null : Server.Trim());

    [RelayCommand]
    private Task LoadMoreAsync() => Session.LoadMoreAsync();

    [RelayCommand]
    private async Task JoinAsync(RoomDescription? room)
    {
        if (room is null)
            return;
        try
        {
            await _open(
                await _client.JoinRoomAsync(
                    room.Alias
                        ?? (
                            string.IsNullOrWhiteSpace(Server)
                                ? room.RoomId
                                : $"https://matrix.to/#/{Uri.EscapeDataString(room.RoomId)}?via={Uri.EscapeDataString(Server.Trim())}"
                        )
                )
            );
        }
        catch (Exception exception)
        {
            await this.DisplayAlertAsync("Could not join room", exception.Message, "OK");
        }
    }
}
