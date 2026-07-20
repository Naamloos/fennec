using Dev.Naamloos.Fennec.App.Models;
using Dev.Naamloos.Fennec.App.Controls;

namespace Dev.Naamloos.Fennec.App.Pages;

public partial class MainPage : ContentPage
{
    private readonly ChatViewModel _viewModel;
    private bool _eventsAttached;
    private bool _stayAtBottom = true;

    public MainPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_eventsAttached)
        {
            _viewModel.RoomOpened += OnRoomOpened;
            _viewModel.MessagesUpdated += OnMessagesUpdated;
            _viewModel.ErrorOccurred += OnErrorOccurred;
            _viewModel.ModerationRequested += OnModerationRequested;
            _viewModel.JoinRoomRequested += OnJoinRoomRequested;
            _viewModel.RedactRequested += OnRedactRequested;
            _eventsAttached = true;
        }
        _viewModel.EnsureAuthenticated();
        OnMessagesUpdated(this, EventArgs.Empty);
    }

    protected override void OnDisappearing()
    {
        if (_eventsAttached)
        {
            _viewModel.RoomOpened -= OnRoomOpened;
            _viewModel.MessagesUpdated -= OnMessagesUpdated;
            _viewModel.ErrorOccurred -= OnErrorOccurred;
            _viewModel.ModerationRequested -= OnModerationRequested;
            _viewModel.JoinRoomRequested -= OnJoinRoomRequested;
            _viewModel.RedactRequested -= OnRedactRequested;
            _eventsAttached = false;
        }
        base.OnDisappearing();
    }

    private void OnRoomOpened(object? sender, EventArgs e)
    {
        _stayAtBottom = true;
        MessagesCollectionView.KeepBottom(true);
    }

    private void OnMessagesUpdated(object? sender, EventArgs e)
    {
        if (_stayAtBottom && _viewModel.Messages.Count > 0)
            Dispatcher.Dispatch(() => MessagesCollectionView.ScrollToEnd(_viewModel.Messages.Count - 1));
    }

    private async void OnErrorOccurred(object? sender, string message) =>
        await DisplayAlertAsync("Matrix error", message, "OK");

    private async void OnModerationRequested(object? sender, ModerationRequest request)
    {
        var action = request.Ban ? "Ban" : "Kick";
        if (!await DisplayAlertAsync(action, $"{action} {request.DisplayName} from this room?", action, "Cancel"))
            return;
        try { await _viewModel.ModerateAsync(request); }
        catch (Exception exception) { await DisplayAlertAsync("Moderation failed", exception.Message, "OK"); }
    }

    private async void OnJoinRoomRequested(object? sender, RoomListItem room)
    {
        if (!await DisplayAlertAsync("Join channel", $"Join {room.Name}?", "Join", "Cancel")) return;
        try { await _viewModel.JoinRoomAsync(room); }
        catch (Exception exception) { await DisplayAlertAsync("Could not join", exception.Message, "OK"); }
    }

    private async void OnRedactRequested(object? sender, ChatMessage message)
    {
        if (!await DisplayAlertAsync("Delete message", "Delete this event for everyone?", "Delete", "Cancel")) return;
        try { await _viewModel.RedactAsync(message); }
        catch (Exception exception) { await DisplayAlertAsync("Could not delete", exception.Message, "OK"); }
    }

    private void OnMessagesScrolled(object? sender, ChatScrollEventArgs e)
    {
        if (e.IsNearBottom) _stayAtBottom = true;
        else if (e.Delta < -1) _stayAtBottom = false;
        MessagesCollectionView.KeepBottom(_stayAtBottom);
        if (_stayAtBottom) _ = _viewModel.MarkAsReadAsync();
        if (!_stayAtBottom && e.IsNearTop)
            _ = _viewModel.LoadOlderMessagesAsync();
    }
}
