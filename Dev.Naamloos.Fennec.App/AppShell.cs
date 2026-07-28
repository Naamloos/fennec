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
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;

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
    private bool _isRoomInfoOpen;

    public bool IsRoomInfoOpen { get => _isRoomInfoOpen; set { _isRoomInfoOpen = value; OnPropertyChanged(); } }

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
            IsRoomInfoOpen = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedRoom));
            OnPropertyChanged(nameof(ShowChat));

            if (value is not null)
            {
                ShowRoom(value);
            }
        }
    }

    public ManagedRoom? ManagedSelectedRoom
    {
        get => _managedSelectedRoom;
        set
        {
            if (value is null && SelectedRoom is not null)
            {
                return;
            }

            _managedSelectedRoom = value;
            OnPropertyChanged();
            // Ensures we do not emit an update if the selected room is already the same as the new value.
            if (SelectedRoom?.Id() != value?.NativeRoom.Id())
            {
                SelectedRoom = value?.NativeRoom;
            }
        }
    }
    private ManagedRoom? _managedSelectedRoom = null;

    public ImageSource? AccountAvatarSource => _accountAvatarSource;
    public string AccountDisplayName => _accountDisplayName;
    public string AccountUserId => _accountUserId;
    public string AccountInitial => _accountInitial;

    public bool ShowChat =>
        SelectedRoom is not null &&
        string.IsNullOrEmpty(RoomErrorMessage);

    public bool HasSelectedRoom => SelectedRoom is not null;

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

    private readonly ManagedMatrixClient _matrixClient;
    private readonly AppNavigationService _appNavigation;
    private readonly SessionVerificationService _sessionVerificationService;

    public AppShell(
        ManagedMatrixClient matrixClient,
        AppNavigationService appNavigation,
        SessionVerificationService sessionVerificationService)
    {
        _matrixClient = matrixClient;
        _appNavigation = appNavigation;
        _sessionVerificationService = sessionVerificationService;

        _matrixClient.SessionInvalidated += OnSessionInvalidated;

        _ = InitializeVerificationServiceAsync(sessionVerificationService);

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

    private static async Task InitializeVerificationServiceAsync(
        SessionVerificationService sessionVerificationService)
    {
        try
        {
            await sessionVerificationService.InitializeAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to initialize session verification: {exception}");
        }
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
        FlyoutWidth = 360;
    }

    private void Build()
    {
        FlyoutContent = new Grid
        {
            Padding = new Thickness(0, 16, 16, 16),
            RowSpacing = 16,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
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
                    },
                }.Row(0),
                new TieredSidebar()
                    .Bind(
                        TieredSidebar.SelectedRoomProperty,
                        nameof(ManagedSelectedRoom),
                        BindingMode.TwoWay,
                        source: this)
                    .Row(1),
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
                                new VerificationPopupV2(),
                                new Chat()
                                    .BindService<ManagedMatrixClient, Chat>(Chat.MatrixClientProperty)
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
                                        source: BindingContext)
                                    .Bind(Chat.IsRoomInfoOpenProperty, nameof(IsRoomInfoOpen), BindingMode.TwoWay, source: BindingContext),
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
#if ANDROID || IOS
        ConfigureMobileTitleBar();
#endif
    }

#if ANDROID || IOS
    private void ConfigureMobileTitleBar()
    {
        if (CurrentPage is not { } page) return;

        Shell.SetNavBarIsVisible(page, true);
        if (Shell.GetTitleView(page) is null)
        {
            Shell.SetTitleView(page, CreateMobileTitleView());
        }
    }

    private View CreateMobileTitleView() => new Grid
    {
        HorizontalOptions = LayoutOptions.Fill,
        ColumnDefinitions =
        {
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto),
        },
        Children =
        {
            new Label
            {
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalTextAlignment = TextAlignment.Center,
            }.Bind(
                Label.TextProperty,
                $"{nameof(ManagedSelectedRoom)}.{nameof(ManagedRoom.DisplayName)}",
                source: this),
            new MauiIcon
            {
                Icon = MaterialIcons.Info,
                IconSize = 22,
                WidthRequest = 48,
                HeightRequest = 44,
                HorizontalOptions = LayoutOptions.End,
                GestureRecognizers =
                {
                    new TapGestureRecognizer { Command = new Command(ToggleRoomInfo) },
                },
            }
            .Bind(
                IsVisibleProperty,
                nameof(HasSelectedRoom),
                source: this)
            .Column(1),
        },
    };
#endif

    private void ConfigureTitleBar()
    {
        if (Window is null)
        {
            return;
        }

        Window.TitleBar = new TitleBar
        {
            Title = "Fennec",
            HeightRequest = 42,
            TrailingContent = new HorizontalStackLayout { Children =
            {
                new AccountButton
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
                new MauiIcon { Icon = MaterialIcons.Info, IconSize = 22, WidthRequest = 40, HeightRequest = 40,
                    GestureRecognizers = { new TapGestureRecognizer { Command = new Command(ToggleRoomInfo) } } }
                    .Bind(IsVisibleProperty, nameof(HasSelectedRoom), source: this),
            }},
        };
    }

    private async Task LoadOwnAvatarAsync()
    {
        try
        {
            if (_matrixClient is null)
            {
                return;
            }

            var profile = await _matrixClient.GetOwnProfileAsync();
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

            var bytes = await _matrixClient.GetThumbnailAsync(
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

    private void ToggleRoomInfo() => IsRoomInfoOpen = !IsRoomInfoOpen;

    [RelayCommand]
    private async Task ShowUserSettingsAsync()
    {
        FlyoutIsPresented = false;
        await Navigation.PushAsync(new Settings(this));
    }

    [RelayCommand]
    private async Task StartVerificationAsync()
    {
        await _sessionVerificationService.InitializeAsync();
        await _sessionVerificationService.RequestVerificationAsync();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (_matrixClient is not null)
        {
            await _matrixClient.LogoutAsync();
        }

        _appNavigation?.ShowLogin();
    }

    private void OnSessionInvalidated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(
            () => _appNavigation?.ShowLogin());

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
    private void Load()
    {
#if WINDOWS || MACCATALYST
        ConfigureTitleBar();
#elif ANDROID || IOS
        ConfigureMobileTitleBar();
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

        if (_matrixClient is not null)
        {
            _matrixClient.SessionInvalidated -=
                OnSessionInvalidated;
        }

        _selectedRoom = null;
    }

}
