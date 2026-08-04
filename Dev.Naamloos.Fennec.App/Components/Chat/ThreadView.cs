using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ThreadView : ContentView
{
    private CancellationTokenSource? _loadCancellation;

    [BindableProperty(PropertyChangedMethodName = nameof(OnSourceChanged))]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSourceChanged))]
    public partial Room? Room { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSourceChanged))]
    public partial string? RootEventId { get; set; }

    [BindableProperty]
    public partial bool IsOpen { get; set; }

    [BindableProperty]
    public partial ChatSession? Session { get; set; }

    [BindableProperty]
    public partial ChatMedia? FullscreenMedia { get; set; }

    [BindableProperty]
    public partial bool IsLoading { get; set; }

    [BindableProperty]
    public partial string LoadError { get; set; } = string.Empty;

    public ThreadView()
    {
        this.BindService<ManagedMatrixClient, ThreadView>(ClientProperty);
        Content = new Grid
        {
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#66000000"),
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer { Command = new Command(Close) },
                    },
                },
                new Border
                {
                    WidthRequest =
                        DeviceInfo.Current.Platform == DevicePlatform.Android
                        || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? -1
                            : 480,
                    HorizontalOptions =
                        DeviceInfo.Current.Platform == DevicePlatform.Android
                        || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? LayoutOptions.Fill
                            : LayoutOptions.End,
                    StrokeThickness = 0,
                    Content = new Grid
                    {
                        RowDefinitions =
                        {
                            new RowDefinition(GridLength.Auto),
                            new RowDefinition(GridLength.Star),
                            new RowDefinition(GridLength.Auto),
                        },
                        Children =
                        {
                            new Grid
                            {
                                Padding = 12,
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Auto),
                                },
                                Children =
                                {
                                    new Label
                                    {
                                        Text = "Thread",
                                        FontSize = 22,
                                        FontAttributes = FontAttributes.Bold,
                                        VerticalOptions = LayoutOptions.Center,
                                    },
                                    new Button
                                    {
                                        Text = "×",
                                        FontSize = 26,
                                        WidthRequest = 44,
                                        HeightRequest = 44,
                                        Padding = 0,
                                        BackgroundColor = Colors.Transparent,
                                        Command = new Command(Close),
                                    }
                                        .Invoke(view =>
                                        {
                                            SemanticProperties.SetDescription(view, "Close thread");
                                            ToolTipProperties.SetText(view, "Close thread");
                                        })
                                        .Column(1),
                                },
                            }.Row(0),
                            new Grid
                            {
                                Children =
                                {
                                    new ChatTimeline
                                    {
                                        EmptyMessage = "No replies in this thread yet.",
                                    }
                                        .Bind(
                                            ChatTimeline.ItemsProperty,
                                            $"{nameof(Session)}.{nameof(ChatSession.Items)}",
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.ClientProperty,
                                            nameof(Client),
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.MembersProperty,
                                            $"{nameof(Session)}.{nameof(ChatSession.Members)}",
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.HistoryCommandProperty,
                                            nameof(LoadMoreCommand),
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
                                            ChatTimeline.AddReactionCommandProperty,
                                            nameof(ReactCommand),
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.MenuCommandProperty,
                                            nameof(MenuCommand),
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.OpenMediaCommandProperty,
                                            nameof(OpenMediaCommand),
                                            source: this
                                        )
                                        .Bind(
                                            ChatTimeline.PollVoteCommandProperty,
                                            nameof(VotePollCommand),
                                            source: this
                                        )
                                        .Bind<ChatTimeline, bool, string, bool>(
                                            IsVisibleProperty,
                                            new Binding(nameof(IsLoading), source: this),
                                            new Binding(nameof(LoadError), source: this),
                                            convert: static values =>
                                                !values.Item1
                                                && string.IsNullOrWhiteSpace(values.Item2)
                                        ),
                                    new ActivityIndicator
                                    {
                                        HorizontalOptions = LayoutOptions.Center,
                                        VerticalOptions = LayoutOptions.Center,
                                    }
                                        .Bind(
                                            ActivityIndicator.IsRunningProperty,
                                            nameof(IsLoading),
                                            source: this
                                        )
                                        .Bind(IsVisibleProperty, nameof(IsLoading), source: this),
                                    new Label
                                    {
                                        Margin = 24,
                                        TextColor = Colors.Red,
                                        HorizontalTextAlignment = TextAlignment.Center,
                                        VerticalTextAlignment = TextAlignment.Center,
                                    }
                                        .Bind(Label.TextProperty, nameof(LoadError), source: this)
                                        .Bind(
                                            IsVisibleProperty,
                                            nameof(LoadError),
                                            converter: new CommunityToolkit.Maui.Converters.IsStringNotNullOrEmptyConverter(),
                                            source: this
                                        ),
                                },
                            }.Row(1),
                            new ChatComposer()
                                .Bind(ChatComposer.SessionProperty, nameof(Session), source: this)
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
                                    ChatComposer.SendCommandProperty,
                                    nameof(SendCommand),
                                    source: this
                                )
                                .Bind(
                                    ChatComposer.AttachCommandProperty,
                                    nameof(AttachCommand),
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
                                    IsVisibleProperty,
                                    nameof(Session),
                                    converter: new Dev.Naamloos.Fennec.App.Converters.NotNullConverter(),
                                    source: this
                                )
                                .Row(2),
                        },
                    },
                }.DynamicResource(BackgroundColorProperty, "Surface"),
                new MediaOverlay()
                    .Bind(MediaOverlay.ClientProperty, nameof(Client), source: this)
                    .Bind(MediaOverlay.MediaProperty, nameof(FullscreenMedia), source: this)
                    .Bind(
                        MediaOverlay.CloseCommandProperty,
                        nameof(CloseMediaCommand),
                        source: this
                    )
                    .Bind(
                        IsVisibleProperty,
                        nameof(FullscreenMedia),
                        converter: new Dev.Naamloos.Fennec.App.Converters.NotNullConverter(),
                        source: this
                    ),
            },
        }.Bind(IsVisibleProperty, nameof(IsOpen), source: this);
        Unloaded += (_, _) => _ = DisposeSessionAsync();
    }

    private static void OnSourceChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => _ = ((ThreadView)bindable).LoadAsync();

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        try
        {
            await DisposeSessionAsync();
            token.ThrowIfCancellationRequested();
            LoadError = string.Empty;
            if (Client is null || Room is null || string.IsNullOrWhiteSpace(RootEventId))
                return;

            IsLoading = true;
            var session = await ChatSession.CreateAsync(
                Client,
                Room,
                token,
                new TimelineFocus.Thread(RootEventId)
            );
            Session = session;
            await session.LoadMoreHistoryAsync(cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                LoadError = exception.Message;
            System.Diagnostics.Debug.WriteLine($"Could not open thread: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                IsLoading = false;
        }
    }

    private void Close()
    {
        IsOpen = false;
        RootEventId = null;
    }

    [RelayCommand]
    private Task SendAsync() => Session?.SendMessageAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task LoadMoreAsync() => Session?.LoadMoreHistoryAsync() ?? Task.CompletedTask;

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
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }

    [RelayCommand]
    private void OpenMedia(ChatMedia? media) => FullscreenMedia = media;

    [RelayCommand]
    private void CloseMedia() => FullscreenMedia = null;

    [RelayCommand]
    private Task VotePollAsync(ChatPollVote? vote) =>
        vote is not null && Session is not null
            ? Session.VoteInPollAsync(vote.Item, vote.AnswerId)
            : Task.CompletedTask;

    [RelayCommand]
    private async Task ReactAsync(ChatTimelineItem? item)
    {
        if (Session is not null && item is not null && Shell.Current?.CurrentPage is { } page)
            await page.ShowPopupAsync(new EmojiPickerPopup(Session, item));
    }

    [RelayCommand]
    private async Task MenuAsync(ChatTimelineItem? item)
    {
        if (item?.EventId is null || Room is null || Shell.Current?.CurrentPage is not { } page)
            return;
        var actions = new List<string> { "Reply" };
        if (item.IsOwn)
            actions.Add("Edit");
        actions.Add("React");
        actions.Add("Copy text");
        actions.Add("Copy link");
        if (Session is not null && await Session.CanDeleteAsync(item))
            actions.Add("Delete");
        var action = await InAppDialogs.ChooseAsync(
            page,
            "Thread message actions",
            actions,
            item.Sender
        );
        if (action == "Reply")
            Session?.ReplyTo(item);
        else if (action == "Edit")
            Session?.Edit(item);
        else if (action == "React")
            await ReactAsync(item);
        else if (action == "Delete")
            await (Session?.DeleteAsync(item) ?? Task.FromResult(false));
        else if (action == "Copy text")
            await Clipboard.Default.SetTextAsync(item.Body);
        else if (action == "Copy link")
            await Clipboard.Default.SetTextAsync(await Room.MatrixToEventPermalink(item.EventId));
    }

    [RelayCommand]
    private async Task AttachAsync()
    {
        if (Session is null || await FilePicker.Default.PickAsync() is not { } file)
            return;
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        await Session.SendAttachmentAsync(
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            memory.ToArray()
        );
    }

    private async Task DisposeSessionAsync()
    {
        if (Session is null)
            return;
        await Session.DisposeAsync();
        Session = null;
    }
}
