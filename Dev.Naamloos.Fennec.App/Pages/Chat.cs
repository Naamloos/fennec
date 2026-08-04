using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView
{
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _globalWallpaperCancellation;
    private ChatSession? _subscribedSession;
    private string? _roomWallpaperUrl;
    private string? _globalWallpaperUrl;
    private readonly UserProfileSheet _profileSheet = new();

    [BindableProperty(PropertyChangedMethodName = nameof(OnMatrixClientChanged))]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSelectedRoomChanged))]
    public partial Room? SelectedRoom { get; set; }

    [BindableProperty]
    public partial ChatSession? Session { get; set; }

    [BindableProperty]
    public partial bool IsLoading { get; set; } = true;

    [BindableProperty]
    public partial string RoomLoadError { get; set; } = string.Empty;

    [BindableProperty(PropertyChangedMethodName = nameof(OnTimelineIsNearBottomChanged))]
    public partial bool TimelineIsNearBottom { get; set; } = true;

    [BindableProperty]
    public partial ChatMedia? FullscreenMedia { get; set; }

    [BindableProperty]
    public partial bool IsRoomInfoOpen { get; set; }

    [BindableProperty]
    public partial bool IsVoiceRecorderOpen { get; set; }

    [BindableProperty]
    public partial string? RoomWallpaperUrl { get; set; }

    public Chat()
    {
        Content = new Grid
        {
            Children =
            {
                new MatrixImage
                {
                    IsJson = false,
                    UseFullSize = true,
                    Aspect = Aspect.AspectFill,
                    Opacity = .42,
                    InputTransparent = true,
                }
                    .Bind(MatrixImage.MatrixSourceProperty, nameof(RoomWallpaperUrl), source: this)
                    .Bind(MatrixImage.ClientProperty, nameof(MatrixClient), source: this),
                new Grid
                {
                    BackgroundColor = Colors.Transparent,
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Star),
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Auto),
                    },
                    Children =
                    {
                        new ChatTimeline()
                            .Bind(
                                ChatTimeline.ItemsProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Items)}",
                                source: this
                            )
                            .Bind(ChatTimeline.ClientProperty, nameof(MatrixClient), source: this)
                            .Bind(
                                ChatTimeline.MembersProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Members)}",
                                source: this
                            )
                            .Bind(
                                ChatTimeline.HistoryCommandProperty,
                                nameof(LoadMoreHistoryCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.HasMoreHistoryProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.CanLoadMoreHistory)}",
                                source: this
                            )
                            .Bind(
                                ChatTimeline.IsLoadingHistoryProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.IsLoadingHistory)}",
                                source: this
                            )
                            .Bind(
                                ChatTimeline.ReplyCommandProperty,
                                nameof(ReplyCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.EditCommandProperty,
                                nameof(EditCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.LinkCommandProperty,
                                nameof(OpenLinkCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.MenuCommandProperty,
                                nameof(OpenItemMenuCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.AddReactionCommandProperty,
                                nameof(OpenReactionPickerCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.OpenMediaCommandProperty,
                                nameof(OpenMediaCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.OpenProfileCommandProperty,
                                nameof(OpenProfileCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.PollVoteCommandProperty,
                                nameof(VotePollCommand),
                                source: this
                            )
                            .Bind(
                                ChatTimeline.IsNearBottomProperty,
                                nameof(TimelineIsNearBottom),
                                BindingMode.TwoWay,
                                source: this
                            )
                            .Row(0),
                        new Label
                        {
                            Margin = new Thickness(12, 2),
                            FontSize = 12,
                            Opacity = .7,
                        }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.TypingText)}",
                                source: this
                            )
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.TypingText)}",
                                converter: new IsStringNotNullOrEmptyConverter(),
                                source: this
                            )
                            .Row(1),
                        new ChatComposer()
                            .Bind(
                                ChatComposer.TextProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.DraftText)}",
                                BindingMode.TwoWay,
                                source: this
                            )
                            .Bind(
                                ChatComposer.ReplyToProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.ReplyTarget)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.EditTargetProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.EditTarget)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.CancelReplyCommandProperty,
                                nameof(CancelReplyCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.CancelEditCommandProperty,
                                nameof(CancelEditCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.ErrorMessageProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.ErrorMessage)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.HasErrorProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.HasError)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.SendCommandProperty,
                                nameof(SendMessageCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.AttachCommandProperty,
                                nameof(AttachFileCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.MoreCommandProperty,
                                nameof(OpenComposerMenuCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.InlineAttachmentCommandProperty,
                                nameof(ReceiveInlineAttachmentsCommand),
                                source: this
                            )
                            .Bind(
                                ChatComposer.MembersProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Members)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.EmotesProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Emotes)}",
                                source: this
                            )
                            .Bind(
                                ChatComposer.RoomsProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Rooms)}",
                                source: this
                            )
                            .Row(2),
                    },
                },
                new RoomInfoFlyout()
                    .Bind(RoomInfoFlyout.ClientProperty, nameof(MatrixClient), source: this)
                    .Bind(
                        RoomInfoFlyout.RoomProperty,
                        $"{nameof(Session)}.{nameof(ChatSession.Room)}",
                        source: this
                    )
                    .Bind(
                        RoomInfoFlyout.MembersProperty,
                        $"{nameof(Session)}.{nameof(ChatSession.Members)}",
                        source: this
                    )
                    .Bind(
                        RoomInfoFlyout.OpenProfileCommandProperty,
                        nameof(OpenProfileCommand),
                        source: this
                    )
                    .Bind(
                        RoomInfoFlyout.WallpaperChangedCommandProperty,
                        nameof(WallpaperChangedCommand),
                        source: this
                    )
                    .Bind(
                        RoomInfoFlyout.IsOpenProperty,
                        nameof(IsRoomInfoOpen),
                        BindingMode.TwoWay,
                        source: this
                    ),
                new Grid
                {
                    BackgroundColor = Color.FromArgb("#66000000"),
                    Children =
                    {
                        new ActivityIndicator
                        {
                            IsRunning = true,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                        },
                    },
                }.Bind(IsVisibleProperty, nameof(IsLoading), source: this),
                new MediaOverlay()
                    .Bind(MediaOverlay.ClientProperty, nameof(MatrixClient), source: this)
                    .Bind(MediaOverlay.MediaProperty, nameof(FullscreenMedia), source: this)
                    .Bind(
                        MediaOverlay.CloseCommandProperty,
                        nameof(CloseFullscreenMediaCommand),
                        source: this
                    )
                    .Bind(
                        IsVisibleProperty,
                        nameof(FullscreenMedia),
                        converter: new NotNullConverter(),
                        source: this
                    ),
                new VoiceRecorder()
                    .Bind(
                        VoiceRecorder.SendCommandProperty,
                        nameof(SendVoiceRecordingCommand),
                        source: this
                    )
                    .Bind(
                        VoiceRecorder.IsOpenProperty,
                        nameof(IsVoiceRecorderOpen),
                        BindingMode.TwoWay,
                        source: this
                    ),
                _profileSheet,
            },
        };
    }

    public async Task SetRoomAsync(Room room, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;

        IsLoading = true;
        RoomLoadError = string.Empty;
        _roomWallpaperUrl = null;
        UpdateWallpaper();

        try
        {
            await DisposeSessionAsync();

            var client =
                MatrixClient ?? throw new InvalidOperationException("Matrix client is required.");
            var session = await ChatSession.CreateAsync(client, room, token);
            token.ThrowIfCancellationRequested();
            Session = session;
            _subscribedSession = session;
            _subscribedSession.Items.CollectionChanged += OnSessionItemsChanged;
            _ = session.MarkAsReadAsync();
            _ = LoadInitialHistoryAsync(session, token);
            _ = LoadRoomWallpaperAsync(client, room.Id(), session, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer room selection superseded this request.
        }
        catch (Exception exception)
        {
            RoomLoadError = exception.Message;
            Debug.WriteLine($"Could not open room {room.Id()}: {exception}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task SendMessageAsync() =>
        Session?.CanSend == true ? Session.SendMessageAsync() : Task.CompletedTask;

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        var attachment = await AttachmentPicker.PickConfirmedAsync();
        if (attachment is null || Session is null)
        {
            return;
        }

        await Session.SendAttachmentAsync(
            attachment.FileName,
            attachment.MimeType,
            attachment.Data
        );
    }

    [RelayCommand]
    private async Task ReceiveInlineAttachmentsAsync(IReadOnlyList<PickedAttachment>? attachments)
    {
        var session = Session;
        if (attachments is null || session is null)
            return;

        foreach (var attachment in attachments)
        {
            var confirmed = await AttachmentPicker.ConfirmAsync(attachment);
            if (confirmed is not null && ReferenceEquals(Session, session))
            {
                await session.SendAttachmentAsync(
                    confirmed.FileName,
                    confirmed.MimeType,
                    confirmed.Data
                );
            }
        }
    }

    [RelayCommand]
    private Task SendVoiceRecordingAsync(PickedAttachment? attachment) =>
        attachment is not null && Session is not null
            ? Session.SendAttachmentAsync(attachment.FileName, attachment.MimeType, attachment.Data)
            : Task.CompletedTask;

    [RelayCommand]
    private async Task OpenComposerMenuAsync()
    {
        var session = Session;
        if (session is null || CurrentPage() is not { } page)
            return;
        var action = await InAppDialogs.ChooseAsync(
            page,
            "Add to message",
            ["Record voice", "Send sticker", "Create poll", "Share location"]
        );
        try
        {
            if (action == "Record voice")
            {
                IsVoiceRecorderOpen = true;
            }
            else if (action == "Send sticker")
            {
                await page.ShowPopupAsync(new StickerPickerPopup(session));
            }
            else if (action == "Create poll")
            {
                var poll = await InAppDialogs.ComposePollAsync(page);
                if (poll is not null)
                    await session.CreatePollAsync(poll.Question, poll.Answers);
            }
            else if (action == "Share location")
            {
                await ShareCurrentLocationAsync(page);
            }
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Could not send", exception.Message, "OK");
        }
    }

    private async Task ShareCurrentLocationAsync(Page page)
    {
        var permission = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (permission != PermissionStatus.Granted)
        {
            await page.DisplayAlertAsync(
                "Location unavailable",
                "Location permission is required to share your current location.",
                "OK"
            );
            return;
        }

        var location = await Geolocation.Default.GetLocationAsync(
            new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(15))
        );
        if (location is null)
        {
            await page.DisplayAlertAsync(
                "Location unavailable",
                "Your current location could not be determined.",
                "OK"
            );
            return;
        }

        var geoUri = FormattableString.Invariant(
            $"geo:{location.Latitude:F6},{location.Longitude:F6}"
        );
        if (
            await page.DisplayAlertAsync(
                "Share your location?",
                "This will send your current location to this room.",
                "Send",
                "Cancel"
            )
        )
        {
            await Session!.SendLocationAsync(geoUri, "Current location");
        }
    }

    [RelayCommand]
    private void Reply(ChatTimelineItem? item) => Session?.ReplyTo(item);

    [RelayCommand]
    private void Edit(ChatTimelineItem? item) => Session?.Edit(item);

    [RelayCommand]
    private void CancelReply() => Session?.CancelReply();

    [RelayCommand]
    private void CancelEdit() => Session?.CancelEdit();

    [RelayCommand]
    private async Task OpenLinkAsync(string? value)
    {
        if (
            CurrentPage() is not { } page
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
        {
            return;
        }

        switch (await InAppDialogs.ChooseAsync(page, uri.AbsoluteUri, ["Open", "Copy"]))
        {
            case "Open":
                await Launcher.Default.OpenAsync(uri);
                break;
            case "Copy":
                await Clipboard.Default.SetTextAsync(uri.AbsoluteUri);
                break;
        }
    }

    [RelayCommand]
    private async Task LoadMoreHistoryAsync() =>
        await (Session?.LoadMoreHistoryAsync() ?? Task.CompletedTask);

    [RelayCommand]
    private async Task OpenReactionPickerAsync(ChatTimelineItem? item)
    {
        if (item is null || Session is null || CurrentPage() is not { } page)
        {
            return;
        }

        await page.ShowPopupAsync(new EmotePickerPopup(Session, item));
    }

    [RelayCommand]
    private void OpenMedia(ChatMedia? media)
    {
        FullscreenMedia = media;
    }

    [RelayCommand]
    private Task VotePollAsync(ChatPollVote? vote) =>
        vote is not null && Session is not null
            ? Session.VoteInPollAsync(vote.Item, vote.AnswerId)
            : Task.CompletedTask;

    [RelayCommand]
    private async Task OpenProfileAsync(object? value)
    {
        if (MatrixClient is null)
            return;

        var member = value as RoomMember;
        var userId = member?.UserId ?? value as string;
        if (string.IsNullOrWhiteSpace(userId))
            return;

        member ??= Session?.Members.FirstOrDefault(candidate => candidate.UserId == userId);
        IsRoomInfoOpen = false;
        if (Shell.Current is { } shell)
        {
            shell.FlyoutIsPresented = false;
        }
        await _profileSheet.ShowAsync(MatrixClient, userId, member?.DisplayName, member?.AvatarUrl);
    }

    [RelayCommand]
    private void WallpaperChanged(string? url)
    {
        _roomWallpaperUrl = url;
        UpdateWallpaper();
    }

    [RelayCommand]
    private void CloseFullscreenMedia() => FullscreenMedia = null;

    [RelayCommand]
    private async Task OpenItemMenuAsync(ChatTimelineItem? item)
    {
        if (item is null || CurrentPage() is not { } page)
        {
            return;
        }

        var actions = new List<string> { "View source" };

        if (item.IsMessage)
        {
            actions.Insert(0, "React");
            actions.Insert(0, "Copy text");

            if (item.CanReply)
            {
                actions.Insert(0, "Reply in thread");
                actions.Insert(0, "Reply");
            }

            if (item.IsOwn && item.EventId is not null)
            {
                actions.Insert(0, "Edit");
            }

            if (Session is not null && await Session.CanDeleteAsync(item))
            {
                actions.Add("Delete");
            }

            if (item.Media is not null)
            {
                actions.Add("Save file");
            }

            if (!item.IsOwn)
            {
                actions.Add("Report message");
                actions.Add("Block user");
            }
        }

        switch (await InAppDialogs.ChooseAsync(page, "Timeline item", actions))
        {
            case "Reply in thread":
                Session?.ReplyInThread(item);
                break;
            case "Reply":
                Reply(item);
                break;
            case "Edit":
                Edit(item);
                break;
            case "Delete":
                await (Session?.DeleteAsync(item) ?? Task.FromResult(false));
                break;
            case "Copy text":
                await Clipboard.Default.SetTextAsync(item.Body);
                break;
            case "React":
                await OpenReactionPickerAsync(item);
                break;
            case "Save file" when item.Media is not null:
                await SaveMediaAsync(item.Media);
                break;
            case "Report message":
                await ReportMessageAsync(item);
                break;
            case "Block user":
                await BlockUserAsync(item);
                break;
            case "View source":
                await page.ShowPopupAsync(new SyntaxHighlighterPopup(item.SourceJson));
                break;
        }
    }

    private async Task SaveMediaAsync(ChatMedia media)
    {
        if (MatrixClient is null)
        {
            return;
        }

#if WINDOWS
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(media.Filename),
        };
        picker.FileTypeChoices.Add(
            "File",
            [Path.GetExtension(media.Filename) is { Length: > 0 } extension ? extension : ".bin"]
        );
        var window =
            Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Application window unavailable.");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(window)
        );
        var destination = await picker.PickSaveFileAsync();
        if (destination is not null)
        {
            await Windows.Storage.FileIO.WriteBytesAsync(
                destination,
                await MatrixClient.GetMediaContentAsync(media.SourceJson)
            );
        }
#elif ANDROID
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, media.Filename);
        values.Put(
            Android.Provider.MediaStore.IMediaColumns.MimeType,
            media.MimeType ?? "application/octet-stream"
        );
        values.Put(
            Android.Provider.MediaStore.IMediaColumns.RelativePath,
            Android.OS.Environment.DirectoryDownloads
        );
        var resolver = Android.App.Application.Context!.ContentResolver!;
        var destination = resolver.Insert(
            Android.Provider.MediaStore.Downloads.ExternalContentUri,
            values
        );
        if (destination is not null)
        {
            await using var output =
                resolver.OpenOutputStream(destination)
                ?? throw new IOException("Could not open download.");
            await output.WriteAsync(await MatrixClient.GetMediaContentAsync(media.SourceJson));
        }
#endif
    }

    private async Task ReportMessageAsync(ChatTimelineItem item)
    {
        if (Session?.Room is not { } room || item.EventId is null || CurrentPage() is not { } page)
            return;
        var reason = await InAppDialogs.PromptAsync(
            page,
            "Report message",
            "Reason (optional)",
            "Report",
            multiline: true
        );
        if (reason is null)
            return;

        try
        {
            await room.ReportContent(item.EventId, reason);
            await page.DisplayAlertAsync(
                "Report sent",
                "The homeserver received your report.",
                "OK"
            );
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Could not report message", exception.Message, "OK");
        }
    }

    private async Task BlockUserAsync(ChatTimelineItem item)
    {
        if (
            MatrixClient is null
            || string.IsNullOrWhiteSpace(item.SenderId)
            || CurrentPage() is not { } page
        )
            return;
        if (!await page.DisplayAlertAsync("Block user", $"Block {item.Sender}?", "Block", "Cancel"))
            return;

        try
        {
            await MatrixClient.IgnoreUserAsync(item.SenderId);
            await page.DisplayAlertAsync(
                "User blocked",
                "Their future messages will be ignored by Matrix.",
                "OK"
            );
        }
        catch (Exception exception)
        {
            await page.DisplayAlertAsync("Could not block user", exception.Message, "OK");
        }
    }

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    )
    {
        var chat = (Chat)bindable;
        chat._roomWallpaperUrl = null;
        chat.UpdateWallpaper();
        if (newValue is Room room)
        {
            _ = chat.SetRoomAsync(room);
        }
    }

    private static void OnMatrixClientChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    )
    {
        var chat = (Chat)bindable;
        chat._globalWallpaperCancellation?.Cancel();
        chat._globalWallpaperCancellation?.Dispose();
        chat._globalWallpaperCancellation = null;
        if (oldValue is ManagedMatrixClient oldClient)
            oldClient.GlobalWallpaperChanged -= chat.OnGlobalWallpaperChanged;
        if (newValue is ManagedMatrixClient newClient)
        {
            newClient.GlobalWallpaperChanged += chat.OnGlobalWallpaperChanged;
            chat._globalWallpaperCancellation = new CancellationTokenSource();
            _ = chat.WatchGlobalWallpaperAsync(newClient, chat._globalWallpaperCancellation.Token);
        }
    }

    private async Task WatchGlobalWallpaperAsync(
        ManagedMatrixClient client,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var refreshDelay = TimeSpan.FromSeconds(1);
            try
            {
                if (client.IsLoggedIn)
                {
                    refreshDelay = TimeSpan.FromSeconds(15);
                    var wallpaper = await client.GetGlobalWallpaperAsync();
                    if (!ReferenceEquals(MatrixClient, client))
                        return;

                    Dispatcher.Dispatch(() =>
                    {
                        if (_globalWallpaperUrl == wallpaper)
                            return;

                        _globalWallpaperUrl = wallpaper;
                        UpdateWallpaper();
                    });
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not load global wallpaper: {exception}");
            }

            try
            {
                await Task.Delay(refreshDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task LoadRoomWallpaperAsync(
        ManagedMatrixClient client,
        string roomId,
        ChatSession session,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested && ReferenceEquals(Session, session))
        {
            try
            {
                if (client.IsLoggedIn)
                {
                    var wallpaper = await client.GetRoomWallpaperAsync(roomId);
                    if (
                        cancellationToken.IsCancellationRequested
                        || !ReferenceEquals(Session, session)
                    )
                    {
                        return;
                    }

                    Dispatcher.Dispatch(() =>
                    {
                        if (_roomWallpaperUrl == wallpaper)
                            return;

                        _roomWallpaperUrl = wallpaper;
                        UpdateWallpaper();
                    });
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not load room wallpaper for {roomId}: {exception}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task LoadInitialHistoryAsync(
        ChatSession session,
        CancellationToken cancellationToken
    )
    {
        await Task.Yield();
        if (!cancellationToken.IsCancellationRequested && ReferenceEquals(Session, session))
        {
            await session.LoadMoreHistoryAsync(100, cancellationToken);
        }
    }

    private void OnGlobalWallpaperChanged(string? url) =>
        Dispatcher.Dispatch(() =>
        {
            _globalWallpaperUrl = url;
            UpdateWallpaper();
        });

    private void UpdateWallpaper() => RoomWallpaperUrl = _roomWallpaperUrl ?? _globalWallpaperUrl;

    private static void OnTimelineIsNearBottomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    )
    {
        if (newValue is true)
        {
            _ = ((Chat)bindable).Session?.MarkAsReadAsync();
        }
    }

    private void OnSessionItemsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (TimelineIsNearBottom)
        {
            _ = Session?.MarkAsReadAsync();
        }
    }

    private async Task DisposeSessionAsync()
    {
        if (_subscribedSession is not null)
        {
            _subscribedSession.Items.CollectionChanged -= OnSessionItemsChanged;
            _subscribedSession = null;
        }

        if (Session is not null)
        {
            await Session.DisposeAsync();
            Session = null;
        }
    }

    private static Page? CurrentPage() =>
        Application
            .Current?.Windows.Select(window => window.Page)
            .FirstOrDefault(page => page is not null);
}
