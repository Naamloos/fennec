using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using CommunityToolkit.Maui.Extensions;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;
using RoomListComponent = Dev.Naamloos.Fennec.App.Components.RoomList;

namespace Dev.Naamloos.Fennec.App;

public sealed class AppShell : Shell
{
    // Services
    private readonly ManagedMatrixClient _matrixClient;

    // UI
    private readonly ShellContent _mainContent;
    private readonly ContentPage _mainPage;
    private readonly ContentView _mainHost;
    private readonly RoomListComponent _roomList;
    private readonly Chat _chatPage;

    // State
    private Room? _selectedRoom;
    private int _roomSelectionVersion;
    private bool _disposed;

    public Room? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            if (_selectedRoom?.Id() == value?.Id())
            {
                return;
            }

            _selectedRoom = value;
            OnPropertyChanged();

            if (value is not null)
            {
                _ = ShowRoomAsync(value);
            }
        }
    }

    public AppShell(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        BindingContext = this;

        _chatPage = new Chat(matrixClient);
        _roomList = CreateRoomList();

        _mainHost = new ContentView
        {
            Content = CreateEmptyRoomView(),
        };

        _mainPage = new ContentPage
        {
            Title = "Fennec",
            Content = _mainHost,
        };

        _mainContent = CreateMainContent();

        ConfigureShell();
        Build();

        Unloaded += OnUnloaded;
    }

    private void ConfigureShell()
    {
        SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "Surface");

        SetDynamicResource(
            FlyoutBackgroundColorProperty,
            "Surface");

        SetDynamicResource(
            Shell.BackgroundColorProperty,
            "Surface");

        SetDynamicResource(
            Shell.ForegroundColorProperty,
            "OnSurface");

        SetDynamicResource(
            Shell.TitleColorProperty,
            "OnSurface");

        SetDynamicResource(
            Shell.DisabledColorProperty,
            "Outline");

        SetDynamicResource(
            Shell.UnselectedColorProperty,
            "OnSurfaceVariant");

        SetDynamicResource(
            Shell.TabBarBackgroundColorProperty,
            "Surface2");

        SetDynamicResource(
            Shell.TabBarForegroundColorProperty,
            "Primary");

        SetDynamicResource(
            Shell.TabBarTitleColorProperty,
            "Primary");

        SetDynamicResource(
            Shell.TabBarUnselectedColorProperty,
            "OnSurfaceVariant");

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutWidth = 320;
    }

    private void Build()
    {
        FlyoutContent = CreateFlyoutContent();

        var mainItem = new FlyoutItem
        {
            Title = "Fennec",
            Route = "main",
            FlyoutItemIsVisible = false,
            Items =
            {
                _mainContent,
            },
        };

        Items.Add(mainItem);
        CurrentItem = mainItem;
    }

    private ShellContent CreateMainContent()
    {
        return new ShellContent
        {
            Title = "Fennec",
            Route = "chat",
            Content = _mainPage,
        };
    }

    private RoomListComponent CreateRoomList()
    {
        var roomList = new RoomListComponent(_matrixClient);

        roomList.SetBinding(
            RoomListComponent.SelectedRoomProperty,
            new Binding(
                nameof(SelectedRoom),
                source: this,
                mode: BindingMode.TwoWay));

        return roomList;
    }

    private View CreateFlyoutContent()
    {
        var layout = new Grid
        {
            Padding = 16,
            RowSpacing = 16,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };

        layout.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "Surface");

        layout.Add(CreateHeader(), row: 0);
        layout.Add(_roomList, row: 1);
        layout.Add(CreateFooter(), row: 2);

        return layout;
    }

    private View CreateHeader()
    {
        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = "Fennec",
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                },
                new Label
                {
                    Text = _matrixClient.IsLoggedIn
                        ? "Signed in"
                        : "Not signed in",
                    FontSize = 12,
                    Opacity = 0.7,
                    LineBreakMode = LineBreakMode.TailTruncation,
                },
            },
        };
    }

    private View CreateFooter()
    {
        return new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Button
                {
                    Text = "Verify this session",
                    HorizontalOptions = LayoutOptions.Fill,
                    Command = new Command(OpenVerification),
                },
                new Button
                {
                    Text = "Settings",
                    HorizontalOptions = LayoutOptions.Fill,
                    Command = new Command(OpenSettings),
                },
            },
        };
    }

    private async void OpenVerification()
    {
        try
        {
            var controller = await _matrixClient
                .GetSessionVerificationControllerAsync();
            await _mainPage.ShowPopupAsync(
                new VerificationPopup(controller));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not start verification: {exception}");
        }
    }

    private static View CreateEmptyRoomView()
    {
        return new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 12,
            Children =
            {
                new Border
                {
                    WidthRequest = 64,
                    HeightRequest = 64,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle
                    {
                        CornerRadius = 32,
                    },
                    Content = new Label
                    {
                        Text = "#",
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    },
                },
                new Label
                {
                    Text = "Select a room",
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new Label
                {
                    Text = "Choose a room from the sidebar to start chatting.",
                    Opacity = 0.7,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    private static View CreateRoomErrorView(Exception exception)
    {
        return new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Could not open room",
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new Label
                {
                    Text = exception.Message,
                    TextColor = Colors.Red,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    private async Task ShowRoomAsync(Room room)
    {
        if (_disposed)
        {
            return;
        }

        var selectionVersion =
            Interlocked.Increment(ref _roomSelectionVersion);

        FlyoutIsPresented = false;

        try
        {
            await _chatPage.SetRoomAsync(room);

            if (_disposed ||
                selectionVersion != _roomSelectionVersion ||
                SelectedRoom?.Id() != room.Id())
            {
                return;
            }

            var roomName = room.DisplayName() ?? room.Id();

            _mainContent.Title = roomName;
            _mainPage.Title = roomName;

            // Swap the content inside the already-displayed page.
            _mainHost.Content = _chatPage.Content;
        }
        catch (Exception exception)
        {
            if (_disposed ||
                selectionVersion != _roomSelectionVersion)
            {
                return;
            }

            Debug.WriteLine(
                $"Failed to open room {room.Id()}: {exception}");

            var roomName = room.DisplayName() ?? room.Id();

            _mainContent.Title = roomName;
            _mainPage.Title = roomName;
            _mainHost.Content = CreateRoomErrorView(exception);
        }
    }

    private static void OpenSettings()
    {
        Debug.WriteLine("Settings clicked.");
    }

    private void OnUnloaded(
        object? sender,
        EventArgs e)
    {
        Dispose();
    }

    private void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Interlocked.Increment(ref _roomSelectionVersion);

        Unloaded -= OnUnloaded;

        _roomList.Dispose();
        _chatPage.Dispose();

        _mainHost.Content = null;
        _selectedRoom = null;
    }
}
