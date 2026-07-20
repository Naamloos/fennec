using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Models;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.ViewModels;

public sealed partial class SettingsViewModel(MatrixService matrixService) : ObservableObject, IDisposable
{
    private SessionVerificationController? _verification;
    private VerificationDelegate? _verificationDelegate;

    public ObservableCollection<MatrixDevice> Devices { get; } = [];
    public ObservableCollection<VerificationEmoji> VerificationEmojis { get; } = [];

    [ObservableProperty] public partial string UserId { get; set; } = string.Empty;
    [ObservableProperty] public partial string DeviceId { get; set; } = string.Empty;
    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty] public partial ImageSource? Avatar { get; set; }
    [ObservableProperty] public partial string VerificationStatus { get; set; } = "Unknown";
    [ObservableProperty] public partial string VerificationMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsVerificationModalOpen { get; set; }
    [ObservableProperty] public partial bool HasIncomingVerification { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasVerificationMessage => !string.IsNullOrWhiteSpace(VerificationMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnVerificationMessageChanged(string value) => OnPropertyChanged(nameof(HasVerificationMessage));

    public async Task InitializeAsync()
    {
        if (matrixService.Client is not { } client) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            UserId = client.UserId() ?? string.Empty;
            DeviceId = client.DeviceId();
            DisplayName = await client.DisplayName();

            using var encryption = client.Encryption();
            await encryption.WaitForE2eeInitializationTasks();
            VerificationStatus = encryption.VerificationState().ToString();

            _verification ??= await client.GetSessionVerificationController();
            _verificationDelegate ??= new VerificationDelegate(this);
            _verification.SetDelegate(_verificationDelegate);

            Devices.Clear();
            foreach (var device in await matrixService.GetDevicesAsync()) Devices.Add(device);

            if (await client.AvatarUrl() is { } avatarUrl)
            {
                var bytes = await matrixService.GetThumbnailAsync(avatarUrl, 128, 128);
                Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (matrixService.Client is not { } client) return;
        await RunAsync(async () => await client.SetDisplayName(DisplayName.Trim()));
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose a profile picture",
            FileTypes = FilePickerFileType.Images
        });
        if (file is null) return;
        await RunAsync(async () =>
        {
            await using var stream = await file.OpenReadAsync();
            using var output = new MemoryStream();
            await stream.CopyToAsync(output);
            if (output.Length > 10 * 1024 * 1024)
                throw new InvalidOperationException("Profile pictures must be 10 MB or smaller.");
            var bytes = output.ToArray();
            await matrixService.SetProfileAvatarAsync(bytes, file.ContentType ?? "image/jpeg");
            Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        });
    }

    [RelayCommand]
    private async Task ManageAccountAsync() =>
        await OpenAccountUrlAsync(new AccountManagementAction.Profile());

    [RelayCommand]
    private async Task SaveDeviceAsync(MatrixDevice device) =>
        await RunAsync(() => matrixService.UpdateDeviceNameAsync(device.DeviceId, device.DisplayName.Trim()));

    [RelayCommand]
    private async Task ManageDeviceAsync(MatrixDevice device) =>
        await OpenAccountUrlAsync(new AccountManagementAction.DeviceView(device.DeviceId));

    [RelayCommand]
    private async Task StartVerificationAsync()
    {
        if (_verification is null) return;
        VerificationMessage = "Waiting for another verified device…";
        await RunAsync(_verification.RequestDeviceVerification);
    }

    [RelayCommand]
    private async Task AcceptVerificationAsync()
    {
        if (_verification is null) return;
        HasIncomingVerification = false;
        await RunAsync(_verification.AcceptVerificationRequest);
    }

    [RelayCommand]
    private async Task ApproveVerificationAsync()
    {
        if (_verification is null) return;
        await RunAsync(_verification.ApproveVerification);
        IsVerificationModalOpen = false;
    }

    [RelayCommand]
    private async Task DeclineVerificationAsync()
    {
        if (_verification is null) return;
        await RunAsync(_verification.DeclineVerification);
        CloseVerification();
    }

    [RelayCommand]
    private void CloseVerification()
    {
        IsVerificationModalOpen = false;
        HasIncomingVerification = false;
        VerificationEmojis.Clear();
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try { await action(); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private async Task OpenAccountUrlAsync(AccountManagementAction action)
    {
        if (matrixService.Client is not { } client) return;
        await RunAsync(async () =>
        {
            var url = await client.AccountUrl(action)
                ?? throw new InvalidOperationException("This homeserver does not provide account management.");
            await Launcher.Default.OpenAsync(url);
        });
    }

    private void Post(System.Action action) => MainThread.BeginInvokeOnMainThread(action);

    private async Task HandleIncomingAsync(SessionVerificationRequestDetails details)
    {
        if (_verification is null) return;
        try
        {
            await _verification.AcknowledgeVerificationRequest(details.SenderProfile.UserId, details.FlowId);
            Post(() =>
            {
                VerificationMessage = $"Verification requested by {details.DeviceDisplayName ?? details.DeviceId}.";
                HasIncomingVerification = true;
            });
        }
        catch (Exception exception) { Post(() => ErrorMessage = exception.Message); }
    }

    private sealed class VerificationDelegate(SettingsViewModel owner) : SessionVerificationControllerDelegate
    {
        public void DidReceiveVerificationRequest(SessionVerificationRequestDetails details) =>
            _ = owner.HandleIncomingAsync(details);

        public void DidAcceptVerificationRequest()
        {
            owner.Post(() =>
            {
                owner.VerificationMessage = "Starting emoji verification…";
                if (owner._verification is { } controller) _ = owner.RunAsync(controller.StartSasVerification);
            });
        }

        public void DidStartSasVerification() =>
            owner.Post(() => owner.VerificationMessage = "Compare the emoji on both devices.");

        public void DidReceiveVerificationData(SessionVerificationData data)
        {
            if (data is SessionVerificationData.Emojis emojis)
            {
                var values = emojis.EmojisValue
                    .Select(emoji => new VerificationEmoji(emoji.Symbol(), emoji.Description()))
                    .ToArray();
                owner.Post(() =>
                {
                    owner.VerificationEmojis.Clear();
                    foreach (var value in values) owner.VerificationEmojis.Add(value);
                    owner.IsVerificationModalOpen = true;
                });
            }
            data.Dispose();
        }

        public void DidFail() => owner.Post(() => owner.VerificationMessage = "Verification failed.");
        public void DidCancel() => owner.Post(() => { owner.VerificationMessage = "Verification cancelled."; owner.CloseVerification(); });
        public void DidFinish() => owner.Post(() =>
        {
            owner.VerificationStatus = "Verified";
            owner.VerificationMessage = "This session is verified.";
            owner.CloseVerification();
        });
    }

    public void Stop()
    {
        _verification?.SetDelegate(null);
        _verification?.Dispose();
        _verification = null;
        _verificationDelegate = null;
        CloseVerification();
    }

    public void Dispose() => Stop();
}
