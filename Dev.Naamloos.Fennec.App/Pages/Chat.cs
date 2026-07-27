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
using System.Collections.Specialized;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView, IAsyncDisposable
{
    private CancellationTokenSource? _loadCancellation;
    private ChatSession? _subscribedSession;
    private bool _disposed;

    [BindableProperty]
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

    public Chat()
    {
        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    EventName = nameof(Unloaded),
                }
                .Bind(EventToCommandBehavior.CommandProperty,
                    nameof(UnloadCommand), source: this),
            },
            Children =
            {
                new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Star),
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Auto),
                    },
                    Children =
                    {
                        new ChatTimeline()
                            .Bind(ChatTimeline.ItemsProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Items)}",
                                source: this)
                            .Bind(ChatTimeline.ClientProperty, nameof(MatrixClient), source: this)
                            .Bind(ChatTimeline.MembersProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Members)}", source: this)
                            .Bind(ChatTimeline.HistoryCommandProperty,
                                nameof(LoadMoreHistoryCommand), source: this)
                            .Bind(ChatTimeline.HasMoreHistoryProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.CanLoadMoreHistory)}",
                                source: this)
                            .Bind(ChatTimeline.ReplyCommandProperty,
                                nameof(ReplyCommand), source: this)
                            .Bind(ChatTimeline.EditCommandProperty,
                                nameof(EditCommand), source: this)
                            .Bind(ChatTimeline.LinkCommandProperty,
                                nameof(OpenLinkCommand), source: this)
                            .Bind(ChatTimeline.MenuCommandProperty,
                                nameof(OpenItemMenuCommand), source: this)
                            .Bind(ChatTimeline.AddReactionCommandProperty,
                                nameof(OpenReactionPickerCommand), source: this)
                            .Bind(ChatTimeline.OpenMediaCommandProperty,
                                nameof(OpenMediaCommand), source: this)
                            .Bind(ChatTimeline.IsNearBottomProperty,
                                nameof(TimelineIsNearBottom), BindingMode.TwoWay, source: this)
                            .Row(0),

                        new Label
                        {
                            Margin = new Thickness(12, 2),
                            FontSize = 12,
                            Opacity = .7,
                        }
                        .Bind(Label.TextProperty,
                            $"{nameof(Session)}.{nameof(ChatSession.TypingText)}",
                            source: this)
                        .Bind(IsVisibleProperty,
                            $"{nameof(Session)}.{nameof(ChatSession.TypingText)}",
                            converter: new IsStringNotNullOrEmptyConverter(), source: this)
                        .Row(1),

                        new ChatComposer()
                            .Bind(ChatComposer.TextProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.DraftText)}",
                                BindingMode.TwoWay, source: this)
                            .Bind(ChatComposer.ReplyToProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.ReplyTarget)}",
                                source: this)
                            .Bind(ChatComposer.EditTargetProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.EditTarget)}",
                                source: this)
                            .Bind(ChatComposer.CancelReplyCommandProperty,
                                nameof(CancelReplyCommand), source: this)
                            .Bind(ChatComposer.CancelEditCommandProperty,
                                nameof(CancelEditCommand), source: this)
                            .Bind(ChatComposer.ErrorMessageProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.ErrorMessage)}",
                                source: this)
                            .Bind(ChatComposer.HasErrorProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.HasError)}",
                                source: this)
                            .Bind(ChatComposer.SendCommandProperty,
                                nameof(SendMessageCommand), source: this)
                            .Bind(ChatComposer.AttachCommandProperty,
                                nameof(AttachFileCommand), source: this)
                            .Bind(ChatComposer.MembersProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Members)}", source: this)
                            .Bind(ChatComposer.EmotesProperty,
                                $"{nameof(Session)}.{nameof(ChatSession.Emotes)}", source: this)
                            .Row(2),
                    },
                }
                .Bind(IsVisibleProperty, nameof(IsLoading),
                    converter: new BooleanInverterConverter(), source: this),

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
                }
                .Bind(IsVisibleProperty, nameof(IsLoading), source: this),

                new MediaOverlay()
                    .Bind(MediaOverlay.ClientProperty, nameof(MatrixClient), source: this)
                    .Bind(MediaOverlay.MediaProperty, nameof(FullscreenMedia), source: this)
                    .Bind(MediaOverlay.CloseCommandProperty,
                        nameof(CloseFullscreenMediaCommand), source: this)
                    .Bind(IsVisibleProperty, nameof(FullscreenMedia),
                        converter: new NotNullConverter(), source: this),
            },
        };
    }

    public async Task SetRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCancellation.Token;

        IsLoading = true;
        RoomLoadError = string.Empty;

        try
        {
            await DisposeSessionAsync();

            Session = await ChatSession.CreateAsync(
                MatrixClient ?? throw new InvalidOperationException("Matrix client is required."),
                room,
                token);
            _subscribedSession = Session;
            _subscribedSession.Items.CollectionChanged += OnSessionItemsChanged;
            await Session.MarkAsReadAsync();
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
    private Task SendMessageAsync() => Session?.CanSend == true
        ? Session.SendMessageAsync()
        : Task.CompletedTask;

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        var file = await FilePicker.Default.PickAsync();

        if (file is null || Session is null)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync();
        using var data = new MemoryStream();
        await stream.CopyToAsync(data);
        await Session.SendAttachmentAsync(
            file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            data.ToArray());
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
        if (CurrentPage() is not { } page ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        switch (await page.DisplayActionSheetAsync(uri.AbsoluteUri, "Cancel", null, "Open", "Copy"))
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
        }

        switch (await page.DisplayActionSheetAsync("Timeline item", "Cancel", null, actions.ToArray()))
        {
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
        picker.FileTypeChoices.Add("File", [Path.GetExtension(media.Filename) is { Length: > 0 } extension ? extension : ".bin"]);
        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Application window unavailable.");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(window));
        var destination = await picker.PickSaveFileAsync();
        if (destination is not null)
        {
            await Windows.Storage.FileIO.WriteBytesAsync(
                destination,
                await MatrixClient.GetMediaContentAsync(media.SourceJson));
        }
#elif ANDROID
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, media.Filename);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, media.MimeType ?? "application/octet-stream");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryDownloads);
        var resolver = Android.App.Application.Context!.ContentResolver!;
        var destination = resolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
        if (destination is not null)
        {
            await using var output = resolver.OpenOutputStream(destination)
                ?? throw new IOException("Could not open download.");
            await output.WriteAsync(await MatrixClient.GetMediaContentAsync(media.SourceJson));
        }
#endif
    }

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (newValue is Room room)
        {
            _ = ((Chat)bindable).SetRoomAsync(room);
        }
    }

    private static void OnTimelineIsNearBottomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (newValue is true)
        {
            _ = ((Chat)bindable).Session?.MarkAsReadAsync();
        }
    }

    private void OnSessionItemsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        if (TimelineIsNearBottom)
        {
            _ = Session?.MarkAsReadAsync();
        }
    }

    [RelayCommand]
    private async Task UnloadAsync() => await DisposeAsync();

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        await DisposeSessionAsync();
    }

    private static Page? CurrentPage() => Application.Current?
        .Windows
        .Select(window => window.Page)
        .FirstOrDefault(page => page is not null);
}
