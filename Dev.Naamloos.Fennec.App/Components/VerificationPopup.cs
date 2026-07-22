using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class VerificationPopup : Popup, SessionVerificationControllerDelegate
{
#if DEBUG
    static VerificationPopup() =>
        Debug.Assert(FormatDecimals([1, 2, 3]) == "1   2   3");
#endif

    private readonly SessionVerificationController _controller;
    private readonly Label _status = new()
    {
        Text = "Waiting for another signed-in session…",
        HorizontalTextAlignment = TextAlignment.Center,
    };
    private readonly Label _sas = new()
    {
        FontSize = 20,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false,
    };
    private readonly HorizontalStackLayout _actions;
    private bool _started;

    public VerificationPopup(SessionVerificationController controller)
    {
        _controller = controller;
        _controller.SetDelegate(this);

        var match = new Button { Text = "They match" };
        var mismatch = new Button { Text = "No match" };
        match.Clicked += Approve;
        mismatch.Clicked += Decline;

        _actions = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { mismatch, match },
        };

        var cancel = new Button { Text = "Cancel" };
        cancel.Clicked += Cancel;

        var card = new Border
        {
            Padding = 24,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            MaximumWidthRequest = 440,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Verify encrypted messages",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center,
                    },
                    new Label
                    {
                        Text = "Open Fennec on another verified session and compare the symbols shown on both devices.",
                        HorizontalTextAlignment = TextAlignment.Center,
                    },
                    _status,
                    _sas,
                    _actions,
                    cancel,
                },
            },
        };
        card.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "Surface");

        Content = card;
        CanBeDismissedByTappingOutsideOfPopup = false;
        Loaded += Start;
        Unloaded += Cleanup;
    }

    private async void Start(object? sender, EventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await Run(_controller.RequestDeviceVerification);
    }

    public void DidReceiveVerificationRequest(
        SessionVerificationRequestDetails details) => _ = Run(async () =>
    {
        SetStatus($"Verification requested by {details.DeviceDisplayName ?? details.DeviceId}.");
        await _controller.AcknowledgeVerificationRequest(
            details.SenderProfile.UserId,
            details.FlowId);
        await _controller.AcceptVerificationRequest();
    });

    public void DidAcceptVerificationRequest() =>
        _ = Run(_controller.StartSasVerification);

    public void DidStartSasVerification() =>
        SetStatus("Compare the symbols on both sessions.");

    public void DidReceiveVerificationData(SessionVerificationData data)
    {
        var text = data switch
        {
            SessionVerificationData.Emojis emojis => string.Join(
                "   ",
                emojis.EmojisValue.Select(emoji => emoji.Symbol())),
            SessionVerificationData.Decimals decimals =>
                FormatDecimals(decimals.Values),
            _ => string.Empty,
        };
        data.Dispose();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _sas.Text = text;
            _sas.IsVisible = true;
            _actions.IsVisible = true;
        });
    }

    public void DidFail() => Complete("Verification failed.");
    public void DidCancel() => Complete("Verification was cancelled.");
    public void DidFinish() => Complete("Session verified.");

    private static string FormatDecimals(IEnumerable<ushort> values) =>
        string.Join("   ", values);

    private async void Approve(object? sender, EventArgs e) =>
        await Run(_controller.ApproveVerification);

    private async void Decline(object? sender, EventArgs e) =>
        await Run(_controller.DeclineVerification);

    private async void Cancel(object? sender, EventArgs e)
    {
        await Run(_controller.CancelVerification);
        await CloseAsync();
    }

    private void SetStatus(string text) =>
        MainThread.BeginInvokeOnMainThread(() => _status.Text = text);

    private void Complete(string text) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _status.Text = text;
            _sas.IsVisible = false;
            _actions.IsVisible = false;
        });

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            SetStatus("Verification failed.");
        }
    }

    private void Cleanup(object? sender, EventArgs e)
    {
        _controller.SetDelegate(null);
        _controller.Dispose();
        Loaded -= Start;
        Unloaded -= Cleanup;
    }
}
