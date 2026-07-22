using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Diagnostics;
using System.Globalization;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RoomList : ContentView, IDisposable
{
    private readonly ManagedMatrixClient _matrixClient;
    private readonly CollectionView _collectionView;

    private ObservableRoomList? _observableRoomList;
    private bool _isLoading;
    private bool _disposed;

    public static readonly BindableProperty SelectedRoomProperty =
        BindableProperty.Create(
            nameof(SelectedRoom),
            typeof(Room),
            typeof(RoomList),
            default(Room),
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSelectedRoomChanged);

    public Room? SelectedRoom
    {
        get => (Room?)GetValue(SelectedRoomProperty);
        set => SetValue(SelectedRoomProperty, value);
    }

    public RoomList(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        _collectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = CreateEmptyView(),
            ItemTemplate = new DataTemplate(CreateRoomListItem),
        };

        _collectionView.SelectionChanged += OnSelectionChanged;

        Content = _collectionView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(
        object? sender,
        EventArgs e)
    {
        await InitializeAsync();
    }

    private void OnUnloaded(
        object? sender,
        EventArgs e)
    {
        DisposeObservableRoomList();
    }

    private async Task InitializeAsync()
    {
        if (_disposed ||
            _isLoading ||
            _observableRoomList is not null)
        {
            return;
        }

        _isLoading = true;

        try
        {
            _observableRoomList =
                await _matrixClient.GetObservableRoomListAsync();

            _collectionView.ItemsSource = _observableRoomList;

            SynchronizeSelectedItem();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Failed to initialize room list: {exception}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is RoomEntry roomEntry)
        {
            SelectedRoom = roomEntry.Room;
        }
        else
        {
            SelectedRoom = null;
        }
    }

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object? oldValue,
        object? newValue)
    {
        var roomList = (RoomList)bindable;
        roomList.SynchronizeSelectedItem();
    }

    private void SynchronizeSelectedItem()
    {
        if (SelectedRoom is null ||
            _observableRoomList is null)
        {
            _collectionView.SelectedItem = null;
            return;
        }

        if (_collectionView.SelectedItem is RoomEntry currentEntry &&
            currentEntry.Room.Id() == SelectedRoom.Id())
        {
            return;
        }

        _collectionView.SelectedItem = _observableRoomList
            .FirstOrDefault(
                entry => entry.Room.Id() == SelectedRoom.Id());
    }

    private static View CreateEmptyView()
    {
        return new VerticalStackLayout
        {
            Padding = new Thickness(0, 24),
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "Loading rooms…",
                    HorizontalTextAlignment = TextAlignment.Center,
                    Opacity = 0.7,
                },
            },
        };
    }

    private static View CreateRoomListItem()
    {
        var nameLabel = new Label
        {
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomEntry.Name),
            mode: BindingMode.OneWay);

        var iconLabel = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomEntry.IsSpace),
            converter: RoomIconConverter.Instance,
            mode: BindingMode.OneWay);

        var roomIcon = new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 18,
            },
            Content = iconLabel,
        };

        var content = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        content.Add(roomIcon, column: 0);
        content.Add(nameLabel, column: 1);

        return new Border
        {
            Margin = new Thickness(0, 2),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 8,
            },
            Content = content,
        };
    }

    private void DisposeObservableRoomList()
    {
        _collectionView.ItemsSource = null;

        _observableRoomList?.Dispose();
        _observableRoomList = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _collectionView.SelectionChanged -= OnSelectionChanged;

        DisposeObservableRoomList();

        GC.SuppressFinalize(this);
    }

    private sealed class RoomIconConverter : IValueConverter
    {
        public static RoomIconConverter Instance { get; } = new();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            return value is true
                ? "O"
                : "#";
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}