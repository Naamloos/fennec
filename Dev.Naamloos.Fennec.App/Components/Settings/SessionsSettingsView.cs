using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class SessionsSettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient), typeof(ManagedMatrixClient), typeof(SessionsSettingsView));
    public static readonly BindableProperty VerificationServiceProperty = BindableProperty.Create(
        nameof(VerificationService), typeof(SessionVerificationService), typeof(SessionsSettingsView));

    private readonly VerticalStackLayout _sessions = new() { Spacing = 8 };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IDispatcherTimer _refreshTimer;

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public SessionVerificationService? VerificationService
    {
        get => (SessionVerificationService?)GetValue(VerificationServiceProperty);
        set => SetValue(VerificationServiceProperty, value);
    }

    public SessionsSettingsView()
    {
        this.BindService<ManagedMatrixClient, SessionsSettingsView>(MatrixClientProperty)
            .BindService<SessionVerificationService, SessionsSettingsView>(VerificationServiceProperty);

        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(15);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _refreshTimer.Stop();

        Content = new SettingsSection("Sessions",
            ActionButton("Refresh sessions")
                .BindCommand(nameof(RefreshCommand), source: this),
            _sessions);
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return RefreshSessionsAsync();
    }

    private async Task RefreshSessionsAsync()
    {
        if (MatrixClient is null || !await _refreshLock.WaitAsync(0)) return;

        try
        {
            var sessions = await MatrixClient.GetSessionsAsync();
            _sessions.Children.Clear();
            foreach (var session in sessions)
            {
                _sessions.Children.Add(CreateSessionView(session));
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private View CreateSessionView(MatrixSession session)
    {
        var current = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        VerificationIcon(session),
                        new Label { Text = "This session", FontAttributes = FontAttributes.Bold },
                    },
                },
                new Label { Text = session.DisplayName },
                ActionButton("Change device name")
                    .BindCommand(nameof(RenameSessionCommand), source: this)
                    .Bind(Button.CommandParameterProperty, ".", source: session),
            },
        };
        var other = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        VerificationIcon(session),
                        new Label { Text = session.DisplayName, FontAttributes = FontAttributes.Bold },
                    },
                },
                new Label { Text = session.DeviceId, Opacity = .7, FontSize = 12 },
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        ActionButton("Verify")
                            .BindCommand(nameof(VerifySessionCommand), source: this)
                            .Bind(Button.CommandParameterProperty, ".", source: session),
                        new Button { Text = "Remove", TextColor = Colors.Red, BackgroundColor = Colors.Transparent }
                            .BindCommand(nameof(RemoveSessionCommand), source: this)
                            .Bind(Button.CommandParameterProperty, ".", source: session),
                    },
                },
            },
        };
        var view = new TemplateSwitchView<MatrixSession, bool>(value => value.IsCurrent)
            .Add(value => value, current)
            .Add(value => !value, other);
        view.Value = session;
        return new Border
        {
            Padding = 12,
            StrokeThickness = session.IsVerified ? 0 : 1,
            Stroke = session.IsVerified ? Colors.Transparent : Colors.Red,
            Content = view,
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2");
    }

    [RelayCommand]
    private async Task RenameSessionAsync(MatrixSession? session)
    {
        if (session is null || MatrixClient is null) return;
        var name = await Shell.Current.DisplayPromptAsync(
            "Device name", "Give this session a recognizable name.", initialValue: session.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) return;

        await MatrixClient.RenameSessionAsync(session.DeviceId, name.Trim());
        await RefreshSessionsAsync();
    }

    [RelayCommand]
    private async Task VerifySessionAsync(MatrixSession? session)
    {
        if (session is null || VerificationService is null) return;
        await VerificationService.InitializeAsync();
        await VerificationService.RequestVerificationAsync();
    }

    [RelayCommand]
    private async Task RemoveSessionAsync(MatrixSession? session)
    {
        if (session is null || MatrixClient is null) return;
        if (!await Shell.Current.DisplayAlert("Remove session", $"Remove {session.DisplayName}?", "Remove", "Cancel")) return;

        var result = await Shell.Current.ShowPopupAsync<string?>(new PasswordConfirmationPopup(
            "Verify password",
            "Enter your password to remove this session.",
            "Remove"));
        if (string.IsNullOrWhiteSpace(result.Result)) return;

        await MatrixClient.RemoveSessionAsync(session.DeviceId, result.Result);
        await RefreshSessionsAsync();
    }

    private static Button ActionButton(string text) => new Button
    {
        Text = text,
        BackgroundColor = Colors.Transparent,
        Padding = new Thickness(10, 4),
        FontSize = 12,
    }.DynamicResource(Button.TextColorProperty, "Primary");

    private static MauiIcon VerificationIcon(MatrixSession session) => new()
    {
        Icon = session.IsVerified ? MaterialIcons.Lock : MaterialIcons.LockOpen,
        IconSize = 16,
        IconColor = session.IsVerified ? Colors.Green : Colors.Red,
        AutomationId = session.IsVerified ? "Verified device" : "Unverified device",
    };
}
