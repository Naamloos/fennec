using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class UserProfileSheet : ContentView
{
    private readonly VerticalStackLayout _profile = new() { Spacing = 16 };
    private readonly Border _sheet;

    public UserProfileSheet()
    {
        IsVisible = false;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += (_, _) => Hide();

        var handle = new Border
        {
            WidthRequest = 44,
            HeightRequest = 5,
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
        }.DynamicResource(BackgroundColorProperty, "OutlineVariant");
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        handle.GestureRecognizers.Add(pan);

        _sheet = new Border
        {
            Padding = new Thickness(24, 10, 24, 28),
            MaximumWidthRequest = 620,
            MaximumHeightRequest = 680,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) },
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                Children =
                {
                    new Grid { Children = { handle }, Padding = new Thickness(0, 0, 0, 10) },
                    new ScrollView { Content = _profile }.Row(1),
                },
            },
        };
        _sheet.SetDynamicResource(VisualElement.BackgroundColorProperty, "Surface");

        Content = new Grid
        {
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#66000000"),
                    GestureRecognizers = { dismiss },
                },
                _sheet,
            },
        };
    }

    public async Task ShowAsync(
        ManagedMatrixClient client,
        string userId,
        string? fallbackName = null,
        string? fallbackAvatarUrl = null
    )
    {
        IsVisible = true;
        _profile.Children.Clear();
        _profile.Children.Add(new ActivityIndicator { IsRunning = true, HeightRequest = 120 });
        await LoadAsync(client, userId, fallbackName, fallbackAvatarUrl);
    }

    public void Hide() => IsVisible = false;

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType is GestureStatus.Running)
        {
            _sheet.TranslationY = Math.Max(0, e.TotalY);
            return;
        }

        if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            if (_sheet.TranslationY > 96)
            {
                Hide();
                _sheet.TranslationY = 0;
            }
            else
            {
                _ = _sheet.TranslateTo(0, 0, 150, Easing.CubicOut);
            }
        }
    }

    private async Task LoadAsync(
        ManagedMatrixClient client,
        string userId,
        string? fallbackName,
        string? fallbackAvatarUrl
    )
    {
        try
        {
            var loaded = await client.GetMatrixProfileAsync(userId);
            var profile = loaded with
            {
                DisplayName =
                    (
                        string.IsNullOrWhiteSpace(loaded.DisplayName)
                        || loaded.DisplayName == loaded.UserId
                    ) && !string.IsNullOrWhiteSpace(fallbackName)
                        ? fallbackName
                        : loaded.DisplayName,
                AvatarUrl = string.IsNullOrWhiteSpace(loaded.AvatarUrl)
                    ? fallbackAvatarUrl
                    : loaded.AvatarUrl,
            };
            _profile.Children.Clear();

            _profile.Children.Add(
                new Grid
                {
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new MatrixAvatar
                        {
                            Size = 104,
                            MatrixSource = profile.AvatarUrl,
                            DisplayName = profile.DisplayName,
                        },
                        new Border
                        {
                            WidthRequest = 22,
                            HeightRequest = 22,
                            StrokeThickness = 3,
                            StrokeShape = new RoundRectangle { CornerRadius = 11 },
                            HorizontalOptions = LayoutOptions.End,
                            VerticalOptions = LayoutOptions.End,
                            BackgroundColor =
                                profile.Presence == "online" ? Colors.Green : Colors.Gray,
                        }.DynamicResource(Border.StrokeProperty, "Surface"),
                    },
                }
            );
            _profile.Children.Add(
                new Label
                {
                    Text = profile.DisplayName,
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                }
            );
            _profile.Children.Add(
                new Label
                {
                    Text = profile.Status ?? profile.Presence ?? "Offline",
                    Opacity = .75,
                    HorizontalTextAlignment = TextAlignment.Center,
                }
            );

            AddDetail("Matrix ID", profile.UserId);
            AddDetail("Homeserver", profile.Homeserver);
            AddDetail("Bio", profile.Bio);
            AddDetail(
                "Pronouns",
                profile.Pronouns.Count == 0 ? null : string.Join(" · ", profile.Pronouns)
            );
            AddDetail("Time zone", profile.TimeZone);
            AddMutualRooms(await client.GetMutualRoomsAsync(userId));
        }
        catch (Exception exception)
        {
            _profile.Children.Clear();
            _profile.Children.Add(
                new Label
                {
                    Text = $"Could not load profile\n{exception.Message}",
                    TextColor = Colors.Red,
                    HorizontalTextAlignment = TextAlignment.Center,
                }
            );
        }
    }

    private void AddDetail(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profile.Children.Add(
            new Border
            {
                Padding = new Thickness(16, 12),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Content = new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        new Label
                        {
                            Text = label.ToUpperInvariant(),
                            FontSize = 10,
                            Opacity = .6,
                        },
                        new Label
                        {
                            Text = value,
                            FontSize = 15,
                            LineBreakMode = LineBreakMode.WordWrap,
                        },
                    },
                },
            }.DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainer")
        );
    }

    private void AddMutualRooms(IReadOnlyList<MatrixSharedRoom> rooms)
    {
        if (rooms.Count == 0)
            return;

        _profile.Children.Add(
            new Label
            {
                Text = $"MUTUAL ROOMS · {rooms.Count}",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                Opacity = .6,
            }
        );
        foreach (var room in rooms)
        {
            _profile.Children.Add(
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                    },
                    ColumnSpacing = 12,
                    Children =
                    {
                        new MatrixAvatar
                        {
                            Size = 36,
                            MatrixSource = room.AvatarUrl,
                            DisplayName = room.DisplayName,
                        },
                        new Label
                        {
                            Text = room.DisplayName,
                            VerticalOptions = LayoutOptions.Center,
                            FontAttributes = FontAttributes.Bold,
                        }.Column(1),
                    },
                }
            );
        }
    }
}
