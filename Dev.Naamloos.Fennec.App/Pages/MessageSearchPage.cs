using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class MessageSearchPage : ContentPage
{
    private readonly ManagedMatrixClient _client;
    private readonly Func<MatrixSearchResult, Task> _open;
    private MatrixSearchSession? _session;
    private string _query = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasSearched;
    private bool _isSearching;

    public MatrixSearchSession? Session
    {
        get => _session;
        private set
        {
            _session = value;
            OnPropertyChanged();
        }
    }

    public string Query
    {
        get => _query;
        set
        {
            if (_query == value)
                return;
            _query = value;
            OnPropertyChanged();
            HasSearched = false;
            ErrorMessage = string.Empty;
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
                return;
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool HasSearched
    {
        get => _hasSearched;
        private set
        {
            if (_hasSearched == value)
                return;
            _hasSearched = value;
            OnPropertyChanged();
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (_isSearching == value)
                return;
            _isSearching = value;
            OnPropertyChanged();
        }
    }

    public MessageSearchPage(ManagedMatrixClient client, Func<MatrixSearchResult, Task> open)
    {
        _client = client;
        _open = open;
        Title = "Search messages";
        BindingContext = this;
        SafeAreaEdges = SafeAreaEdges.All;
        Content = new Grid
        {
            Padding =
                DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                    ? new Thickness(12)
                    : new Thickness(16),
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
                        new SearchBar
                        {
                            Placeholder = "Search joined conversations",
                            ReturnType = ReturnType.Search,
                            SearchCommand = SearchCommand,
                            IsSpellCheckEnabled = false,
                        }.Bind(SearchBar.TextProperty, nameof(Query), BindingMode.TwoWay),
                        new Button
                        {
                            Text = "Search",
                            MinimumHeightRequest = 44,
                            IsVisible = DeviceInfo.Current.Idiom != DeviceIdiom.Phone,
                        }
                            .BindCommand(nameof(SearchCommand), source: this)
                            .Bind(
                                IsEnabledProperty,
                                nameof(IsSearching),
                                converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                                source: this
                            )
                            .Column(1),
                    },
                }.Row(0),
                new CollectionView
                {
                    SelectionMode = SelectionMode.None,
                    EmptyView = new TemplateSwitchView<bool, bool>(value => value)
                    {
                        FallbackTemplate = new TemplateSwitchView<bool, bool>(value => value)
                        {
                            FallbackTemplate = new Label
                            {
                                Text = "Enter a term to search message history.",
                                Opacity = .7,
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center,
                            },
                        }
                            .Add(
                                value => value,
                                new Label
                                {
                                    Text = "No matching messages found.",
                                    Opacity = .7,
                                    HorizontalOptions = LayoutOptions.Center,
                                    VerticalOptions = LayoutOptions.Center,
                                }
                            )
                            .Bind(
                                TemplateSwitchView<bool, bool>.ValueProperty,
                                nameof(HasSearched),
                                source: this
                            ),
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
                            nameof(IsSearching),
                            source: this
                        ),
                    ItemTemplate = new DataTemplate(() =>
                        new Border
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            Padding = 12,
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                            {
                                CornerRadius = 10,
                            },
                            Content = new VerticalStackLayout
                            {
                                Spacing = 4,
                                Children =
                                {
                                    new Label
                                    {
                                        FontAttributes = FontAttributes.Bold,
                                        LineBreakMode = LineBreakMode.TailTruncation,
                                    }.Bind(
                                        Label.TextProperty,
                                        nameof(MatrixSearchResult.SenderName)
                                    ),
                                    new Label
                                    {
                                        MaxLines = 4,
                                        LineBreakMode = LineBreakMode.TailTruncation,
                                    }.Bind(Label.TextProperty, nameof(MatrixSearchResult.Body)),
                                    new Label
                                    {
                                        FontSize = 11,
                                        Opacity = .6,
                                        LineBreakMode = LineBreakMode.TailTruncation,
                                    }.Bind<Label, string, string, string>(
                                        Label.TextProperty,
                                        new Binding(nameof(MatrixSearchResult.RoomId)),
                                        new Binding(nameof(MatrixSearchResult.Timestamp)),
                                        convert: static values => $"{values.Item1} · {values.Item2}"
                                    ),
                                },
                            },
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer()
                                    .BindCommand(nameof(OpenResultCommand), source: this)
                                    .Bind(TapGestureRecognizer.CommandParameterProperty, "."),
                            },
                        }
                            .Bind(
                                SemanticProperties.DescriptionProperty,
                                nameof(MatrixSearchResult.Body),
                                stringFormat: "Search result: {0}"
                            )
                            .DynamicResource(BackgroundColorProperty, "SurfaceContainer")
                    ),
                }
                    .Bind(
                        ItemsView.ItemsSourceProperty,
                        $"{nameof(Session)}.{nameof(MatrixSearchSession.Results)}"
                    )
                    .Row(1),
                new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Button { Text = "Load more" }
                            .BindCommand(nameof(LoadMoreCommand), source: this)
                            .Bind(
                                IsEnabledProperty,
                                $"{nameof(Session)}.{nameof(MatrixSearchSession.IsLoading)}",
                                converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter()
                            )
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Session)}.{nameof(MatrixSearchSession.HasMore)}"
                            ),
                        new ActivityIndicator { IsRunning = true }.Bind(
                            IsVisibleProperty,
                            $"{nameof(Session)}.{nameof(MatrixSearchSession.IsLoading)}"
                        ),
                        new Label
                        {
                            TextColor = Colors.Red,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap,
                        }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(Session)}.{nameof(MatrixSearchSession.ErrorMessage)}"
                            )
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Session)}.{nameof(MatrixSearchSession.ErrorMessage)}",
                                converter: new CommunityToolkit.Maui.Converters.IsStringNotNullOrEmptyConverter()
                            ),
                        new Label
                        {
                            TextColor = Colors.Red,
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.WordWrap,
                        }
                            .Bind(Label.TextProperty, nameof(ErrorMessage), source: this)
                            .Bind(
                                IsVisibleProperty,
                                nameof(ErrorMessage),
                                converter: new CommunityToolkit.Maui.Converters.IsStringNotNullOrEmptyConverter(),
                                source: this
                            ),
                    },
                }.Row(2),
            },
        };
        Unloaded += (_, _) => Session?.Dispose();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsSearching || string.IsNullOrWhiteSpace(Query))
            return;
        IsSearching = true;
        ErrorMessage = string.Empty;
        HasSearched = true;
        Session?.Dispose();
        Session = null;
        try
        {
            Session = await _client.CreateSearchSessionAsync(Query);
            await Session.LoadMoreAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private Task LoadMoreAsync() => Session?.LoadMoreAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task OpenResultAsync(MatrixSearchResult? result) =>
        result is null ? Task.CompletedTask : _open(result);
}
