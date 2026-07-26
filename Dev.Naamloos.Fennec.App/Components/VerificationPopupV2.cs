using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public partial class VerificationPopupV2 :
    ContentView,
    SessionVerificationControllerDelegate
{
    public enum VerificationState
    {
        Listening,

        // Incoming request
        AcknowledgingRequest,
        AwaitingUserAcceptance,
        AcceptingRequest,

        // Outgoing request
        RequestingVerification,
        WaitingForOtherSession,

        // Shared verification flow
        StartingSas,
        WaitingForVerificationData,
        Comparing,
        Approving,
        Declining,
        Cancelling,

        // Terminal states
        Completed,
        Cancelled,
        Failed,
    }

    public sealed record VerificationEmojiItem(
        string Symbol,
        string Description);

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial VerificationState State { get; set; }
        = VerificationState.Listening;

    [BindableProperty]
    public partial string Status { get; set; }
        = "Waiting for verification requests...";

    [BindableProperty]
    public partial string? DeviceName { get; set; }

    [BindableProperty]
    public partial SessionVerificationData? Data { get; set; }

    [BindableProperty]
    public partial IReadOnlyList<VerificationEmojiItem> Emojis { get; set; }
        = Array.Empty<VerificationEmojiItem>();

    [BindableProperty]
    public partial IReadOnlyList<ushort> Decimals { get; set; }
        = Array.Empty<ushort>();

    [BindableProperty]
    public partial bool ShowProgress { get; set; }

    [BindableProperty]
    public partial bool ShowRequestButtons { get; set; }

    [BindableProperty]
    public partial bool ShowVerificationData { get; set; }

    [BindableProperty]
    public partial bool ShowComparisonButtons { get; set; }

    [BindableProperty]
    public partial bool ShowCancelButton { get; set; }

    [BindableProperty]
    public partial bool ShowCloseButton { get; set; }

    private SessionVerificationController? _controller;
    private SessionVerificationRequestDetails? _pendingRequest;
    private Popup? _popup;

    private bool _isInitializing;
    private bool _isDisposed;
    private bool _isClosingPopup;
    private bool _initiatedLocally;

    public VerificationPopupV2()
    {
        WidthRequest = 1;
        HeightRequest = 1;
        MinimumWidthRequest = 1;
        MinimumHeightRequest = 1;
        Opacity = 0;
        InputTransparent = true;

        this.BindService<ManagedMatrixClient, VerificationPopupV2>(
            MatrixClientProperty);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        HandlerChanged += OnHandlerChanged;

        ApplyState(VerificationState.Listening);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName != MatrixClientProperty.PropertyName)
        {
            return;
        }

        if (MatrixClient is null)
        {
            DetachController();
            return;
        }

        if (Handler is not null)
        {
            _ = InitializeControllerAsync();
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        _isDisposed = false;
        await InitializeControllerAsync();
    }

    private async void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (Handler is null)
        {
            return;
        }

        _isDisposed = false;
        await InitializeControllerAsync();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _isDisposed = true;

        DetachController();

        if (_popup is not null)
        {
            _ = ClosePopupAsync();
        }
    }

    private async Task InitializeControllerAsync()
    {
        if (_isDisposed ||
            _controller is not null ||
            _isInitializing)
        {
            return;
        }

        if (MatrixClient is null)
        {
            Debug.WriteLine(
                "Verification listener: MatrixClient is unavailable.");

            return;
        }

        _isInitializing = true;

        try
        {
            var controller =
                await MatrixClient.GetSessionVerificationControllerAsync();

            if (_isDisposed)
            {
                controller?.SetDelegate(null);
                return;
            }

            if (controller is null)
            {
                Debug.WriteLine(
                    "Verification listener: controller was null.");

                return;
            }

            _controller = controller;
            _controller.SetDelegate(this);

            Debug.WriteLine(
                "Verification listener: delegate registered.");

            ApplyState(VerificationState.Listening);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Verification listener initialization failed: {exception}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void DetachController()
    {
        if (_controller is null)
        {
            return;
        }

        _controller.SetDelegate(null);
        _controller = null;
    }

    [RelayCommand]
    public async Task StartVerificationAsync()
    {
        await InitializeControllerAsync();

        if (_controller is null)
        {
            Status = "Session verification is unavailable.";
            ApplyState(VerificationState.Failed);

            await EnsurePopupShownAsync();
            return;
        }

        if (!CanStartNewFlow())
        {
            await EnsurePopupShownAsync();
            return;
        }

        ResetFlow();

        _initiatedLocally = true;

        Status =
            "Sending a verification request to your other signed-in sessions...";

        ApplyState(VerificationState.RequestingVerification);

        await EnsurePopupShownAsync();

        try
        {
            await _controller.RequestDeviceVerification();

            RunOnMainThread(() =>
            {
                if (State != VerificationState.RequestingVerification)
                {
                    return;
                }

                Status =
                    "Waiting for another signed-in session to accept...";

                ApplyState(VerificationState.WaitingForOtherSession);
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to request device verification: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not send the verification request.";
                ApplyState(VerificationState.Failed);
            });
        }
    }

    [RelayCommand]
    private async Task AcceptRequestAsync()
    {
        if (_controller is null ||
            _pendingRequest is null ||
            State != VerificationState.AwaitingUserAcceptance)
        {
            return;
        }

        Status = "Accepting verification request...";
        ApplyState(VerificationState.AcceptingRequest);

        try
        {
            await _controller.AcceptVerificationRequest();

            RunOnMainThread(() =>
            {
                if (State != VerificationState.AcceptingRequest)
                {
                    return;
                }

                Status =
                    "Waiting for the other session to start verification...";

                ApplyState(VerificationState.WaitingForVerificationData);
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to accept verification request: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not accept the verification request.";
                ApplyState(VerificationState.AwaitingUserAcceptance);
            });
        }
    }

    [RelayCommand]
    private Task RejectRequestAsync()
    {
        if (_controller is null ||
            State != VerificationState.AwaitingUserAcceptance)
        {
            return Task.CompletedTask;
        }

        return CancelCurrentVerificationAsync();
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (_controller is null ||
            State != VerificationState.Comparing ||
            Data is null)
        {
            return;
        }

        Status = "Confirming that the values match...";
        ApplyState(VerificationState.Approving);

        try
        {
            await _controller.ApproveVerification();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to approve verification: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not confirm the verification.";
                ApplyState(VerificationState.Comparing);
            });
        }
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        if (_controller is null ||
            State != VerificationState.Comparing ||
            Data is null)
        {
            return;
        }

        Status = "Reporting that the values do not match...";
        ApplyState(VerificationState.Declining);

        try
        {
            await _controller.DeclineVerification();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to decline verification: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not reject the verification.";
                ApplyState(VerificationState.Comparing);
            });
        }
    }

    [RelayCommand]
    private Task CancelAsync()
    {
        return ShowCancelButton
            ? CancelCurrentVerificationAsync()
            : Task.CompletedTask;
    }

    private async Task CancelCurrentVerificationAsync()
    {
        Status = "Cancelling verification...";
        ApplyState(VerificationState.Cancelling);

        try
        {
            if (_controller is not null)
            {
                await _controller.CancelVerification();
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to cancel verification: {exception}");
        }

        RunOnMainThread(() =>
        {
            Status = "Verification was cancelled.";
            ApplyState(VerificationState.Cancelled);
        });
    }

    [RelayCommand]
    private Task CloseAsync()
    {
        return ClosePopupAsync();
    }

    private async Task AcknowledgeRequestAsync(
        SessionVerificationRequestDetails details)
    {
        if (_controller is null)
        {
            RunOnMainThread(() =>
            {
                Status = "Session verification is unavailable.";
                ApplyState(VerificationState.Failed);
            });

            return;
        }

        try
        {
            await _controller.AcknowledgeVerificationRequest(
                details.SenderProfile.UserId,
                details.FlowId);

            RunOnMainThread(() =>
            {
                if (_pendingRequest is null ||
                    _pendingRequest.FlowId != details.FlowId)
                {
                    return;
                }

                Status =
                    "Do you want to accept this verification request?";

                ApplyState(
                    VerificationState.AwaitingUserAcceptance);
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to acknowledge verification request: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not open the verification request.";
                ApplyState(VerificationState.Failed);
            });
        }
    }

    private async Task StartSasAsync()
    {
        if (_controller is null)
        {
            RunOnMainThread(() =>
            {
                Status = "Session verification is unavailable.";
                ApplyState(VerificationState.Failed);
            });

            return;
        }

        Status = "Starting secure emoji verification...";
        ApplyState(VerificationState.StartingSas);

        try
        {
            await _controller.StartSasVerification();

            RunOnMainThread(() =>
            {
                if (State != VerificationState.StartingSas)
                {
                    return;
                }

                Status = "Preparing verification values...";
                ApplyState(VerificationState.WaitingForVerificationData);
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to start SAS verification: {exception}");

            RunOnMainThread(() =>
            {
                Status = "Could not start emoji verification.";
                ApplyState(VerificationState.Failed);
            });
        }
    }

    private bool CanStartNewFlow()
    {
        return State is
            VerificationState.Listening or
            VerificationState.Completed or
            VerificationState.Cancelled or
            VerificationState.Failed;
    }

    private void ResetFlow()
    {
        Data = null;
        DeviceName = null;

        Emojis = Array.Empty<VerificationEmojiItem>();
        Decimals = Array.Empty<ushort>();

        _pendingRequest = null;
        _initiatedLocally = false;
    }

    private void ResetToListening()
    {
        ResetFlow();

        Status = "Waiting for verification requests...";
        ApplyState(VerificationState.Listening);
    }

    private Task EnsurePopupShownAsync()
    {
        if (_isDisposed || _popup is not null)
        {
            return Task.CompletedTask;
        }

        var page = FindCurrentPage();

        if (page is null)
        {
            Debug.WriteLine(
                "Verification popup: no active page found.");

            return Task.CompletedTask;
        }

        _popup = new Popup
        {
            BackgroundColor = Colors.Transparent,
            CanBeDismissedByTappingOutsideOfPopup = false,

            Content = new Border
            {
                Padding = 24,
                MinimumWidthRequest = 360,
                MaximumWidthRequest = 560,
                StrokeThickness = 0,

                StrokeShape = new RoundRectangle
                {
                    CornerRadius = 18,
                },

                Content = new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Spacing = 18,

                        Children =
                        {
                            new Label
                            {
                                Text = "Verify this session",
                                FontSize = 24,
                                FontAttributes = FontAttributes.Bold,
                                HorizontalTextAlignment = TextAlignment.Center,
                                HorizontalOptions = LayoutOptions.Fill,
                            },

                            new Label
                            {
                                FontSize = 14,
                                Opacity = 0.7,
                                HorizontalTextAlignment = TextAlignment.Center,
                                HorizontalOptions = LayoutOptions.Fill,
                            }
                            .Bind(
                                Label.TextProperty,
                                nameof(DeviceName),
                                source: this)
                            .Bind(
                                IsVisibleProperty,
                                nameof(DeviceName),
                                source: this,
                                convert: static (string? value) =>
                                    !string.IsNullOrWhiteSpace(value)),

                            new Label
                            {
                                FontSize = 17,
                                HorizontalTextAlignment = TextAlignment.Center,
                                HorizontalOptions = LayoutOptions.Fill,
                            }
                            .Bind(
                                Label.TextProperty,
                                nameof(Status),
                                source: this),

                            new ActivityIndicator
                            {
                                HorizontalOptions = LayoutOptions.Center,
                            }
                            .Bind(
                                IsVisibleProperty,
                                nameof(ShowProgress),
                                source: this)
                            .Bind(
                                ActivityIndicator.IsRunningProperty,
                                nameof(ShowProgress),
                                source: this),

                            new TemplateSwitchView<
                                SessionVerificationData,
                                SessionVerificationData>(data => data)
                                .Add(
                                    value =>
                                        value is SessionVerificationData.Emojis,
                                    EmojiVerificationDisplay())
                                .Add(
                                    value =>
                                        value is SessionVerificationData.Decimals,
                                    DecimalVerificationDisplay())
                                .Bind(
                                    TemplateSwitchView<
                                        SessionVerificationData,
                                        SessionVerificationData>.ValueProperty,
                                    nameof(Data),
                                    source: this)
                                .Bind(
                                    IsVisibleProperty,
                                    nameof(ShowVerificationData),
                                    source: this),

                            new HorizontalStackLayout
                            {
                                Spacing = 8,
                                HorizontalOptions = LayoutOptions.Center,

                                Children =
                                {
                                    new Button
                                    {
                                        Text = "Accept",
                                    }
                                    .BindCommand(
                                        nameof(AcceptRequestCommand),
                                        source: this)
                                    .Bind(
                                        IsEnabledProperty,
                                        nameof(ShowRequestButtons),
                                        source: this),

                                    new Button
                                    {
                                        Text = "Reject",
                                    }
                                    .BindCommand(
                                        nameof(RejectRequestCommand),
                                        source: this)
                                    .Bind(
                                        IsEnabledProperty,
                                        nameof(ShowRequestButtons),
                                        source: this),
                                },
                            }
                            .Bind(
                                IsVisibleProperty,
                                nameof(ShowRequestButtons),
                                source: this),

                            new HorizontalStackLayout
                            {
                                Spacing = 8,
                                HorizontalOptions = LayoutOptions.Center,

                                Children =
                                {
                                    new Button
                                    {
                                        Text = "They Match",
                                    }
                                    .BindCommand(
                                        nameof(ApproveCommand),
                                        source: this)
                                    .Bind(
                                        IsEnabledProperty,
                                        nameof(ShowComparisonButtons),
                                        source: this),

                                    new Button
                                    {
                                        Text = "They Don't Match",
                                    }
                                    .BindCommand(
                                        nameof(DeclineCommand),
                                        source: this)
                                    .Bind(
                                        IsEnabledProperty,
                                        nameof(ShowComparisonButtons),
                                        source: this),
                                },
                            }
                            .Bind(
                                IsVisibleProperty,
                                nameof(ShowComparisonButtons),
                                source: this),

                            new Button
                            {
                                Text = "Cancel",
                                HorizontalOptions = LayoutOptions.Center,
                            }
                            .BindCommand(
                                nameof(CancelCommand),
                                source: this)
                            .Bind(
                                IsVisibleProperty,
                                nameof(ShowCancelButton),
                                source: this)
                            .Bind(
                                IsEnabledProperty,
                                nameof(ShowCancelButton),
                                source: this),

                            new Button
                            {
                                Text = "Close",
                                HorizontalOptions = LayoutOptions.Center,
                            }
                            .BindCommand(
                                nameof(CloseCommand),
                                source: this)
                            .Bind(
                                IsVisibleProperty,
                                nameof(ShowCloseButton),
                                source: this)
                            .Bind(
                                IsEnabledProperty,
                                nameof(ShowCloseButton),
                                source: this),
                        },
                    },
                },
            }
            .DynamicResource(
                VisualElement.BackgroundColorProperty,
                "Surface"),
        };

        page.Dispatcher.Dispatch(async () =>
        {
            var popup = _popup;

            if (popup is null)
            {
                return;
            }

            try
            {
                await page.ShowPopupAsync(popup);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Unable to display verification popup: {exception}");
            }
            finally
            {
                if (ReferenceEquals(_popup, popup))
                {
                    _popup = null;
                }

                _isClosingPopup = false;

                if (IsTerminalState(State))
                {
                    ResetToListening();
                }
            }
        });

        return Task.CompletedTask;
    }

    private async Task ClosePopupAsync()
    {
        if (_popup is null || _isClosingPopup)
        {
            return;
        }

        _isClosingPopup = true;

        try
        {
            await _popup.CloseAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Unable to close verification popup: {exception}");

            _popup = null;
            _isClosingPopup = false;

            if (IsTerminalState(State))
            {
                ResetToListening();
            }
        }
    }

    private static Page? FindCurrentPage()
    {
        if (Shell.Current?.CurrentPage is { } shellPage)
        {
            return shellPage;
        }

        return Application.Current?
            .Windows
            .Select(window => window.Page)
            .FirstOrDefault(page => page is not null);
    }

    private ContentView EmojiVerificationDisplay()
    {
        return new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,

            Content = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Center,
                AlignItems = FlexAlignItems.Center,
                AlignContent = FlexAlignContent.Center,

                // 4 × 112px cards + 3 × 8px gaps.
                MaximumWidthRequest = (4 * 112) + (3 * 8),
            }
            .Bind(
                BindableLayout.ItemsSourceProperty,
                nameof(Emojis),
                source: this)
            .Invoke(layout =>
            {
                BindableLayout.SetItemTemplate(
                    layout,
                    new DataTemplate(() =>
                        new Border
                        {
                            WidthRequest = 112,
                            HeightRequest = 108,
                            Padding = new Thickness(8, 6),
                            Stroke = Brush.Transparent,

                            Content = new VerticalStackLayout
                            {
                                Spacing = 2,
                                VerticalOptions = LayoutOptions.Center,

                                Children =
                                {
                                new Label
                                {
                                    FontSize = 34,
                                    HorizontalOptions = LayoutOptions.Fill,
                                    HorizontalTextAlignment =
                                        TextAlignment.Center,
                                    VerticalTextAlignment =
                                        TextAlignment.Center,
                                }
                                .Bind(
                                    Label.TextProperty,
                                    nameof(VerificationEmojiItem.Symbol)),

                                new Label
                                {
                                    FontSize = 12,
                                    MaxLines = 2,
                                    HorizontalOptions = LayoutOptions.Fill,
                                    HorizontalTextAlignment =
                                        TextAlignment.Center,
                                    VerticalTextAlignment =
                                        TextAlignment.Start,
                                    LineBreakMode = LineBreakMode.WordWrap,
                                }
                                .Bind(
                                    Label.TextProperty,
                                    nameof(
                                        VerificationEmojiItem.Description)),
                                },
                            },
                        }));
            }),
        };
    }

    private ContentView DecimalVerificationDisplay()
    {
        return new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,

            Content = new CollectionView
            {
                SelectionMode = SelectionMode.None,
                HorizontalOptions = LayoutOptions.Center,

                ItemsLayout = new LinearItemsLayout(
                    ItemsLayoutOrientation.Horizontal)
                {
                    ItemSpacing = 8,
                },

                ItemTemplate = new DataTemplate(() =>
                    new Border
                    {
                        MinimumWidthRequest = 96,
                        Padding = new Thickness(16, 12),
                        StrokeThickness = 1,

                        StrokeShape = new RoundRectangle
                        {
                            CornerRadius = 12,
                        },

                        Content = new Label
                        {
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalTextAlignment = TextAlignment.Center,
                        }
                        .Bind(Label.TextProperty, "."),
                    }
                    .DynamicResource(
                        VisualElement.BackgroundColorProperty,
                        "SurfaceContainer")
                    .DynamicResource(
                        Border.StrokeProperty,
                        "OutlineVariant")),
            }
            .Bind(
                CollectionView.ItemsSourceProperty,
                nameof(Decimals),
                source: this),
        };
    }

    private void ApplyState(VerificationState state)
    {
        State = state;

        ShowProgress = state is
            VerificationState.AcknowledgingRequest or
            VerificationState.AcceptingRequest or
            VerificationState.RequestingVerification or
            VerificationState.WaitingForOtherSession or
            VerificationState.StartingSas or
            VerificationState.WaitingForVerificationData or
            VerificationState.Approving or
            VerificationState.Declining or
            VerificationState.Cancelling;

        ShowRequestButtons =
            state == VerificationState.AwaitingUserAcceptance;

        ShowVerificationData =
            state == VerificationState.Comparing &&
            Data is not null;

        ShowComparisonButtons =
            state == VerificationState.Comparing &&
            Data is not null;

        ShowCancelButton = state is
            VerificationState.AwaitingUserAcceptance or
            VerificationState.AcceptingRequest or
            VerificationState.RequestingVerification or
            VerificationState.WaitingForOtherSession or
            VerificationState.StartingSas or
            VerificationState.WaitingForVerificationData or
            VerificationState.Comparing;

        ShowCloseButton = IsTerminalState(state);
    }

    private static bool IsTerminalState(VerificationState state)
    {
        return state is
            VerificationState.Completed or
            VerificationState.Cancelled or
            VerificationState.Failed;
    }

    private void RunOnMainThread(System.Action action)
    {
        if (_isDisposed)
        {
            return;
        }

        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_isDisposed)
            {
                action();
            }
        });
    }

    public void DidReceiveVerificationRequest(
        SessionVerificationRequestDetails details)
    {
        RunOnMainThread(() =>
        {
            Debug.WriteLine(
                $"Verification request received: {details.FlowId}");

            ResetFlow();

            _initiatedLocally = false;
            _pendingRequest = details;

            DeviceName =
                details.DeviceDisplayName ??
                details.DeviceId;

            Status = "Opening verification request...";

            ApplyState(VerificationState.AcknowledgingRequest);

            _ = EnsurePopupShownAsync();
            _ = AcknowledgeRequestAsync(details);
        });
    }

    public void DidAcceptVerificationRequest()
    {
        RunOnMainThread(() =>
        {
            Debug.WriteLine(
                "Verification request entered the ready state.");

            if (_initiatedLocally)
            {
                Status =
                    "The other session accepted. Starting verification...";

                _ = StartSasAsync();
                return;
            }

            Status =
                "Request accepted. Waiting for the other session to start verification...";

            ApplyState(VerificationState.WaitingForVerificationData);
        });
    }

    public void DidStartSasVerification()
    {
        RunOnMainThread(() =>
        {
            Debug.WriteLine("SAS verification started.");

            Status = "Preparing verification values...";
            ApplyState(VerificationState.WaitingForVerificationData);

            _ = EnsurePopupShownAsync();
        });
    }

    public void DidReceiveVerificationData(
        SessionVerificationData data)
    {
        RunOnMainThread(() =>
        {
            Debug.WriteLine("Verification data received.");

            Data = data;

            Emojis = data switch
            {
                SessionVerificationData.Emojis emojis =>
                    emojis.EmojisValue
                        .Select(emoji => new VerificationEmojiItem(
                            emoji.Symbol(),
                            emoji.Description()))
                        .ToArray(),

                _ => Array.Empty<VerificationEmojiItem>(),
            };

            Decimals = data switch
            {
                SessionVerificationData.Decimals decimals =>
                    decimals.Values.ToArray(),

                _ => Array.Empty<ushort>(),
            };

            Status =
                "Compare these values with the values shown on the other session.";

            ApplyState(VerificationState.Comparing);

            _ = EnsurePopupShownAsync();
        });
    }

    public void DidFail()
    {
        RunOnMainThread(() =>
        {
            Status = "Verification failed.";
            ApplyState(VerificationState.Failed);

            _ = EnsurePopupShownAsync();
        });
    }

    public void DidCancel()
    {
        RunOnMainThread(() =>
        {
            Status = "Verification was cancelled.";
            ApplyState(VerificationState.Cancelled);

            _ = EnsurePopupShownAsync();
        });
    }

    public void DidFinish()
    {
        RunOnMainThread(() =>
        {
            Status = "This session was successfully verified.";
            ApplyState(VerificationState.Completed);

            _ = EnsurePopupShownAsync();
        });
    }
}
