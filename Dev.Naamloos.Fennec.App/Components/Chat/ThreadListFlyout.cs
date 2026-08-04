using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ThreadListFlyout : ContentView, IDisposable
{
    private ObservableThreadList? _threads;

    [BindableProperty(PropertyChangedMethodName = nameof(OnRoomChanged))]
    public partial Room? Room { get; set; }

    [BindableProperty]
    public partial bool IsOpen { get; set; }

    [BindableProperty]
    public partial ICommand? OpenThreadCommand { get; set; }

    [BindableProperty]
    public partial ObservableThreadList? Threads { get; set; }

    public ThreadListFlyout()
    {
        Content = new Grid
        {
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#66000000"),
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer { Command = new Command(() => IsOpen = false) },
                    },
                },
                new Border
                {
                    WidthRequest =
                        DeviceInfo.Current.Platform == DevicePlatform.Android
                        || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? -1
                            : 380,
                    HorizontalOptions =
                        DeviceInfo.Current.Platform == DevicePlatform.Android
                        || DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? LayoutOptions.Fill
                            : LayoutOptions.End,
                    StrokeThickness = 0,
                    Padding = 16,
                    Content = new Grid
                    {
                        RowSpacing = 12,
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
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Auto),
                                },
                                Children =
                                {
                                    new Label
                                    {
                                        Text = "Threads",
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
                                        Command = new Command(() => IsOpen = false),
                                    }
                                        .Invoke(view =>
                                            SemanticProperties.SetDescription(view, "Close threads")
                                        )
                                        .Column(1),
                                },
                            }.Row(0),
                            new CollectionView
                            {
                                SelectionMode = SelectionMode.None,
                                EmptyView = new TemplateSwitchView<bool, bool>(value => value)
                                {
                                    FallbackTemplate = new Label
                                    {
                                        Text = "No threads yet.",
                                        Opacity = .7,
                                        HorizontalOptions = LayoutOptions.Center,
                                        VerticalOptions = LayoutOptions.Center,
                                    },
                                }
                                    .Add(
                                        value => value,
                                        new ActivityIndicator
                                        {
                                            IsRunning = true,
                                            HorizontalOptions = LayoutOptions.Center,
                                            VerticalOptions = LayoutOptions.Center,
                                        }
                                    )
                                    .Bind(
                                        TemplateSwitchView<bool, bool>.ValueProperty,
                                        $"{nameof(Threads)}.{nameof(ObservableThreadList.IsLoading)}",
                                        source: this
                                    ),
                                ItemTemplate = new DataTemplate(() =>
                                    new Border
                                    {
                                        Margin = new Thickness(0, 0, 0, 8),
                                        Padding = 10,
                                        StrokeThickness = 0,
                                        StrokeShape =
                                            new Microsoft.Maui.Controls.Shapes.RoundRectangle
                                            {
                                                CornerRadius = 10,
                                            },
                                        Content = new Grid
                                        {
                                            ColumnSpacing = 10,
                                            ColumnDefinitions =
                                            {
                                                new ColumnDefinition(GridLength.Auto),
                                                new ColumnDefinition(GridLength.Star),
                                            },
                                            Children =
                                            {
                                                new MatrixAvatar { Size = 36 }
                                                    .Bind(
                                                        MatrixAvatar.MatrixSourceProperty,
                                                        nameof(MatrixThreadSummary.SenderAvatarUrl)
                                                    )
                                                    .Bind(
                                                        MatrixAvatar.DisplayNameProperty,
                                                        nameof(MatrixThreadSummary.Sender)
                                                    ),
                                                new VerticalStackLayout
                                                {
                                                    Spacing = 2,
                                                    Children =
                                                    {
                                                        new Label
                                                        {
                                                            FontAttributes = FontAttributes.Bold,
                                                        }.Bind(
                                                            Label.TextProperty,
                                                            nameof(MatrixThreadSummary.Sender)
                                                        ),
                                                        new Label
                                                        {
                                                            MaxLines = 2,
                                                            LineBreakMode =
                                                                LineBreakMode.TailTruncation,
                                                        }.Bind(
                                                            Label.TextProperty,
                                                            nameof(MatrixThreadSummary.Body)
                                                        ),
                                                        new Label
                                                        {
                                                            MaxLines = 1,
                                                            FontSize = 12,
                                                            Opacity = .7,
                                                            LineBreakMode =
                                                                LineBreakMode.TailTruncation,
                                                        }.Bind(
                                                            Label.TextProperty,
                                                            nameof(MatrixThreadSummary.LatestBody),
                                                            stringFormat: "Latest: {0}"
                                                        ),
                                                        new Label
                                                        {
                                                            FontSize = 11,
                                                            Opacity = .65,
                                                        }.Bind(
                                                            Label.TextProperty,
                                                            nameof(MatrixThreadSummary.ReplyCount),
                                                            stringFormat: "{0} replies"
                                                        ),
                                                    },
                                                }.Column(1),
                                            },
                                        },
                                        GestureRecognizers =
                                        {
                                            new TapGestureRecognizer()
                                                .BindCommand(
                                                    nameof(OpenThreadCommand),
                                                    source: this
                                                )
                                                .Bind(
                                                    TapGestureRecognizer.CommandParameterProperty,
                                                    nameof(MatrixThreadSummary.RootEventId)
                                                ),
                                        },
                                    }
                                        .Bind(
                                            SemanticProperties.DescriptionProperty,
                                            nameof(MatrixThreadSummary.Body),
                                            stringFormat: "Thread: {0}"
                                        )
                                        .DynamicResource(
                                            BackgroundColorProperty,
                                            "SurfaceContainer"
                                        )
                                ),
                            }
                                .Bind(
                                    ItemsView.ItemsSourceProperty,
                                    $"{nameof(Threads)}.{nameof(ObservableThreadList.Items)}",
                                    source: this
                                )
                                .Row(1),
                            new TemplateSwitchView<bool, bool>(value => value)
                            {
                                FallbackTemplate = new Label
                                {
                                    Text = "All threads loaded",
                                    Opacity = .6,
                                    HorizontalOptions = LayoutOptions.Center,
                                },
                            }
                                .Add(
                                    value => value,
                                    new Button { Text = "Load more" }
                                        .BindCommand(nameof(LoadMoreCommand), source: this)
                                        .Bind(
                                            IsEnabledProperty,
                                            $"{nameof(Threads)}.{nameof(ObservableThreadList.IsLoading)}",
                                            converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                                            source: this
                                        )
                                )
                                .Bind(
                                    TemplateSwitchView<bool, bool>.ValueProperty,
                                    $"{nameof(Threads)}.{nameof(ObservableThreadList.HasMore)}",
                                    source: this
                                )
                                .Bind(
                                    IsVisibleProperty,
                                    $"{nameof(Threads)}.{nameof(ObservableThreadList.IsLoading)}",
                                    converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                                    source: this
                                )
                                .Row(2),
                            new ActivityIndicator
                            {
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.End,
                            }
                                .Bind(
                                    ActivityIndicator.IsRunningProperty,
                                    $"{nameof(Threads)}.{nameof(ObservableThreadList.IsLoading)}",
                                    source: this
                                )
                                .Bind(
                                    IsVisibleProperty,
                                    $"{nameof(Threads)}.{nameof(ObservableThreadList.IsLoading)}",
                                    source: this
                                )
                                .Row(2),
                        },
                    },
                }.DynamicResource(BackgroundColorProperty, "Surface"),
            },
        }.Bind(IsVisibleProperty, nameof(IsOpen), source: this);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task LoadMoreAsync() => Threads?.LoadMoreAsync() ?? Task.CompletedTask;

    private static void OnRoomChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (ThreadListFlyout)bindable;
        view._threads?.Dispose();
        view.Threads = view._threads = newValue is Room room
            ? new ObservableThreadList(room)
            : null;
        if (view.Threads is not null)
            _ = view.Threads.LoadMoreAsync();
    }

    public void Dispose()
    {
        _threads?.Dispose();
        _threads = null;
        Threads = null;
    }
}
