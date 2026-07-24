using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class VerificationPopup :
    ContentView,
    SessionVerificationControllerDelegate
{
#if DEBUG
    static VerificationPopup() =>
        Debug.Assert(
            FormatDecimals([1, 2, 3]) == "1   2   3");
#endif

    private bool _started;

    [BindableProperty(PropertyChangedMethodName = nameof(OnControllerChanged))]
    public partial SessionVerificationController? Controller { get; set; }

    [BindableProperty]
    public partial string Status { get; set; } =
        "Waiting for another signed-in session…";

    [BindableProperty]
    public partial string Sas { get; set; } = string.Empty;

    [BindableProperty]
    public partial bool ShowSas { get; set; }

    [BindableProperty]
    public partial bool ShowActions { get; set; }

    public VerificationPopup()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new Border
        {
            Padding = 24,
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            MaximumWidthRequest = 440,
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Loaded),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(StartCommand)),
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Unloaded),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(CleanupCommand)),
            },
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
                    new Label
                    {
                        HorizontalTextAlignment = TextAlignment.Center,
                    }.Bind(Label.TextProperty, nameof(Status)),
                    new Label
                    {
                        FontSize = 20,
                        HorizontalTextAlignment = TextAlignment.Center,
                    }
                    .Bind(Label.TextProperty, nameof(Sas))
                    .Bind(IsVisibleProperty, nameof(ShowSas)),
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        HorizontalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Button { Text = "No match" }
                                .BindCommand(nameof(DeclineCommand)),
                            new Button { Text = "They match" }
                                .BindCommand(nameof(ApproveCommand)),
                        },
                    }.Bind(IsVisibleProperty, nameof(ShowActions)),
                    new Button { Text = "Cancel" }
                        .BindCommand(nameof(CancelCommand)),
                },
            },
        }.DynamicResource(
            BackgroundColorProperty,
            "Surface");
    }

    private static void OnControllerChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (oldValue is SessionVerificationController oldController)
        {
            oldController.SetDelegate(null);
        }

        if (newValue is SessionVerificationController newController)
        {
            newController.SetDelegate(
                (VerificationPopup)bindable);
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_started ||
            Controller is null)
        {
            return;
        }

        _started = true;
        await Run(Controller.RequestDeviceVerification);
    }

    public void DidReceiveVerificationRequest(
        SessionVerificationRequestDetails details) =>
        _ = Run(async () =>
        {
            SetStatus(
                $"Verification requested by {details.DeviceDisplayName ?? details.DeviceId}.");

            if (Controller is null)
            {
                return;
            }

            await Controller.AcknowledgeVerificationRequest(
                details.SenderProfile.UserId,
                details.FlowId);
            await Controller.AcceptVerificationRequest();
        });

    public void DidAcceptVerificationRequest()
    {
        if (Controller is not null)
        {
            _ = Run(Controller.StartSasVerification);
        }
    }

    public void DidStartSasVerification() =>
        SetStatus("Compare the symbols on both sessions.");

    public void DidReceiveVerificationData(
        SessionVerificationData data)
    {
        var text = data switch
        {
            SessionVerificationData.Emojis emojis =>
                string.Join(
                    "   ",
                    emojis.EmojisValue.Select(
                        emoji => emoji.Symbol())),
            SessionVerificationData.Decimals decimals =>
                FormatDecimals(decimals.Values),
            _ => string.Empty,
        };
        data.Dispose();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Sas = text;
            ShowSas = true;
            ShowActions = true;
        });
    }

    public void DidFail() =>
        Complete("Verification failed.");

    public void DidCancel() =>
        Complete("Verification was cancelled.");

    public void DidFinish() =>
        Complete("Session verified.");

    private static string FormatDecimals(
        IEnumerable<ushort> values) =>
        string.Join("   ", values);

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (Controller is not null)
        {
            await Run(Controller.ApproveVerification);
        }
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        if (Controller is not null)
        {
            await Run(Controller.DeclineVerification);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (Controller is not null)
        {
            await Run(Controller.CancelVerification);
        }

        if (Parent is Popup popup)
        {
            await popup.CloseAsync();
        }
    }

    private void SetStatus(string text) =>
        MainThread.BeginInvokeOnMainThread(
            () => Status = text);

    private void Complete(string text) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Status = text;
            ShowSas = false;
            ShowActions = false;
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

    [RelayCommand]
    private void Cleanup()
    {
        Controller?.SetDelegate(null);
        Controller?.Dispose();
        Controller = null;
    }
}
