using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public partial class VerificationPopupV2 : ContentView
{
    public sealed record VerificationEmojiItem(string Symbol, string Description);

    [BindableProperty]
    public partial SessionVerificationService? SessionVerificationService { get; set; }

    private Popup? _popup;
    private Task? _initializationTask;

    public VerificationPopupV2()
    {
        WidthRequest = 1;
        HeightRequest = 1;
        MinimumWidthRequest = 1;
        MinimumHeightRequest = 1;
        Opacity = 0;
        InputTransparent = true;

        this.BindService<SessionVerificationService, VerificationPopupV2>(
            SessionVerificationServiceProperty
        );

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Starts verification with another signed-in session.
    /// </summary>
    public async Task StartVerificationAsync()
    {
        await EnsureServiceInitializedAsync();

        if (SessionVerificationService is not null)
        {
            await SessionVerificationService.RequestVerificationAsync();
        }
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        try
        {
            await EnsureServiceInitializedAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to initialize session verification: {exception}");
        }
    }

    private async void OnUnloaded(object? sender, EventArgs e)
    {
        var service = SessionVerificationService;

        if (service is null)
        {
            return;
        }

        service.PropertyChanged -= OnServicePropertyChanged;

        await ClosePopupAsync();
        await service.StopAsync();

        _initializationTask = null;
    }

    private Task EnsureServiceInitializedAsync()
    {
        return _initializationTask ??= InitializeServiceAsync();
    }

    private async Task InitializeServiceAsync()
    {
        var service = SessionVerificationService;

        if (service is null)
        {
            return;
        }

        await service.InitializeAsync();

        service.PropertyChanged += OnServicePropertyChanged;

        if (service.State != ManagedVerificationState.Listening)
        {
            await EnsurePopupShownAsync();
        }
    }

    private async void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName != nameof(SessionVerificationService.State)
            || sender is not SessionVerificationService service
            || service.State == ManagedVerificationState.Listening
        )
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(EnsurePopupShownAsync);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to show verification popup: {exception}");
        }
    }

    private async Task EnsurePopupShownAsync()
    {
        var service = SessionVerificationService;

        if (service is null || _popup is not null)
        {
            return;
        }

        var page = FindCurrentPage();

        if (page is null)
        {
            Debug.WriteLine("Verification popup: no active page found.");

            return;
        }

        var popup = new Popup
        {
            Padding = 0,
            Margin = 0,
            BackgroundColor = Colors.Transparent,
            CanBeDismissedByTappingOutsideOfPopup = false,
            Content = CreatePopupContent(service),
        };

        _popup = popup;

        try
        {
            await page.ShowPopupAsync(popup);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to display verification popup: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_popup, popup))
            {
                _popup = null;
            }

            if (IsTerminal(service.State))
            {
                service.Reset();
            }
        }
    }

    private Border CreatePopupContent(SessionVerificationService service)
    {
        return new Border
        {
            BindingContext = service,
            Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 16 : 24,
            MinimumWidthRequest = DeviceInfo.Current.Idiom == DeviceIdiom.Phone ? 0 : 360,
            MaximumWidthRequest = 560,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 0,

            StrokeShape = new RoundRectangle { CornerRadius = 18 },

            Content = new ScrollView
            {
                VerticalOptions = LayoutOptions.Center,
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
                            HorizontalOptions = LayoutOptions.Fill,
                            HorizontalTextAlignment = TextAlignment.Center,
                        },
                        CreateDeviceNameLabel(),
                        new Label
                        {
                            FontSize = 17,
                            HorizontalOptions = LayoutOptions.Fill,
                            HorizontalTextAlignment = TextAlignment.Center,
                        }.Bind<Label, ManagedVerificationState, string>(
                            Label.TextProperty,
                            nameof(SessionVerificationService.State),
                            convert: static (value) => GetStatusText(value)
                        ),
                        new ActivityIndicator { HorizontalOptions = LayoutOptions.Center }
                            .Bind<ActivityIndicator, ManagedVerificationState, bool>(
                                IsVisibleProperty,
                                nameof(SessionVerificationService.State),
                                convert: static (value) => IsBusy(value)
                            )
                            .Bind<ActivityIndicator, ManagedVerificationState, bool>(
                                ActivityIndicator.IsRunningProperty,
                                nameof(SessionVerificationService.State),
                                convert: static (value) => IsBusy(value)
                            ),
                        CreateEmojiVerificationDisplay(),
                        CreateDecimalVerificationDisplay(),
                        CreateRequestButtons(service),
                        CreateComparisonButtons(service),
                        new Button
                        {
                            Text = "Cancel",
                            MinimumHeightRequest = 44,
                            HorizontalOptions = LayoutOptions.Center,
                            Command = new AsyncRelayCommand(service.CancelOrRejectAsync),
                        }.Bind<Button, ManagedVerificationState, bool>(
                            IsVisibleProperty,
                            nameof(SessionVerificationService.State),
                            convert: static (value) => CanCancel(value)
                        ),
                        new Button
                        {
                            Text = "Close",
                            MinimumHeightRequest = 44,
                            HorizontalOptions = LayoutOptions.Center,
                            Command = new AsyncRelayCommand(ClosePopupAsync),
                        }.Bind<Button, ManagedVerificationState, bool>(
                            IsVisibleProperty,
                            nameof(SessionVerificationService.State),
                            convert: static (value) => IsTerminal(value)
                        ),
                    },
                },
            },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface");
    }

    private static Label CreateDeviceNameLabel()
    {
        const string path =
            $"{nameof(SessionVerificationService.PendingRequest)}."
            + $"{nameof(SessionVerificationRequestDetails.DeviceDisplayName)}";

        return new Label
        {
            FontSize = 14,
            Opacity = 0.7,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
        }
            .Bind<Label, string?, string?>(Label.TextProperty, path)
            .Bind<Label, string?, bool>(
                IsVisibleProperty,
                path,
                convert: static value => !string.IsNullOrWhiteSpace(value)
            );
    }

    private static Grid CreateRequestButtons(SessionVerificationService service)
    {
        return new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },

            Children =
            {
                new Button
                {
                    Text = "Accept",
                    MinimumHeightRequest = 44,
                    Command = new AsyncRelayCommand(service.AcceptAsync),
                },
                new Button
                {
                    Text = "Reject",
                    MinimumHeightRequest = 44,
                    Command = new AsyncRelayCommand(service.CancelOrRejectAsync),
                }.Column(1),
            },
        }.Bind<Grid, ManagedVerificationState, bool>(
            IsVisibleProperty,
            nameof(SessionVerificationService.State),
            convert: static state => state == ManagedVerificationState.AwaitingUserAcceptance
        );
    }

    private static Grid CreateComparisonButtons(SessionVerificationService service)
    {
        return new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },

            Children =
            {
                new Button
                {
                    Text = "They Match",
                    MinimumHeightRequest = 44,
                    Command = new AsyncRelayCommand(service.ApproveAsync),
                },
                new Button
                {
                    Text = "They Don't Match",
                    MinimumHeightRequest = 44,
                    Command = new AsyncRelayCommand(service.DeclineAsync),
                }.Column(1),
            },
        }.Bind<Grid, ManagedVerificationState, bool>(
            IsVisibleProperty,
            nameof(SessionVerificationService.State),
            convert: static state => state == ManagedVerificationState.Comparing
        );
    }

    private static bool IsPhone => DeviceInfo.Current.Idiom == DeviceIdiom.Phone;

    private static double EmojiCardWidth => IsPhone ? 68 : 112;

    private static double EmojiCardHeight => IsPhone ? 92 : 108;

    private static double EmojiSymbolFontSize => IsPhone ? 26 : 34;

    private static double EmojiDescriptionFontSize => IsPhone ? 10 : 12;

    private static ContentView CreateEmojiVerificationDisplay()
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

                // Four cards and three gaps.
                MaximumWidthRequest = (4 * EmojiCardWidth) + (3 * 8),
            }
                .Bind<FlexLayout, SessionVerificationData?, IReadOnlyList<VerificationEmojiItem>>(
                    BindableLayout.ItemsSourceProperty,
                    nameof(SessionVerificationService.VerificationData),
                    convert: GetEmojis
                )
                .Invoke(layout =>
                    BindableLayout.SetItemTemplate(layout, new DataTemplate(CreateEmojiCard))
                ),
        }.Bind<ContentView, SessionVerificationData?, bool>(
            IsVisibleProperty,
            nameof(SessionVerificationService.VerificationData),
            convert: static data => data is SessionVerificationData.Emojis
        );
    }

    private static Border CreateEmojiCard()
    {
        return new Border
        {
            WidthRequest = EmojiCardWidth,
            HeightRequest = EmojiCardHeight,
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
                        FontSize = EmojiSymbolFontSize,
                        HorizontalOptions = LayoutOptions.Fill,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Center,
                    }.Bind(Label.TextProperty, nameof(VerificationEmojiItem.Symbol)),
                    new Label
                    {
                        FontSize = EmojiDescriptionFontSize,
                        MaxLines = 2,
                        LineBreakMode = LineBreakMode.WordWrap,
                        HorizontalOptions = LayoutOptions.Fill,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment = TextAlignment.Start,
                    }.Bind(Label.TextProperty, nameof(VerificationEmojiItem.Description)),
                },
            },
        };
    }

    private static ContentView CreateDecimalVerificationDisplay()
    {
        return new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,

            Content = new CollectionView
            {
                SelectionMode = SelectionMode.None,
                HorizontalOptions = LayoutOptions.Center,

                ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
                {
                    ItemSpacing = 8,
                },

                ItemTemplate = new DataTemplate(() =>
                    new Border
                    {
                        MinimumWidthRequest = 96,
                        Padding = new Thickness(16, 12),
                        StrokeThickness = 1,

                        StrokeShape = new RoundRectangle { CornerRadius = 12 },

                        Content = new Label
                        {
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalTextAlignment = TextAlignment.Center,
                        }.Bind(Label.TextProperty, "."),
                    }
                        .DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainer")
                        .DynamicResource(Border.StrokeProperty, "OutlineVariant")
                ),
            }.Bind<CollectionView, SessionVerificationData?, IReadOnlyList<ushort>>(
                CollectionView.ItemsSourceProperty,
                nameof(SessionVerificationService.VerificationData),
                convert: GetDecimals
            ),
        }.Bind<ContentView, SessionVerificationData?, bool>(
            IsVisibleProperty,
            nameof(SessionVerificationService.VerificationData),
            convert: static data => data is SessionVerificationData.Decimals
        );
    }

    private async Task ClosePopupAsync()
    {
        var popup = _popup;

        if (popup is null)
        {
            return;
        }

        try
        {
            await popup.CloseAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to close verification popup: {exception}");

            if (ReferenceEquals(_popup, popup))
            {
                _popup = null;
            }
        }
    }

    private static Page? FindCurrentPage()
    {
        if (Shell.Current?.Navigation.NavigationStack.LastOrDefault() is { } page)
        {
            return page;
        }

        if (Shell.Current?.CurrentPage is { } shellPage)
        {
            return shellPage;
        }

        return Application
            .Current?.Windows.Select(window => window.Page)
            .FirstOrDefault(page => page is not null);
    }

    private static IReadOnlyList<VerificationEmojiItem> GetEmojis(SessionVerificationData? data)
    {
        return data is SessionVerificationData.Emojis emojis
            ? emojis
                .EmojisValue.Select(emoji => new VerificationEmojiItem(
                    emoji.Symbol(),
                    emoji.Description()
                ))
                .ToArray()
            : [];
    }

    private static IReadOnlyList<ushort> GetDecimals(SessionVerificationData? data)
    {
        return data is SessionVerificationData.Decimals decimals ? decimals.Values.ToArray() : [];
    }

    private static string GetStatusText(ManagedVerificationState? state)
    {
        return state switch
        {
            ManagedVerificationState.Listening => "Waiting for verification requests...",

            ManagedVerificationState.AcknowledgingRequest => "Opening verification request...",

            ManagedVerificationState.AwaitingUserAcceptance =>
                "Do you want to accept this verification request?",

            ManagedVerificationState.AcceptingRequest => "Accepting verification request...",

            ManagedVerificationState.RequestingVerification => "Sending a verification request...",

            ManagedVerificationState.WaitingForOtherSession =>
                "Waiting for another signed-in session to accept...",

            ManagedVerificationState.StartingSas => "Starting secure emoji verification...",

            ManagedVerificationState.WaitingForVerificationData =>
                "Preparing verification values...",

            ManagedVerificationState.Comparing => "Compare these values with the other session.",

            ManagedVerificationState.Approving => "Confirming that the values match...",

            ManagedVerificationState.Declining => "Reporting that the values do not match...",

            ManagedVerificationState.Cancelling => "Cancelling verification...",

            ManagedVerificationState.Completed => "This session was successfully verified.",

            ManagedVerificationState.Cancelled => "Verification was cancelled.",

            ManagedVerificationState.Failed => "Verification failed.",

            _ => string.Empty,
        };
    }

    private static bool IsBusy(ManagedVerificationState? state)
    {
        return state
            is ManagedVerificationState.AcknowledgingRequest
                or ManagedVerificationState.AcceptingRequest
                or ManagedVerificationState.RequestingVerification
                or ManagedVerificationState.WaitingForOtherSession
                or ManagedVerificationState.StartingSas
                or ManagedVerificationState.WaitingForVerificationData
                or ManagedVerificationState.Approving
                or ManagedVerificationState.Declining
                or ManagedVerificationState.Cancelling;
    }

    private static bool CanCancel(ManagedVerificationState? state)
    {
        return state
            is ManagedVerificationState.AwaitingUserAcceptance
                or ManagedVerificationState.AcceptingRequest
                or ManagedVerificationState.RequestingVerification
                or ManagedVerificationState.WaitingForOtherSession
                or ManagedVerificationState.StartingSas
                or ManagedVerificationState.WaitingForVerificationData
                or ManagedVerificationState.Comparing;
    }

    private static bool IsTerminal(ManagedVerificationState? state)
    {
        return state
            is ManagedVerificationState.Completed
                or ManagedVerificationState.Cancelled
                or ManagedVerificationState.Failed;
    }
}
