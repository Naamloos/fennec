using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;
using Microsoft.Maui.Controls.Shapes;
using uniffi.matrix_sdk_ffi;

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

    public bool IsRoomInfoOpen
    {
        get => _isRoomInfoOpen;
        set
        {
            _isRoomInfoOpen = value;
            OnPropertyChanged();
        }
    }

    public Room? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            if (value?.Membership() == Membership.Invited)
            {
                _ = HandleInviteAsync(value);
                return;
            }

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

    public bool ShowChat => SelectedRoom is not null && string.IsNullOrEmpty(RoomErrorMessage);

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
        SessionVerificationService sessionVerificationService
    )
    {
        _matrixClient = matrixClient;
        _appNavigation = appNavigation;
        _sessionVerificationService = sessionVerificationService;

        _matrixClient.SessionInvalidated += OnSessionInvalidated;
        _matrixClient.AvatarChanged += OnAvatarChanged;

        _ = InitializeVerificationServiceAsync(sessionVerificationService);

        BindingContext = this;

        ConfigureShell();
        Build();

        Behaviors.Add(
            new EventToCommandBehavior { BindingContext = this, EventName = nameof(Loaded) }.Bind(
                EventToCommandBehavior.CommandProperty,
                nameof(LoadCommand)
            )
        );
        Behaviors.Add(
            new EventToCommandBehavior { BindingContext = this, EventName = nameof(Unloaded) }.Bind(
                EventToCommandBehavior.CommandProperty,
                nameof(UnloadCommand)
            )
        );
    }

    private static async Task InitializeVerificationServiceAsync(
        SessionVerificationService sessionVerificationService
    )
    {
        try
        {
            await sessionVerificationService.InitializeAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to initialize session verification: {exception}");
        }
    }

    private void ConfigureShell()
    {
        this.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
#if ANDROID
        FlyoutBackgroundColor = Colors.Transparent;
        FlyoutBackdrop = new SolidColorBrush(Color.FromArgb("#66000000"));
#else
        this.DynamicResource(FlyoutBackgroundColorProperty, "Surface");
#endif
        this.DynamicResource(Shell.BackgroundColorProperty, "Surface")
            .DynamicResource(Shell.ForegroundColorProperty, "OnSurface")
            .DynamicResource(Shell.TitleColorProperty, "OnSurface")
            .DynamicResource(Shell.DisabledColorProperty, "Outline")
            .DynamicResource(Shell.UnselectedColorProperty, "OnSurfaceVariant")
            .DynamicResource(Shell.TabBarBackgroundColorProperty, "Surface2")
            .DynamicResource(Shell.TabBarForegroundColorProperty, "Primary")
            .DynamicResource(Shell.TabBarTitleColorProperty, "Primary")
            .DynamicResource(Shell.TabBarUnselectedColorProperty, "OnSurfaceVariant");

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutWidth = 360;
    }

    private void Build()
    {
        var sidebar = new Grid
        {
            Padding = 0,
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
                        new Button
                        {
                            Text = "New conversation",
                            HorizontalOptions = LayoutOptions.Fill,
                            Margin = new Thickness(8, 0, 8, 10),
                        }
                            .DynamicResource(
                                VisualElement.BackgroundColorProperty,
                                "PrimaryContainer"
                            )
                            .DynamicResource(Button.TextColorProperty, "OnPrimaryContainer")
                            .BindCommand(nameof(OpenConversationCommand), source: this),
#if !WINDOWS && !MACCATALYST
                        new AccountButton { Margin = new Thickness(8, 0, 8, 4), ShowUserId = true }
                            .Bind(
                                AccountButton.AvatarSourceProperty,
                                nameof(AccountAvatarSource),
                                source: BindingContext
                            )
                            .Bind(
                                AccountButton.DisplayNameProperty,
                                nameof(AccountDisplayName),
                                source: BindingContext
                            )
                            .Bind(
                                AccountButton.UserIdProperty,
                                nameof(AccountUserId),
                                source: BindingContext
                            )
                            .Bind(
                                AccountButton.InitialProperty,
                                nameof(AccountInitial),
                                source: BindingContext
                            )
                            .Bind(
                                AccountButton.OpenCommandProperty,
                                nameof(ShowUserSettingsCommand),
                                source: BindingContext
                            ),
#endif
                    },
                }.Row(0),
                new TieredSidebar()
                    .Bind(
                        TieredSidebar.SelectedRoomProperty,
                        nameof(ManagedSelectedRoom),
                        BindingMode.TwoWay,
                        source: this
                    )
                    .Row(1),
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
        FlyoutContent = sidebar;

        Items.Add(
            new FlyoutItem
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
                            SafeAreaEdges = SafeAreaEdges.All,
                            Content = new Grid
                            {
                                Children =
                                {
                                    new VerificationPopupV2(),
                                    new Chat()
                                        .BindService<ManagedMatrixClient, Chat>(
                                            Chat.MatrixClientProperty
                                        )
                                        .Bind(
                                            IsVisibleProperty,
                                            nameof(ShowChat),
                                            source: BindingContext
                                        )
                                        .Bind(
                                            Chat.SelectedRoomProperty,
                                            nameof(SelectedRoom),
                                            source: BindingContext
                                        )
                                        .Bind(
                                            Chat.RoomLoadErrorProperty,
                                            nameof(RoomErrorMessage),
                                            BindingMode.TwoWay,
                                            source: BindingContext
                                        )
                                        .Bind(
                                            Chat.IsRoomInfoOpenProperty,
                                            nameof(IsRoomInfoOpen),
                                            BindingMode.TwoWay,
                                            source: BindingContext
                                        ),
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
                                                Text =
                                                    "Choose a room from the sidebar to start chatting.",
                                                Opacity = .7,
                                                HorizontalTextAlignment = TextAlignment.Center,
                                            },
                                        },
                                    }.Bind(
                                        IsVisibleProperty,
                                        nameof(SelectedRoom),
                                        converter: new IsNullConverter(),
                                        source: BindingContext
                                    ),
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
                                                source: BindingContext
                                            ),
                                        },
                                    }.Bind(
                                        IsVisibleProperty,
                                        nameof(RoomErrorMessage),
                                        converter: new IsStringNotNullOrEmptyConverter(),
                                        source: BindingContext
                                    ),
                                },
                            },
                        },
                    },
                },
            }
        );
        CurrentItem = Items.Single();
#if ANDROID || IOS
        ConfigureMobileTitleBar();
#endif
    }

#if ANDROID || IOS
    private void ConfigureMobileTitleBar()
    {
        if (CurrentPage is not { } page)
            return;

        Shell.SetNavBarIsVisible(page, true);
        if (Shell.GetTitleView(page) is null)
        {
            Shell.SetTitleView(page, CreateMobileTitleView());
        }
    }

    private View CreateMobileTitleView() =>
        new Grid
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
                    source: this
                ),
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
                    .Bind(IsVisibleProperty, nameof(HasSelectedRoom), source: this)
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
            TrailingContent = new HorizontalStackLayout
            {
                Children =
                {
                    new MauiIcon
                    {
                        Icon = MaterialIcons.Search,
                        IconSize = 22,
                        WidthRequest = 40,
                        HeightRequest = 40,
                        GestureRecognizers =
                        {
                            new TapGestureRecognizer().BindCommand(
                                nameof(SearchMessagesCommand),
                                source: this
                            ),
                        },
                    },
                    new AccountButton
                    {
                        Margin = new Thickness(0, 0, 8, 0),
                        TransparentBackground = true,
                    }
                        .Bind(
                            AccountButton.AvatarSourceProperty,
                            nameof(AccountAvatarSource),
                            source: BindingContext
                        )
                        .Bind(
                            AccountButton.DisplayNameProperty,
                            nameof(AccountDisplayName),
                            source: BindingContext
                        )
                        .Bind(
                            AccountButton.UserIdProperty,
                            nameof(AccountUserId),
                            source: BindingContext
                        )
                        .Bind(
                            AccountButton.InitialProperty,
                            nameof(AccountInitial),
                            source: BindingContext
                        )
                        .Bind(
                            AccountButton.OpenCommandProperty,
                            nameof(ShowUserSettingsCommand),
                            source: BindingContext
                        ),
                    new MauiIcon
                    {
                        Icon = MaterialIcons.Info,
                        IconSize = 22,
                        WidthRequest = 40,
                        HeightRequest = 40,
                        GestureRecognizers =
                        {
                            new TapGestureRecognizer { Command = new Command(ToggleRoomInfo) },
                        },
                    }.Bind(IsVisibleProperty, nameof(HasSelectedRoom), source: this),
                },
            },
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
                ? "@"
                : profile.DisplayName[..1].ToUpperInvariant();
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
                isJson: false
            );
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
    private async Task SearchMessagesAsync()
    {
        var page = CurrentPage;
        var query = await InAppDialogs.PromptAsync(
            page,
            "Search messages",
            "Search joined conversations",
            "Search"
        );
        if (string.IsNullOrWhiteSpace(query))
            return;

        try
        {
            var results = await _matrixClient.SearchMessagesAsync(query.Trim());
            if (results.Count == 0)
            {
                await page.DisplayAlertAsync("Search messages", "No matching messages.", "OK");
                return;
            }

            var labels = results
                .Take(20)
                .Select(result => $"{result.SenderId}: {result.Body}")
                .ToArray();
            var selected = await InAppDialogs.ChooseAsync(
                page,
                $"{results.Count} result(s)",
                labels
            );
            var result = results.FirstOrDefault(candidate =>
                $"{candidate.SenderId}: {candidate.Body}" == selected
            );
            if (result is not null)
            {
                ManagedSelectedRoom = new ManagedRoom(
                    _matrixClient.GetSyncService().RoomListService().Room(result.RoomId)
                );
            }
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Search failed", exception.Message, "OK");
        }
    }

    private async Task HandleInviteAsync(Room room)
    {
        var page = CurrentPage;
        var name = room.DisplayName() ?? room.Id();
        var accept = await page.DisplayAlertAsync(
            "Room invitation",
            $"Join {name}?",
            "Join",
            "Decline"
        );
        try
        {
            if (accept)
            {
                await _matrixClient.AcceptInviteAsync(room.Id());
                SelectedRoom = room;
            }
            else
            {
                await _matrixClient.DeclineInviteAsync(room.Id());
            }
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Could not update invitation", exception.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task OpenConversationAsync()
    {
        var page = CurrentPage;
        var action = await InAppDialogs.ChooseAsync(
            page,
            "New conversation",
            ["Start direct message", "Join room"]
        );
        var prompt = action switch
        {
            "Start direct message" => await InAppDialogs.PromptAsync(
                page,
                "Start direct message",
                "Matrix ID",
                "Start",
                "@alice:example.org"
            ),
            "Join room" => await InAppDialogs.PromptAsync(
                page,
                "Join room",
                "Room alias, ID, or link",
                "Join",
                "#community:example.org"
            ),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        try
        {
            var room =
                action == "Start direct message"
                    ? await _matrixClient.CreateDirectMessageAsync(prompt.Trim())
                    : await _matrixClient.JoinRoomAsync(prompt.Trim());
            ManagedSelectedRoom = room;
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Could not open conversation", exception.Message, "OK");
        }
    }

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
        MainThread.BeginInvokeOnMainThread(() => _appNavigation?.ShowLogin());

    private void OnAvatarChanged(string? previous, string? current) =>
        MainThread.BeginInvokeOnMainThread(() => _ = LoadOwnAvatarAsync());

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
            _matrixClient.SessionInvalidated -= OnSessionInvalidated;
            _matrixClient.AvatarChanged -= OnAvatarChanged;
        }

        _selectedRoom = null;
    }
}
