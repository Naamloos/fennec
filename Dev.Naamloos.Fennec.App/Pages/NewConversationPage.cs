using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class NewConversationPage : ContentPage
{
    private readonly ManagedMatrixClient _client;
    private readonly Func<ManagedRoom, Task> _open;
    private string _userId = string.Empty;
    private string _roomAddress = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isWorking;

    public string UserId
    {
        get => _userId;
        set
        {
            if (_userId == value) return;
            _userId = value;
            OnPropertyChanged();
            ErrorMessage = string.Empty;
        }
    }

    public string RoomAddress
    {
        get => _roomAddress;
        set
        {
            if (_roomAddress == value) return;
            _roomAddress = value;
            OnPropertyChanged();
            ErrorMessage = string.Empty;
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set
        {
            if (_isWorking == value) return;
            _isWorking = value;
            OnPropertyChanged();
        }
    }

    public NewConversationPage(ManagedMatrixClient client, Func<ManagedRoom, Task> open)
    {
        _client = client;
        _open = open;
        Title = "New conversation";
        BindingContext = this;
        SafeAreaEdges = SafeAreaEdges.All;

        Content = new Grid
        {
            Children =
            {
                new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? new Thickness(12)
                            : new Thickness(20),
                        Spacing = 16,
                        MaximumWidthRequest = 620,
                        HorizontalOptions = LayoutOptions.Fill,
                        Children =
                        {
                            new Label
                            {
                                Text = "Start a conversation",
                                FontSize = 26,
                                FontAttributes = FontAttributes.Bold,
                            },
                            new Label
                            {
                                Text = "Message someone directly, join a known room, or browse public rooms.",
                                Opacity = .72,
                            },
                            new Border
                            {
                                Padding = 18,
                                StrokeThickness = 0,
                                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                                {
                                    CornerRadius = 16,
                                },
                                Content = new VerticalStackLayout
                                {
                                    Spacing = 10,
                                    Children =
                                    {
                                        new Label
                                        {
                                            Text = "Direct message",
                                            FontSize = 18,
                                            FontAttributes = FontAttributes.Bold,
                                        },
                                        new Label
                                        {
                                            Text = "Enter the complete Matrix ID of the person you want to message.",
                                            Opacity = .7,
                                        },
                                        new Entry
                                        {
                                            Placeholder = "@alice:example.org",
                                            Keyboard = Keyboard.Email,
                                            ReturnType = ReturnType.Go,
                                            IsSpellCheckEnabled = false,
                                            IsTextPredictionEnabled = false,
                                        }
                                            .Bind(
                                                Entry.TextProperty,
                                                nameof(UserId),
                                                BindingMode.TwoWay
                                            )
                                            .Bind(
                                                Entry.ReturnCommandProperty,
                                                nameof(StartDirectMessageCommand),
                                                source: this
                                            ),
                                        new Button
                                        {
                                            Text = "Start direct message",
                                            HorizontalOptions = DeviceInfo.Current.Idiom
                                                == DeviceIdiom.Phone
                                                ? LayoutOptions.Fill
                                                : LayoutOptions.End,
                                            MinimumHeightRequest = 44,
                                        }.BindCommand(nameof(StartDirectMessageCommand), source: this),
                                    },
                                },
                            }.DynamicResource(BackgroundColorProperty, "SurfaceContainer"),
                            new Border
                            {
                                Padding = 18,
                                StrokeThickness = 0,
                                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                                {
                                    CornerRadius = 16,
                                },
                                Content = new VerticalStackLayout
                                {
                                    Spacing = 10,
                                    Children =
                                    {
                                        new Label
                                        {
                                            Text = "Join a room",
                                            FontSize = 18,
                                            FontAttributes = FontAttributes.Bold,
                                        },
                                        new Label
                                        {
                                            Text = "Paste a room alias, room ID, or matrix.to link.",
                                            Opacity = .7,
                                        },
                                        new Entry
                                        {
                                            Placeholder = "#community:example.org",
                                            Keyboard = Keyboard.Url,
                                            ReturnType = ReturnType.Go,
                                            IsSpellCheckEnabled = false,
                                            IsTextPredictionEnabled = false,
                                        }.Bind(
                                            Entry.TextProperty,
                                            nameof(RoomAddress),
                                            BindingMode.TwoWay
                                        )
                                            .Bind(
                                                Entry.ReturnCommandProperty,
                                                nameof(JoinRoomCommand),
                                                source: this
                                            ),
                                        new Button
                                        {
                                            Text = "Join room",
                                            HorizontalOptions = DeviceInfo.Current.Idiom
                                                == DeviceIdiom.Phone
                                                ? LayoutOptions.Fill
                                                : LayoutOptions.End,
                                            MinimumHeightRequest = 44,
                                        }.BindCommand(nameof(JoinRoomCommand), source: this),
                                    },
                                },
                            }.DynamicResource(BackgroundColorProperty, "SurfaceContainer"),
                            new Border
                            {
                                Padding = 18,
                                StrokeThickness = 0,
                                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                                {
                                    CornerRadius = 16,
                                },
                                Content = new VerticalStackLayout
                                {
                                    Spacing = 10,
                                    Children =
                                    {
                                        new VerticalStackLayout
                                        {
                                            Spacing = 6,
                                            Children =
                                            {
                                                new Label
                                                {
                                                    Text = "Discover public rooms",
                                                    FontSize = 18,
                                                    FontAttributes = FontAttributes.Bold,
                                                },
                                                new Label
                                                {
                                                    Text = "Search public room directories by name or topic.",
                                                    Opacity = .7,
                                                },
                                            },
                                        },
                                        new Button
                                        {
                                            Text = "Browse rooms",
                                            MinimumHeightRequest = 44,
                                            HorizontalOptions = DeviceInfo.Current.Idiom
                                                == DeviceIdiom.Phone
                                                ? LayoutOptions.Fill
                                                : LayoutOptions.End,
                                        }.BindCommand(nameof(DiscoverRoomsCommand), source: this),
                                    },
                                },
                            }.DynamicResource(BackgroundColorProperty, "SurfaceContainer"),
                            new Label
                            {
                                TextColor = Colors.Red,
                                HorizontalTextAlignment = TextAlignment.Center,
                            }
                                .Bind(Label.TextProperty, nameof(ErrorMessage), source: this)
                                .Bind(
                                    IsVisibleProperty,
                                    nameof(ErrorMessage),
                                    converter: new IsStringNotNullOrEmptyConverter(),
                                    source: this
                                ),
                        },
                    },
                }.Bind(
                    IsEnabledProperty,
                    nameof(IsWorking),
                    converter: new InvertedBoolConverter(),
                    source: this
                ),
                new ActivityIndicator
                {
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }
                    .Bind(ActivityIndicator.IsRunningProperty, nameof(IsWorking), source: this)
                    .Bind(IsVisibleProperty, nameof(IsWorking), source: this),
            },
        };
    }

    [RelayCommand]
    private Task StartDirectMessageAsync()
    {
        var userId = UserId.Trim();
        if (!userId.StartsWith('@') || !userId.Contains(':'))
        {
            ErrorMessage = "Enter a complete Matrix user ID, such as @alice:example.org.";
            return Task.CompletedTask;
        }

        return OpenAsync(() => _client.CreateDirectMessageAsync(userId));
    }

    [RelayCommand]
    private Task JoinRoomAsync()
    {
        var roomAddress = RoomAddress.Trim();
        if (string.IsNullOrWhiteSpace(roomAddress))
        {
            ErrorMessage = "Enter a room alias, room ID, or matrix.to link.";
            return Task.CompletedTask;
        }

        return OpenAsync(() => _client.JoinRoomAsync(roomAddress));
    }

    [RelayCommand]
    private Task DiscoverRoomsAsync() =>
        Navigation.PushAsync(new RoomDiscoveryPage(_client, OpenAndCloseAsync));

    private async Task OpenAsync(Func<Task<ManagedRoom>> open)
    {
        if (IsWorking) return;

        IsWorking = true;
        ErrorMessage = string.Empty;
        try
        {
            await OpenAndCloseAsync(await open());
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }

    private async Task OpenAndCloseAsync(ManagedRoom room)
    {
        await _open(room);
        await Navigation.PopToRootAsync();
    }
}
