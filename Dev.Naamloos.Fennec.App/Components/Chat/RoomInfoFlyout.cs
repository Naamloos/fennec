using System.Windows.Input;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RoomInfoFlyout : ContentView
{
    private const string AnimationName = nameof(RoomInfoFlyout);
    private readonly Border _drawer;
    private readonly BoxView _scrim;
    private readonly Label _title = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Label _topic = new() { Opacity = .75, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _roomId = new()
    {
        FontSize = 11,
        Opacity = .6,
        LineBreakMode = LineBreakMode.TailTruncation,
    };
    private readonly Label _wallpaperStatus = new() { FontSize = 11, IsVisible = false };
    private readonly MatrixAvatar _avatar = new() { Size = 80 };
    private TaskHandle? _roomInfoSubscription;
    private RoomInfoListener? _roomInfoListener;

    public static readonly BindableProperty ClientProperty = BindableProperty.Create(
        nameof(Client),
        typeof(ManagedMatrixClient),
        typeof(RoomInfoFlyout)
    );
    public static readonly BindableProperty RoomProperty = BindableProperty.Create(
        nameof(Room),
        typeof(Room),
        typeof(RoomInfoFlyout),
        propertyChanged: OnRoomChanged
    );
    public static readonly BindableProperty MembersProperty = BindableProperty.Create(
        nameof(Members),
        typeof(IEnumerable<RoomMember>),
        typeof(RoomInfoFlyout)
    );
    public static readonly BindableProperty OpenProfileCommandProperty = BindableProperty.Create(
        nameof(OpenProfileCommand),
        typeof(ICommand),
        typeof(RoomInfoFlyout)
    );
    public static readonly BindableProperty WallpaperChangedCommandProperty =
        BindableProperty.Create(
            nameof(WallpaperChangedCommand),
            typeof(ICommand),
            typeof(RoomInfoFlyout)
        );
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen),
        typeof(bool),
        typeof(RoomInfoFlyout),
        false,
        BindingMode.TwoWay,
        propertyChanged: OnIsOpenChanged
    );

    public ManagedMatrixClient? Client
    {
        get => (ManagedMatrixClient?)GetValue(ClientProperty);
        set => SetValue(ClientProperty, value);
    }
    public Room? Room
    {
        get => (Room?)GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }
    public IEnumerable<RoomMember>? Members
    {
        get => (IEnumerable<RoomMember>?)GetValue(MembersProperty);
        set => SetValue(MembersProperty, value);
    }
    public ICommand? OpenProfileCommand
    {
        get => (ICommand?)GetValue(OpenProfileCommandProperty);
        set => SetValue(OpenProfileCommandProperty, value);
    }
    public ICommand? WallpaperChangedCommand
    {
        get => (ICommand?)GetValue(WallpaperChangedCommandProperty);
        set => SetValue(WallpaperChangedCommandProperty, value);
    }
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public RoomInfoFlyout()
    {
        IsVisible = false;

        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += (_, _) => IsOpen = false;
        _scrim = new BoxView
        {
            Color = Color.FromArgb("#66000000"),
            Opacity = 0,
            GestureRecognizers = { dismiss },
        };

        var close = new Button
        {
            Text = "×",
            FontSize = 28,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
        };
        close.Clicked += (_, _) => IsOpen = false;
        SemanticProperties.SetDescription(close, "Close room information");
        ToolTipProperties.SetText(close, "Close room information");

        var actions = new Button
        {
            Text = "•••",
            FontSize = 16,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
        };
        actions.Clicked += async (_, _) => await ShowActionsAsync();
        SemanticProperties.SetDescription(actions, "Open room actions");
        ToolTipProperties.SetText(actions, "Room actions");

        var members = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Header = new VerticalStackLayout
            {
                Padding = new Thickness(20, 12, 20, 14),
                Spacing = 10,
                Children =
                {
                    _avatar,
                    _title,
                    _roomId,
                    _topic,
                    WallpaperControls(),
                    _wallpaperStatus,
                    new Label
                    {
                        Text = "Members",
                        Margin = new Thickness(0, 12, 0, 2),
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        Opacity = .7,
                    },
                },
            },
            ItemTemplate = new DataTemplate(MemberRow),
        }.Bind(ItemsView.ItemsSourceProperty, nameof(Members), source: this);

        _drawer = new Border
        {
            WidthRequest = DeviceInfo.Current.Platform == DevicePlatform.Android
                || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                ? -1
                : 360,
            HorizontalOptions = DeviceInfo.Current.Platform == DevicePlatform.Android
                || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                ? LayoutOptions.Fill
                : LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            StrokeThickness = 0,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children =
                {
                    new Grid
                    {
                        Padding = new Thickness(20, 8, 8, 8),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                            new ColumnDefinition(GridLength.Auto),
                        },
                        Children =
                        {
                            new Label
                            {
                                Text = "Room info",
                                FontSize = 16,
                                FontAttributes = FontAttributes.Bold,
                                VerticalTextAlignment = TextAlignment.Center,
                            },
                            actions.Column(1),
                            close.Column(2),
                        },
                    },
                    members.Row(1),
                },
            },
        }
            .DynamicResource(VisualElement.BackgroundColorProperty, "Surface")
            .DynamicResource(Border.StrokeProperty, "OutlineVariant");

        Content = new Grid { Children = { _scrim, _drawer } };
    }

    private View MemberRow()
    {
        var row = new Grid
        {
            MinimumHeightRequest = 48,
            Padding = new Thickness(20, 7),
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Children =
            {
                new MatrixAvatar { Size = 40 }
                    .Bind(MatrixAvatar.MatrixSourceProperty, nameof(RoomMember.AvatarUrl))
                    .Bind(MatrixAvatar.DisplayNameProperty, nameof(RoomMember.DisplayName)),
                new VerticalStackLayout
                {
                    Spacing = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            FontAttributes = FontAttributes.Bold,
                            LineBreakMode = LineBreakMode.TailTruncation,
                        }.Bind(Label.TextProperty, nameof(RoomMember.DisplayName)),
                        new Label
                        {
                            FontSize = 11,
                            Opacity = .6,
                            LineBreakMode = LineBreakMode.TailTruncation,
                        }.Bind(Label.TextProperty, nameof(RoomMember.UserId)),
                    },
                }.Column(1),
            },
        };
        row.SetBinding(
            SemanticProperties.DescriptionProperty,
            new Binding(nameof(RoomMember.DisplayName), stringFormat: "Room member: {0}")
        );
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (row.BindingContext is RoomMember member)
            {
                await ShowMemberActionsAsync(member);
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private async Task ShowMemberActionsAsync(RoomMember member)
    {
        if (Room is null || Client is null || Shell.Current?.CurrentPage is not { } page)
            return;
        var actions = new List<string> { "View profile" };
        using var powerLevels = await Room.GetPowerLevels();
        var isSelf = member.UserId == Room.OwnUserId();
        if (!isSelf && powerLevels.CanOwnUserKick())
            actions.Add("Remove from room");
        if (!isSelf && powerLevels.CanOwnUserBan())
            actions.Add("Ban from room");

        var action = await InAppDialogs.ChooseAsync(
            page,
            member.DisplayName ?? member.UserId,
            actions
        );
        switch (action)
        {
            case "View profile":
                OpenProfileCommand?.Execute(member);
                break;
            case "Remove from room":
                await ModerateMemberAsync(page, member, "Remove member", Client.KickUserAsync);
                break;
            case "Ban from room":
                await ModerateMemberAsync(page, member, "Ban member", Client.BanUserAsync);
                break;
        }
    }

    private async Task ModerateMemberAsync(
        Page page,
        RoomMember member,
        string title,
        Func<string, string, string?, Task> operation
    )
    {
        if (Room is null)
            return;
        if (
            !await page.DisplayAlertAsync(
                title,
                $"{title} {member.DisplayName ?? member.UserId}?",
                title,
                "Cancel"
            )
        )
            return;
        var reason = await InAppDialogs.PromptAsync(
            page,
            title,
            "Reason (optional)",
            title,
            multiline: true
        );
        try
        {
            await operation(Room.Id(), member.UserId, reason);
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Member action failed", exception.Message, "OK");
        }
    }

    private View WallpaperControls()
    {
        var set = new Button
        {
            Text = "Set wallpaper",
            Padding = new Thickness(18, 10),
            MinimumHeightRequest = 44,
        }
            .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
            .DynamicResource(Button.TextColorProperty, "OnPrimary");
        var clear = new Button
        {
            Text = "Clear",
            Padding = new Thickness(18, 10),
            MinimumHeightRequest = 44,
        }
            .DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainer")
            .DynamicResource(Button.TextColorProperty, "OnSurface");

        set.Clicked += async (_, _) =>
        {
            if (Client is null || Room is null)
                return;
            var attachment = await AttachmentPicker.PickConfirmedAsync(
                new PickOptions
                {
                    PickerTitle = "Choose room wallpaper",
                    FileTypes = FilePickerFileType.Images,
                }
            );
            if (attachment is null)
                return;
            try
            {
                set.IsEnabled = clear.IsEnabled = false;
                _wallpaperStatus.Text = "Uploading wallpaper…";
                _wallpaperStatus.TextColor = Colors.Gray;
                _wallpaperStatus.IsVisible = true;
                var url = await Client.UploadMediaAsync(attachment.MimeType, attachment.Data);
                await Client.SetRoomWallpaperAsync(Room.Id(), url);
                WallpaperChangedCommand?.Execute(url);
                _wallpaperStatus.Text = "Wallpaper updated";
            }
            catch (Exception exception)
            {
                _wallpaperStatus.Text = exception.Message;
                _wallpaperStatus.TextColor = Colors.Red;
            }
            finally
            {
                set.IsEnabled = clear.IsEnabled = true;
            }
        };

        clear.Clicked += async (_, _) =>
        {
            if (Client is null || Room is null)
                return;
            try
            {
                set.IsEnabled = clear.IsEnabled = false;
                _wallpaperStatus.Text = "Clearing wallpaper…";
                _wallpaperStatus.TextColor = Colors.Gray;
                _wallpaperStatus.IsVisible = true;
                await Client.ClearRoomWallpaperAsync(Room.Id());
                WallpaperChangedCommand?.Execute(null);
                _wallpaperStatus.Text = "Room wallpaper cleared";
            }
            catch (Exception exception)
            {
                _wallpaperStatus.Text = exception.Message;
                _wallpaperStatus.TextColor = Colors.Red;
            }
            finally
            {
                set.IsEnabled = clear.IsEnabled = true;
            }
        };

        return new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            Children = { set, clear.Column(1) },
        };
    }

    private async Task ShowActionsAsync()
    {
        if (Client is null || Room is null || Shell.Current?.CurrentPage is not { } page)
            return;
        var info = await Room.RoomInfo();
        var actions = new List<string>
        {
            info.IsFavourite ? "Remove from favorites" : "Add to favorites",
            info.IsMarkedUnread ? "Mark read" : "Mark unread",
            info.CachedUserDefinedNotificationMode == RoomNotificationMode.Mute
                ? "Unmute notifications"
                : "Mute notifications",
            "Invite member",
            "Change room name",
            "Change topic",
            "History visibility",
            "Report room",
            "Leave room",
        };
        if (await Client.IsServerNoticeRoomAsync(Room.Id()))
            actions.Remove("Leave room");
        var action = await InAppDialogs.ChooseAsync(page, "Room actions", actions);
        if (action is null)
            return;

        try
        {
            switch (action)
            {
                case "Add to favorites":
                    await Client.SetRoomFavouriteAsync(Room.Id(), true);
                    break;
                case "Remove from favorites":
                    await Client.SetRoomFavouriteAsync(Room.Id(), false);
                    break;
                case "Mark unread":
                    await Client.SetRoomUnreadAsync(Room.Id(), true);
                    IsOpen = false;
                    break;
                case "Mark read":
                    await Room.MarkAsRead(ReceiptType.Read);
                    await Room.MarkAsRead(ReceiptType.FullyRead);
                    await Client.SetRoomUnreadAsync(Room.Id(), false);
                    IsOpen = false;
                    break;
                case "Mute notifications":
                    await Client.SetRoomMutedAsync(Room.Id(), true);
                    break;
                case "Unmute notifications":
                    await Client.SetRoomMutedAsync(Room.Id(), false);
                    break;
                case "Invite member":
                    await InviteMemberAsync(page);
                    break;
                case "Change room name":
                    await ChangeNameAsync(page);
                    break;
                case "Change topic":
                    await ChangeTopicAsync(page);
                    break;
                case "History visibility":
                    await ChangeHistoryVisibilityAsync(page);
                    break;
                case "Report room":
                    await ReportRoomAsync(page);
                    break;
                case "Leave room":
                    if (
                        await page.DisplayAlertAsync(
                            "Leave room",
                            $"Leave {_title.Text}?",
                            "Leave",
                            "Cancel"
                        )
                    )
                    {
                        await Client.LeaveRoomAsync(Room.Id(), forget: true);
                        IsOpen = false;
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Room action failed", exception.Message, "OK");
        }
    }

    private async Task InviteMemberAsync(Page page)
    {
        if (Client is null || Room is null)
            return;
        using var powerLevels = await Room.GetPowerLevels();
        if (!powerLevels.CanOwnUserInvite())
        {
            await page.DisplayAlertAsync(
                "Permission required",
                "You do not have permission to invite members.",
                "OK"
            );
            return;
        }

        var userId = await InAppDialogs.PromptAsync(
            page,
            "Invite member",
            "Matrix ID",
            "Invite",
            "@alice:example.org"
        );
        if (!string.IsNullOrWhiteSpace(userId))
            await Client.InviteUserAsync(Room.Id(), userId.Trim());
    }

    private async Task ChangeNameAsync(Page page)
    {
        if (Client is null || Room is null)
            return;
        var name = await InAppDialogs.PromptAsync(
            page,
            "Room name",
            "Name",
            "Save",
            initialValue: _title.Text
        );
        if (!string.IsNullOrWhiteSpace(name))
            await Client.SetRoomNameAsync(Room.Id(), name.Trim());
    }

    private async Task ChangeTopicAsync(Page page)
    {
        if (Client is null || Room is null)
            return;
        var topic = await InAppDialogs.PromptAsync(
            page,
            "Room topic",
            "Topic",
            "Save",
            initialValue: _topic.Text,
            multiline: true
        );
        if (topic is not null)
            await Client.SetRoomTopicAsync(Room.Id(), topic.Trim());
    }

    private async Task ChangeHistoryVisibilityAsync(Page page)
    {
        if (Client is null || Room is null)
            return;
        var selected = await InAppDialogs.ChooseAsync(
            page,
            "History visibility",
            ["Invited members", "Joined members", "Shared", "World readable"]
        );
        RoomHistoryVisibility? visibility = selected switch
        {
            "Invited members" => new RoomHistoryVisibility.Invited(),
            "Joined members" => new RoomHistoryVisibility.Joined(),
            "Shared" => new RoomHistoryVisibility.Shared(),
            "World readable" => new RoomHistoryVisibility.WorldReadable(),
            _ => null,
        };
        if (visibility is not null)
            await Client.SetRoomHistoryVisibilityAsync(Room.Id(), visibility);
    }

    private async Task ReportRoomAsync(Page page)
    {
        if (Room is null)
            return;
        var reason = await InAppDialogs.PromptAsync(
            page,
            "Report room",
            "Reason",
            "Report",
            multiline: true
        );
        if (!string.IsNullOrWhiteSpace(reason))
            await Room.ReportRoom(reason);
    }

    private static void OnRoomChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var flyout = (RoomInfoFlyout)bindable;
        flyout.StopRoomInfoUpdates();
        if (newValue is not Room room)
            return;

        ApplyInitialRoomInfo(flyout, room);
        flyout._roomInfoListener = new RoomInfoUpdateListener(flyout, room);
        flyout._roomInfoSubscription = room.SubscribeToRoomInfoUpdates(flyout._roomInfoListener);
    }

    private void ApplyRoomInfo(RoomInfo info)
    {
        var name = info.DisplayName ?? info.Id;
        _wallpaperStatus.IsVisible = false;
        _title.Text = name;
        _topic.Text = info.Topic;
        _topic.IsVisible = !string.IsNullOrWhiteSpace(_topic.Text);
        _roomId.Text = info.Id;
        _avatar.MatrixSource = info.AvatarUrl;
        _avatar.DisplayName = name;
    }

    private void StopRoomInfoUpdates()
    {
        try
        {
            _roomInfoSubscription?.Cancel();
        }
        catch
        {
            // The native listener may already have been released.
        }

        _roomInfoSubscription?.Dispose();
        _roomInfoSubscription = null;
        _roomInfoListener = null;
    }

    private sealed class RoomInfoUpdateListener(RoomInfoFlyout flyout, Room room) : RoomInfoListener
    {
        public void Call(RoomInfo roomInfo) =>
            flyout.Dispatcher.Dispatch(() =>
            {
                if (ReferenceEquals(flyout.Room, room))
                {
                    flyout.ApplyRoomInfo(roomInfo);
                }
            });
    }

    private static void ApplyInitialRoomInfo(RoomInfoFlyout flyout, Room room)
    {
        var name = room.DisplayName() ?? room.Id();
        flyout._wallpaperStatus.IsVisible = false;
        flyout._title.Text = name;
        flyout._topic.Text = room.Topic();
        flyout._topic.IsVisible = !string.IsNullOrWhiteSpace(flyout._topic.Text);
        flyout._roomId.Text = room.Id();
        flyout._avatar.MatrixSource = room.AvatarUrl();
        flyout._avatar.DisplayName = name;
    }

    private static void OnIsOpenChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((RoomInfoFlyout)bindable).SetOpen((bool)newValue);

    private void SetOpen(bool open)
    {
        this.AbortAnimation(AnimationName);
        var width = _drawer.Width > 0 ? _drawer.Width : 420;
        if (open)
        {
            IsVisible = true;
            _drawer.TranslationX = width;
            _scrim.Opacity = 0;
        }

        var animation = new Animation();
        animation.Add(
            0,
            1,
            new Animation(
                value => _drawer.TranslationX = value,
                _drawer.TranslationX,
                open ? 0 : width,
                Easing.CubicOut
            )
        );
        animation.Add(
            0,
            1,
            new Animation(value => _scrim.Opacity = value, _scrim.Opacity, open ? 1 : 0)
        );
        animation.Commit(
            this,
            AnimationName,
            16,
            200,
            finished: (_, cancelled) =>
            {
                if (!open && !cancelled)
                    IsVisible = false;
            }
        );
    }
}
