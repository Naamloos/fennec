using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;
using RoomListComponent = Dev.Naamloos.Fennec.App.Components.RoomList;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;

namespace Dev.Naamloos.Fennec.App;

public sealed partial class AppShell : Shell
{
    // State
    private Room? _selectedRoom;
    private bool _disposed;
    private ImageSource? _accountAvatarSource;
    private string _accountDisplayName = "Account";
    private string _accountUserId = string.Empty;
    private string _accountInitial = "@";
    private string _roomErrorMessage = string.Empty;

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
            OnPropertyChanged(nameof(ShowChat));

            if (value is not null)
            {
                ShowRoom(value);
            }
        }
    }

    public ImageSource? AccountAvatarSource => _accountAvatarSource;
    public string AccountDisplayName => _accountDisplayName;
    public string AccountUserId => _accountUserId;
    public string AccountInitial => _accountInitial;
    [BindableProperty(PropertyChangedMethodName = nameof(OnMatrixClientChanged))]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial AppNavigationService? AppNavigation { get; set; }

    public string SessionStatus =>
        MatrixClient?.IsLoggedIn == true
            ? "Signed in"
            : "Not signed in";
    public bool ShowChat =>
        SelectedRoom is not null &&
        string.IsNullOrEmpty(RoomErrorMessage);

    public string RoomErrorMessage
    {
        get => _roomErrorMessage;
        private set
        {
            if (_roomErrorMessage == value)
            {
                return;
            }

            _roomErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowChat));
        }
    }

    public AppShell()
    {
        BindingContext = this;

        ConfigureShell();
        Build();

        Behaviors.Add(
            new EventToCommandBehavior
            {
                BindingContext = this,
                EventName = nameof(Loaded),
            }.Bind(
                EventToCommandBehavior.CommandProperty,
                nameof(LoadCommand)));
        Behaviors.Add(
            new EventToCommandBehavior
            {
                BindingContext = this,
                EventName = nameof(Unloaded),
            }.Bind(
                EventToCommandBehavior.CommandProperty,
                nameof(UnloadCommand)));
    }

    private static void OnMatrixClientChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var shell = (AppShell)bindable;

        if (oldValue is ManagedMatrixClient oldClient)
        {
            oldClient.SessionInvalidated -=
                shell.OnSessionInvalidated;
        }

        if (newValue is ManagedMatrixClient newClient)
        {
            newClient.SessionInvalidated +=
                shell.OnSessionInvalidated;
        }

        shell.OnPropertyChanged(nameof(SessionStatus));
    }

    private void ConfigureShell()
    {
        this
            .DynamicResource(
                VisualElement.BackgroundColorProperty,
                "Surface")
            .DynamicResource(
                FlyoutBackgroundColorProperty,
                "Surface")
            .DynamicResource(
                Shell.BackgroundColorProperty,
                "Surface")
            .DynamicResource(
                Shell.ForegroundColorProperty,
                "OnSurface")
            .DynamicResource(
                Shell.TitleColorProperty,
                "OnSurface")
            .DynamicResource(
                Shell.DisabledColorProperty,
                "Outline")
            .DynamicResource(
                Shell.UnselectedColorProperty,
                "OnSurfaceVariant")
            .DynamicResource(
                Shell.TabBarBackgroundColorProperty,
                "Surface2")
            .DynamicResource(
                Shell.TabBarForegroundColorProperty,
                "Primary")
            .DynamicResource(
                Shell.TabBarTitleColorProperty,
                "Primary")
            .DynamicResource(
                Shell.TabBarUnselectedColorProperty,
                "OnSurfaceVariant");

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutWidth = 320;
    }

    private void Build()
    {
        FlyoutContent = new Grid
        {
            Padding = 16,
            RowSpacing = 16,
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
                    Spacing = 2,
                    Children =
                    {
#if !WINDOWS && !MACCATALYST
                        new AccountButton
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            ShowUserId = true,
                        }
                        .Bind(
                            AccountButton.AvatarSourceProperty,
                            nameof(AccountAvatarSource),
                            source: BindingContext)
                        .Bind(
                            AccountButton.DisplayNameProperty,
                            nameof(AccountDisplayName),
                            source: BindingContext)
                        .Bind(
                            AccountButton.UserIdProperty,
                            nameof(AccountUserId),
                            source: BindingContext)
                        .Bind(
                            AccountButton.InitialProperty,
                            nameof(AccountInitial),
                            source: BindingContext)
                        .Bind(
                            AccountButton.OpenCommandProperty,
                            nameof(ShowUserSettingsCommand),
                            source: BindingContext),
#endif
                        new Label
                        {
                            Text = "Fennec",
                            FontSize = 26,
                            FontAttributes = FontAttributes.Bold,
                        },
                        new Label
                        {
                            FontSize = 12,
                            Opacity = .7,
                            LineBreakMode = LineBreakMode.TailTruncation,
                        }.Bind(
                            Label.TextProperty,
                            nameof(SessionStatus)),
                    },
                }.Row(0),
                new RoomListComponent()
                    .Bind(
                        RoomListComponent.MatrixClientProperty,
                        nameof(MatrixClient),
                        source: BindingContext)
                    .Bind(
                        RoomListComponent.SelectedRoomProperty,
                        nameof(SelectedRoom),
                        BindingMode.TwoWay,
                        source: BindingContext)
                    .Row(1),
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Button
                        {
                            Text = "Verify this session",
                            HorizontalOptions = LayoutOptions.Fill,
                        }.BindCommand(
                            nameof(OpenVerificationCommand)),
                        new Button
                        {
                            Text = "Settings",
                            HorizontalOptions = LayoutOptions.Fill,
                        }.BindCommand(
                            nameof(OpenSettingsCommand)),
                    },
                }.Row(2),
            },
        }.DynamicResource(
            VisualElement.BackgroundColorProperty,
            "Surface");

        Items.Add(new FlyoutItem
        {
            Title = "Fennec",
            Route = "main",
            FlyoutItemIsVisible = false,
            Items =
            {
                new ShellContent
                {
                    Title = "Fennec",
                    Route = "chat",
                    Content = new ContentPage
                    {
                        Title = "Fennec",
                        Content = new Grid
                        {
                            Children =
                            {
                                new Chat()
                                    .Bind(
                                        Chat.MatrixClientProperty,
                                        nameof(MatrixClient),
                                        source: BindingContext)
                                    .Bind(
                                        IsVisibleProperty,
                                        nameof(ShowChat),
                                        source: BindingContext)
                                    .Bind(
                                        Chat.SelectedRoomProperty,
                                        nameof(SelectedRoom),
                                        source: BindingContext)
                                    .Bind(
                                        Chat.RoomLoadErrorProperty,
                                        nameof(RoomErrorMessage),
                                        BindingMode.TwoWay,
                                        source: BindingContext),
                                new VerticalStackLayout
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
                                            Opacity = .7,
                                            HorizontalTextAlignment = TextAlignment.Center,
                                        },
                                    },
                                }.Bind(
                                    IsVisibleProperty,
                                    nameof(SelectedRoom),
                                    converter: new IsNullConverter(),
                                    source: BindingContext),
                                new VerticalStackLayout
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
                                            TextColor = Colors.Red,
                                            HorizontalTextAlignment = TextAlignment.Center,
                                        }.Bind(
                                            Label.TextProperty,
                                            nameof(RoomErrorMessage),
                                            source: BindingContext),
                                    },
                                }.Bind(
                                    IsVisibleProperty,
                                    nameof(RoomErrorMessage),
                                    converter: new IsStringNotNullOrEmptyConverter(),
                                    source: BindingContext),
                            },
                        },
                    },
                },
            },
        });
        CurrentItem = Items.Single();
    }

    private void ConfigureTitleBar()
    {
        if (Window is null)
        {
            return;
        }

        Window.TitleBar = new TitleBar
        {
            Title = "Fennec",
            HeightRequest = 48,
            TrailingContent = new AccountButton
            {
                Margin = new Thickness(0, 0, 8, 0),
                TransparentBackground = true,
            }
            .Bind(
                AccountButton.AvatarSourceProperty,
                nameof(AccountAvatarSource),
                source: BindingContext)
            .Bind(
                AccountButton.DisplayNameProperty,
                nameof(AccountDisplayName),
                source: BindingContext)
            .Bind(
                AccountButton.UserIdProperty,
                nameof(AccountUserId),
                source: BindingContext)
            .Bind(
                AccountButton.InitialProperty,
                nameof(AccountInitial),
                source: BindingContext)
            .Bind(
                AccountButton.OpenCommandProperty,
                nameof(ShowUserSettingsCommand),
                source: BindingContext),
        };
    }

    private async Task LoadOwnAvatarAsync()
    {
        try
        {
            if (MatrixClient is null)
            {
                return;
            }

            var profile = await MatrixClient.GetOwnProfileAsync();
            _accountDisplayName = profile.DisplayName ?? profile.UserId;
            _accountUserId = profile.UserId;
            _accountInitial = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? "@" : profile.DisplayName[..1].ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                OnPropertyChanged(nameof(AccountDisplayName));
                OnPropertyChanged(nameof(AccountUserId));
                OnPropertyChanged(nameof(AccountInitial));
                return;
            }

            var bytes = await MatrixClient.GetThumbnailAsync(
                profile.AvatarUrl,
                60,
                60,
                isJson: false);
            _accountAvatarSource = ImageSource.FromStream(() => new MemoryStream(bytes));
            OnPropertyChanged(nameof(AccountAvatarSource));
            OnPropertyChanged(nameof(AccountDisplayName));
            OnPropertyChanged(nameof(AccountUserId));
            OnPropertyChanged(nameof(AccountInitial));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load account avatar: {exception}");
        }
    }

    [RelayCommand]
    private async Task ShowUserSettingsAsync()
    {
        await CurrentPage.ShowPopupAsync(
            new Popup
            {
                Content = new UserSettingsPopup()
                    .Bind(
                        UserSettingsPopup.AvatarSourceProperty,
                        nameof(AccountAvatarSource),
                        source: BindingContext)
                    .Bind(
                        UserSettingsPopup.DisplayNameProperty,
                        nameof(AccountDisplayName),
                        source: BindingContext)
                    .Bind(
                        UserSettingsPopup.UserIdProperty,
                        nameof(AccountUserId),
                        source: BindingContext)
                    .Bind(
                        UserSettingsPopup.LogoutCommandProperty,
                        nameof(LogoutCommand),
                        source: BindingContext),
            });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (MatrixClient is not null)
        {
            await MatrixClient.LogoutAsync();
        }

        AppNavigation?.ShowLogin();
    }

    private void OnSessionInvalidated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(
            () => AppNavigation?.ShowLogin());

    [RelayCommand]
    private async Task OpenVerificationAsync()
    {
        try
        {
            var controller = await (MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required."))
                .GetSessionVerificationControllerAsync();
            await CurrentPage.ShowPopupAsync(
                new Popup
                {
                    CanBeDismissedByTappingOutsideOfPopup = false,
                    Content = new VerificationPopup
                    {
                        Controller = controller,
                    },
                });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not start verification: {exception}");
        }
    }

    private void ShowRoom(Room room)
    {
        if (_disposed)
        {
            return;
        }

        FlyoutIsPresented = false;
        var roomName = room.DisplayName() ?? room.Id();
        CurrentPage.Title = roomName;
        RoomErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Debug.WriteLine("Settings clicked.");
    }

    [RelayCommand]
    private void Load()
    {
#if WINDOWS || MACCATALYST
        ConfigureTitleBar();
#endif
        _ = LoadOwnAvatarAsync();
    }

    [RelayCommand]
    private void Unload() => Dispose();

    private void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (MatrixClient is not null)
        {
            MatrixClient.SessionInvalidated -=
                OnSessionInvalidated;
        }

        _selectedRoom = null;
    }

}
