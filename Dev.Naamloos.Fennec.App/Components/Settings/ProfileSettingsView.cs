using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ProfileSettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient),
        typeof(ManagedMatrixClient),
        typeof(ProfileSettingsView)
    );

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public string DisplayName { get; set; } = "Account";
    public string UserId { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public string Bio { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TimeZone { get; set; } = TimeZoneInfo.Local.Id;
    public IReadOnlyList<string> TimeZones { get; } =
        TimeZoneInfo.GetSystemTimeZones().Select(timeZone => timeZone.Id).ToArray();
    public string Pronouns { get; set; } = string.Empty;
    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "@" : DisplayName[..1].ToUpperInvariant();

    public ProfileSettingsView()
    {
        this.BindService<ManagedMatrixClient, ProfileSettingsView>(MatrixClientProperty);
        Loaded += async (_, _) => await RefreshAsync();

        Content = new SettingsSection(
            "Profile",
            new Border
            {
                Padding = 20,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Content = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                    },
                    ColumnSpacing = 16,
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 88,
                            HeightRequest = 88,
                            StrokeThickness = 0,
                            StrokeShape = new RoundRectangle { CornerRadius = 44 },
                            Content = new Grid
                            {
                                Children =
                                {
                                    new Label
                                    {
                                        FontSize = 32,
                                        FontAttributes = FontAttributes.Bold,
                                        HorizontalTextAlignment = TextAlignment.Center,
                                        VerticalTextAlignment = TextAlignment.Center,
                                    }.Bind(Label.TextProperty, nameof(Initial), source: this),
                                    new MatrixImage
                                    {
                                        IsJson = false,
                                        Aspect = Aspect.AspectFill,
                                    }.Bind(
                                        MatrixImage.MatrixSourceProperty,
                                        nameof(AvatarUrl),
                                        source: this
                                    ),
                                },
                            },
                        }.DynamicResource(
                            VisualElement.BackgroundColorProperty,
                            "PrimaryContainer"
                        ),
                        new VerticalStackLayout
                        {
                            VerticalOptions = LayoutOptions.Center,
                            Spacing = 4,
                            Children =
                            {
                                new Label
                                {
                                    Text = "Your profile",
                                    Opacity = .7,
                                    FontSize = 12,
                                },
                                new Label
                                {
                                    FontSize = 20,
                                    FontAttributes = FontAttributes.Bold,
                                }.Bind(Label.TextProperty, nameof(DisplayName), source: this),
                                new Label
                                {
                                    Opacity = .7,
                                    LineBreakMode = LineBreakMode.TailTruncation,
                                }.Bind(Label.TextProperty, nameof(UserId), source: this),
                            },
                        }.Column(1),
                    },
                },
            }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2"),
            new Button { Text = "Change profile picture", BackgroundColor = Colors.Transparent }
                .DynamicResource(Button.TextColorProperty, "Primary")
                .BindCommand(nameof(ChangeAvatarCommand), source: this),
            Field("Display name", nameof(DisplayName)),
            Field("Bio", nameof(Bio)),
            Field("Status", nameof(Status)),
            Field("Pronouns (comma separated)", nameof(Pronouns)),
            new Picker { Title = "Time zone" }
                .Bind(Picker.ItemsSourceProperty, nameof(TimeZones), source: this)
                .Bind(
                    Picker.SelectedItemProperty,
                    nameof(TimeZone),
                    BindingMode.TwoWay,
                    source: this
                ),
            new Button { Text = "Save profile" }.BindCommand(
                nameof(SaveProfileCommand),
                source: this
            ),
            new Button { Text = "Copy Matrix ID", BackgroundColor = Colors.Transparent }
                .DynamicResource(Button.TextColorProperty, "Primary")
                .BindCommand(nameof(CopyMatrixIdCommand), source: this)
        );
    }

    private async Task RefreshAsync()
    {
        if (MatrixClient is null)
            return;

        var profile = await MatrixClient.GetOwnMatrixProfileAsync();
        DisplayName = profile.DisplayName;
        UserId = profile.UserId;
        AvatarUrl = profile.AvatarUrl;
        Bio = profile.Bio ?? string.Empty;
        Status = profile.Status ?? string.Empty;
        TimeZone = profile.TimeZone ?? TimeZoneInfo.Local.Id;
        Pronouns = string.Join(", ", profile.Pronouns);
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(UserId));
        OnPropertyChanged(nameof(AvatarUrl));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(TimeZone));
        OnPropertyChanged(nameof(Pronouns));
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(DisplayName) || MatrixClient is null)
            return;
        await MatrixClient.SetOwnMatrixProfileAsync(
            new MatrixProfile(
                UserId,
                DisplayName.Trim(),
                AvatarUrl,
                Bio.Trim(),
                Status.Trim(),
                "online",
                TimeZone.Trim(),
                Pronouns.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                ),
                string.Empty
            )
        );
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ChangeAvatarAsync()
    {
        if (MatrixClient is null)
            return;

        try
        {
            var attachment = await AttachmentPicker.PickConfirmedAsync(
                new PickOptions
                {
                    PickerTitle = "Choose profile picture",
                    FileTypes = FilePickerFileType.Images,
                }
            );
            if (attachment is null)
                return;

            await MatrixClient.SetOwnAvatarAsync(attachment.MimeType, attachment.Data);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not change profile picture: {exception}");
            await Shell.Current.DisplayAlertAsync(
                "Profile picture",
                "Could not update your profile picture.",
                "OK"
            );
        }
    }

    [RelayCommand]
    private Task CopyMatrixIdAsync() => Clipboard.Default.SetTextAsync(UserId);

    private Entry Field(string placeholder, string property) =>
        new Entry { Placeholder = placeholder }.Bind(
            Entry.TextProperty,
            property,
            BindingMode.TwoWay,
            source: this
        );
}
