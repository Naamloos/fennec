using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView, IDisposable
{
    // Services
    private readonly ManagedMatrixClient _matrixClient;

    // UI
    private readonly CollectionView _timelineView;
    private readonly Entry _messageEntry;
    private readonly Grid _loadingOverlay;

    // State
    private ObservableTimeline? _observableTimeline;
    private Room? _room;

    private bool _isLoading;
    private bool _isSending;
    private bool _disposed;

    // Backing fields
    private string _messageText = string.Empty;
    private string _errorMessage = string.Empty;

    // Binding properties
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string MessageText
    {
        get => _messageText;
        set
        {
            if (_messageText == value)
            {
                return;
            }

            _messageText = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            _loadingOverlay.IsVisible = value;
            _loadingOverlay.InputTransparent = !value;

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (_isSending == value)
            {
                return;
            }

            _isSending = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanSend =>
        !IsLoading &&
        !IsSending &&
        _observableTimeline is not null &&
        !string.IsNullOrWhiteSpace(MessageText);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public Chat(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        BindingContext = this;

        _timelineView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsSource = Messages,
            ItemTemplate = new DataTemplate(CreateMessageItem),
            EmptyView = CreateEmptyView(),
            ItemsLayout = new LinearItemsLayout(
                ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 4,
            },
        };

        _messageEntry = new Entry
        {
            Placeholder = "Message",
            ReturnType = ReturnType.Send,
        };

        _messageEntry.SetBinding(
            Entry.TextProperty,
            new Binding(
                nameof(MessageText),
                source: this,
                mode: BindingMode.TwoWay));

        _messageEntry.Completed += OnMessageEntryCompleted;

        _loadingOverlay = new Grid
        {
            IsVisible = false,
            InputTransparent = true,
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
        };

        Build();
    }

    public async Task SetRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_room?.Id() == room.Id() &&
            _observableTimeline is not null)
        {
            return;
        }

        Debug.WriteLine(
            $"Opening timeline for {room.Id()}");

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            DisposeTimeline();

            _room = room;

            Messages.Clear();

            cancellationToken.ThrowIfCancellationRequested();

            Debug.WriteLine(
                "Creating ObservableTimeline...");

            var observableTimeline =
                await _matrixClient.GetObservableTimelineAsync(room);

            cancellationToken.ThrowIfCancellationRequested();

            if (_disposed)
            {
                observableTimeline.Dispose();
                return;
            }

            _observableTimeline = observableTimeline;

            _observableTimeline.CollectionChanged +=
                OnTimelineCollectionChanged;

            Debug.WriteLine(
                "ObservableTimeline created.");

            RebuildMessages();

            Debug.WriteLine(
                $"Timeline loaded with {_observableTimeline.Count} items.");

            await ScrollToBottomAsync();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine(
                $"Timeline loading cancelled for {room.Id()}.");

            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Failed to load timeline for {room.Id()}: {exception}");
        }
        finally
        {
            Debug.WriteLine(
                "Timeline loading completed.");

            IsLoading = false;

            OnPropertyChanged(nameof(CanSend));
            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (_observableTimeline is null)
        {
            return;
        }

        var text = MessageText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            var content = CreateTextMessageContent(
                _observableTimeline.Timeline,
                text);

            await _observableTimeline.Timeline.Send(content);

            MessageText = string.Empty;

            _messageEntry.Focus();

            await ScrollToBottomAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Failed to send message to {_room?.Id()}: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    private static RoomMessageEventContentWithoutRelation
        CreateTextMessageContent(
            Timeline timeline,
            string text)
    {
        var content = timeline.CreateMessageContent(
            new MessageType.Text(
                new TextMessageContent(
                    text,
                    null)));

        return content
            ?? throw new InvalidOperationException(
                "Could not create Matrix text-message content.");
    }

    private void OnMessageEntryCompleted(
        object? sender,
        EventArgs e)
    {
        if (SendMessageCommand.CanExecute(null))
        {
            SendMessageCommand.Execute(null);
        }
    }

    private void OnTimelineCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        RebuildMessages();

        _ = ScrollToBottomAsync();
    }

    private void RebuildMessages()
    {
        if (_observableTimeline is null)
        {
            Messages.Clear();
            return;
        }

        var parsedMessages = _observableTimeline
            .Select(TryCreateChatMessage)
            .Where(message => message is not null)
            .Cast<ChatMessage>()
            .ToArray();

        Messages.Clear();

        foreach (var message in parsedMessages)
        {
            Messages.Add(message);
        }
    }

    private static ChatMessage? TryCreateChatMessage(
        TimelineItem timelineItem)
    {
        var eventItem = timelineItem.AsEvent();

        if (eventItem is null)
        {
            return null;
        }

        if (eventItem.Content is not TimelineItemContent.MsgLike msgLike)
        {
            return null;
        }

        if (msgLike.Content.Kind is not MsgLikeKind.Message message)
        {
            return null;
        }

        var username = ResolveUsername(eventItem);

        return new ChatMessage(
            timelineItem.UniqueId().Id,
            username,
            message.Content.Body,
            eventItem.IsOwn);
    }

    private static string ResolveUsername(
        EventTimelineItem eventItem)
    {
        if (eventItem.SenderProfile is ProfileDetails.Ready ready &&
            !string.IsNullOrWhiteSpace(ready.DisplayName))
        {
            return ready.DisplayName;
        }

        return eventItem.Sender;
    }

    private async Task ScrollToBottomAsync()
    {
        if (Messages.Count == 0)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed || Messages.Count == 0)
            {
                return;
            }

            _timelineView.ScrollTo(
                Messages[^1],
                position: ScrollToPosition.End,
                animate: false);
        });
    }

    private void Build()
    {
        var sendButton = new Button
        {
            Text = "Send",
            VerticalOptions = LayoutOptions.Center,
        };

        sendButton.SetBinding(
            Button.CommandProperty,
            new Binding(
                nameof(SendMessageCommand),
                source: this));

        var composer = new Grid
        {
            Padding = 12,
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        composer.Add(_messageEntry, column: 0);
        composer.Add(sendButton, column: 1);

        var errorLabel = new Label
        {
            TextColor = Colors.Red,
            Margin = new Thickness(12, 4),
            LineBreakMode = LineBreakMode.WordWrap,
        };

        errorLabel.SetBinding(
            Label.TextProperty,
            new Binding(
                nameof(ErrorMessage),
                source: this));

        errorLabel.SetBinding(
            IsVisibleProperty,
            new Binding(
                nameof(HasError),
                source: this));

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        layout.Add(_timelineView, row: 0);
        layout.Add(errorLabel, row: 1);
        layout.Add(composer, row: 2);

        layout.AddWithSpan(
            _loadingOverlay,
            row: 0,
            column: 0,
            rowSpan: 3,
            columnSpan: 1);

        layout.SafeAreaEdges = SafeAreaEdges.All;

        Content = layout;
    }

    private static View CreateMessageItem()
    {
        var usernameLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            LineBreakMode = LineBreakMode.TailTruncation,
        }.Bind(
            Label.TextProperty,
            nameof(ChatMessage.Username),
            mode: BindingMode.OneWay);

        var separator = new Label
        {
            Text = ":",
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
        };

        var bodyLabel = new Label
        {
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        }.Bind(
            Label.TextProperty,
            nameof(ChatMessage.Body),
            mode: BindingMode.OneWay);

        var messageGrid = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        messageGrid.Add(usernameLabel, column: 0);
        messageGrid.Add(separator, column: 1);
        messageGrid.Add(bodyLabel, column: 2);

        return new Border
        {
            Margin = new Thickness(12, 2),
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 8,
            },
            Content = messageGrid,
        };
    }

    private static View CreateEmptyView()
    {
        return new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = "No messages yet",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new Label
                {
                    Text = "Send the first message.",
                    Opacity = 0.7,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    private void DisposeTimeline()
    {
        if (_observableTimeline is null)
        {
            return;
        }

        _observableTimeline.CollectionChanged -=
            OnTimelineCollectionChanged;

        _observableTimeline.Dispose();
        _observableTimeline = null;

        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _messageEntry.Completed -=
            OnMessageEntryCompleted;

        DisposeTimeline();

        _loadingOverlay.IsVisible = false;
        _loadingOverlay.InputTransparent = true;

        Messages.Clear();

        _room = null;

        GC.SuppressFinalize(this);
    }
}

public sealed record ChatMessage(
    string Id,
    string Username,
    string Body,
    bool IsOwn);