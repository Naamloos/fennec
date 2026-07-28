using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.App.Services;
using MauiIcons.Core;
using MauiIcons.Material;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Settings : ContentPage
{
    public static readonly BindableProperty UserSettingsProperty = BindableProperty.Create(
        nameof(UserSettings), typeof(UserSettingsService), typeof(Settings));

    public UserSettingsService? UserSettings
    {
        get => (UserSettingsService?)GetValue(UserSettingsProperty);
        set => SetValue(UserSettingsProperty, value);
    }

    private readonly List<Button> _tabButtons = [];
    private string _selectedTab = "Profile";

    public string SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (_selectedTab == value) return;
            _selectedTab = value;
            OnPropertyChanged();
            UpdateTabButtons();
        }
    }

    public Settings(AppShell shell)
    {
        BindingContext = shell;
        this.BindService<UserSettingsService, Settings>(UserSettingsProperty);
        Shell.SetNavBarIsVisible(this, false);

        var profile = new ProfileSettingsView();
        var sessions = new SessionsSettingsView();
        var emotes = new EmoteSettingsView();
        var client = new VerticalStackLayout
        {
            Children = { new ClientSettingsView(), new WallpaperSettingsView() },
        };
        var about = new VerticalStackLayout
        {
            Children = { new AboutSettingsView(), new AdvancedSettingsView() },
        };
        var content = new TemplateSwitchView<string, string>(value => value)
            .Add(value => value == "Profile", CreateTabContent(profile))
            .Add(value => value == "Sessions", CreateTabContent(sessions))
            .Add(value => value == "Stickers", CreateTabContent(emotes))
            .Add(value => value == "Client", CreateTabContent(client))
            .Add(value => value == "About", CreateTabContent(about));
        content.SetBinding(
            TemplateSwitchView<string, string>.ValueProperty,
            new Binding(nameof(SelectedTab), source: this));

        Content = new Grid
        {
            Padding = new Thickness(24, 20),
            RowSpacing = 16,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
#if !WINDOWS
                        new MauiIcon
                        {
                            Icon = MaterialIcons.ArrowBack,
                            IconSize = 24,
                            WidthRequest = 40,
                            HeightRequest = 40,
                            GestureRecognizers =
                            {
                                new TapGestureRecognizer()
                                    .BindCommand(nameof(BackCommand), source: this),
                            },
                        },
#endif
                        new Label
                        {
                            Text = "Settings",
                            FontSize = 24,
                            FontAttributes = FontAttributes.Bold,
                            VerticalTextAlignment = TextAlignment.Center,
                        },
                    },
                },
                new Grid
                {
                    HorizontalOptions = LayoutOptions.Fill,
                    MaximumWidthRequest = 780,
                    Children =
                    {
                        new ScrollView
                        {
                            Orientation = ScrollOrientation.Horizontal,
                            HorizontalOptions = LayoutOptions.Fill,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                            Content = new HorizontalStackLayout
                            {
                                Spacing = 4,
                                Children =
                                {
                                    CreateTabButton("Profile"),
                                    CreateTabButton("Sessions"),
                                    CreateTabButton("Stickers"),
                                    CreateTabButton("Client"),
                                    CreateTabButton("About"),
                                },
                            },
                        },
                    },
                }.Row(1),
                new Grid
                {
                    HorizontalOptions = LayoutOptions.Center,
                    MaximumWidthRequest = 780,
                    Children = { content },
                }.Row(2),
                new Button
                {
                    Text = "Log out",
                    BackgroundColor = Colors.Transparent,
                    TextColor = Colors.Red,
                    HorizontalOptions = LayoutOptions.Center,
                }
                .BindCommand(nameof(AppShell.LogoutCommand))
                .Row(3),
            },
        }
        .DynamicResource(VisualElement.BackgroundColorProperty, "Surface");

        UpdateTabButtons();
    }

    private static View CreateTabContent(View view) => new ScrollView
    {
        Content = new VerticalStackLayout
        {
            Padding = new Thickness(0, 4, 0, 16),
            Children = { view },
        },
    };

    private Button CreateTabButton(string tab)
    {
        var button = new Button
        {
            Text = tab,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(12, 6),
            CornerRadius = 14,
            FontAttributes = FontAttributes.Bold,
        };
        button.Clicked += (_, _) => SelectedTab = tab;
        _tabButtons.Add(button);
        return button;
    }

    private void UpdateTabButtons()
    {
        foreach (var button in _tabButtons)
        {
            var selected = button.Text == SelectedTab;
            button.BackgroundColor = selected
                ? (Color)Application.Current!.Resources["PrimaryContainer"]
                : Colors.Transparent;
            button.TextColor = selected
                ? (Color)Application.Current!.Resources["OnPrimaryContainer"]
                : (Color)Application.Current!.Resources["OnSurfaceVariant"];
        }
    }

    [RelayCommand]
    private async Task BackAsync() => await Navigation.PopAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = UserSettings?.LoadAsync();

#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Disabled;
        }
#endif
    }

    protected override void OnDisappearing()
    {
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Flyout;
        }
#endif

        base.OnDisappearing();
    }
}
