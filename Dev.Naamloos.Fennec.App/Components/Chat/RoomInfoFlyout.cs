using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RoomInfoFlyout : ContentView
{
    private const string AnimationName = nameof(RoomInfoFlyout);
    private readonly Border _drawer;
    private readonly BoxView _scrim;
    private readonly Label _title = new() { FontSize = 22, FontAttributes = FontAttributes.Bold };
    private readonly Label _topic = new() { Opacity = .75, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Label _roomId = new() { FontSize = 11, Opacity = .6, LineBreakMode = LineBreakMode.TailTruncation };
    private readonly Label _wallpaperStatus = new() { FontSize = 11, IsVisible = false };
    private readonly MatrixAvatar _avatar = new() { Size = 80 };

    public static readonly BindableProperty ClientProperty = BindableProperty.Create(nameof(Client), typeof(ManagedMatrixClient), typeof(RoomInfoFlyout));
    public static readonly BindableProperty RoomProperty = BindableProperty.Create(nameof(Room), typeof(Room), typeof(RoomInfoFlyout), propertyChanged: OnRoomChanged);
    public static readonly BindableProperty MembersProperty = BindableProperty.Create(nameof(Members), typeof(IEnumerable<RoomMember>), typeof(RoomInfoFlyout));
    public static readonly BindableProperty OpenProfileCommandProperty = BindableProperty.Create(nameof(OpenProfileCommand), typeof(ICommand), typeof(RoomInfoFlyout));
    public static readonly BindableProperty WallpaperChangedCommandProperty = BindableProperty.Create(nameof(WallpaperChangedCommand), typeof(ICommand), typeof(RoomInfoFlyout));
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(RoomInfoFlyout), false,
        BindingMode.TwoWay, propertyChanged: OnIsOpenChanged);

    public ManagedMatrixClient? Client { get => (ManagedMatrixClient?)GetValue(ClientProperty); set => SetValue(ClientProperty, value); }
    public Room? Room { get => (Room?)GetValue(RoomProperty); set => SetValue(RoomProperty, value); }
    public IEnumerable<RoomMember>? Members { get => (IEnumerable<RoomMember>?)GetValue(MembersProperty); set => SetValue(MembersProperty, value); }
    public ICommand? OpenProfileCommand { get => (ICommand?)GetValue(OpenProfileCommandProperty); set => SetValue(OpenProfileCommandProperty, value); }
    public ICommand? WallpaperChangedCommand { get => (ICommand?)GetValue(WallpaperChangedCommandProperty); set => SetValue(WallpaperChangedCommandProperty, value); }
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    public RoomInfoFlyout()
    {
        IsVisible = false;

        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += (_, _) => IsOpen = false;
        _scrim = new BoxView
        {
            Color = Color.FromArgb("#66000000"),
            Opacity = 0,
            GestureRecognizers = { dismiss },
        };

        var close = new Button
        {
            Text = "×",
            FontSize = 28,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
        };
        close.Clicked += (_, _) => IsOpen = false;

        var members = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Header = new VerticalStackLayout
            {
                Padding = new Thickness(20, 12, 20, 14),
                Spacing = 10,
                Children =
                {
                    _avatar,
                    _title,
                    _roomId,
                    _topic,
                    WallpaperControls(),
                    _wallpaperStatus,
                    new Label
                    {
                        Text = "Members",
                        Margin = new Thickness(0, 12, 0, 2),
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        Opacity = .7,
                    },
                },
            },
            ItemTemplate = new DataTemplate(MemberRow),
        }.Bind(ItemsView.ItemsSourceProperty, nameof(Members), source: this);

        _drawer = new Border
        {
            WidthRequest = 360,
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18, 0, 0, 18) },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children =
                {
                    new Grid
                    {
                        Padding = new Thickness(20, 8, 8, 8),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                        },
                        Children =
                        {
                            new Label
                            {
                                Text = "Room info",
                                FontSize = 16,
                                FontAttributes = FontAttributes.Bold,
                                VerticalTextAlignment = TextAlignment.Center,
                            },
                            close.Column(1),
                        },
                    },
                    members.Row(1),
                },
            },
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "Surface")
        .DynamicResource(Border.StrokeProperty, "OutlineVariant");

        Content = new Grid { Children = { _scrim, _drawer } };
    }

    private View MemberRow()
    {
        var row = new Grid
        {
            Padding = new Thickness(20, 7),
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Children =
            {
                new MatrixAvatar { Size = 40 }
                    .Bind(MatrixAvatar.MatrixSourceProperty, nameof(RoomMember.AvatarUrl))
                    .Bind(MatrixAvatar.DisplayNameProperty, nameof(RoomMember.DisplayName)),
                new VerticalStackLayout
                {
                    Spacing = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation }
                            .Bind(Label.TextProperty, nameof(RoomMember.DisplayName)),
                        new Label { FontSize = 11, Opacity = .6, LineBreakMode = LineBreakMode.TailTruncation }
                            .Bind(Label.TextProperty, nameof(RoomMember.UserId)),
                    },
                }.Column(1),
            },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OpenProfileCommand?.Execute(row.BindingContext);
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private View WallpaperControls()
    {
        var set = new Button
        {
            Text = "Set wallpaper",
            Padding = new Thickness(18, 10),
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
        .DynamicResource(Button.TextColorProperty, "OnPrimary");
        var clear = new Button
        {
            Text = "Clear",
            Padding = new Thickness(18, 10),
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainer")
        .DynamicResource(Button.TextColorProperty, "OnSurface");

        set.Clicked += async (_, _) =>
        {
            if (Client is null || Room is null) return;
            var attachment = await AttachmentPicker.PickConfirmedAsync(new PickOptions
            {
                PickerTitle = "Choose room wallpaper",
                FileTypes = FilePickerFileType.Images,
            });
            if (attachment is null) return;
            try
            {
                set.IsEnabled = clear.IsEnabled = false;
                _wallpaperStatus.Text = "Uploading wallpaper…";
                _wallpaperStatus.TextColor = Colors.Gray;
                _wallpaperStatus.IsVisible = true;
                var url = await Client.UploadMediaAsync(attachment.MimeType, attachment.Data);
                await Client.SetRoomWallpaperAsync(Room.Id(), url);
                WallpaperChangedCommand?.Execute(url);
                _wallpaperStatus.Text = "Wallpaper updated";
            }
            catch (Exception exception)
            {
                _wallpaperStatus.Text = exception.Message;
                _wallpaperStatus.TextColor = Colors.Red;
            }
            finally
            {
                set.IsEnabled = clear.IsEnabled = true;
            }
        };

        clear.Clicked += async (_, _) =>
        {
            if (Client is null || Room is null) return;
            try
            {
                set.IsEnabled = clear.IsEnabled = false;
                _wallpaperStatus.Text = "Clearing wallpaper…";
                _wallpaperStatus.TextColor = Colors.Gray;
                _wallpaperStatus.IsVisible = true;
                await Client.ClearRoomWallpaperAsync(Room.Id());
                WallpaperChangedCommand?.Execute(null);
                _wallpaperStatus.Text = "Room wallpaper cleared";
            }
            catch (Exception exception)
            {
                _wallpaperStatus.Text = exception.Message;
                _wallpaperStatus.TextColor = Colors.Red;
            }
            finally
            {
                set.IsEnabled = clear.IsEnabled = true;
            }
        };

        return new HorizontalStackLayout { Spacing = 8, Children = { set, clear } };
    }

    private static void OnRoomChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not Room room) return;
        var flyout = (RoomInfoFlyout)bindable;
        var name = room.DisplayName() ?? room.Id();
        flyout._wallpaperStatus.IsVisible = false;
        flyout._title.Text = name;
        flyout._topic.Text = room.Topic();
        flyout._topic.IsVisible = !string.IsNullOrWhiteSpace(flyout._topic.Text);
        flyout._roomId.Text = room.Id();
        flyout._avatar.MatrixSource = room.AvatarUrl();
        flyout._avatar.DisplayName = name;
    }

    private static void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((RoomInfoFlyout)bindable).SetOpen((bool)newValue);

    private void SetOpen(bool open)
    {
        this.AbortAnimation(AnimationName);
        var width = _drawer.Width > 0 ? _drawer.Width : 420;
        if (open)
        {
            IsVisible = true;
            _drawer.TranslationX = width;
            _scrim.Opacity = 0;
        }

        var animation = new Animation();
        animation.Add(0, 1, new Animation(
            value => _drawer.TranslationX = value,
            _drawer.TranslationX,
            open ? 0 : width,
            Easing.CubicOut));
        animation.Add(0, 1, new Animation(
            value => _scrim.Opacity = value,
            _scrim.Opacity,
            open ? 1 : 0));
        animation.Commit(this, AnimationName, 16, 200, finished: (_, cancelled) =>
        {
            if (!open && !cancelled) IsVisible = false;
        });
    }
}
