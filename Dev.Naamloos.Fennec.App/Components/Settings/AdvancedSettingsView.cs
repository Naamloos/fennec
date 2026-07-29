using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class AdvancedSettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient),
        typeof(ManagedMatrixClient),
        typeof(AdvancedSettingsView)
    );

    private readonly VerticalStackLayout _namespaces = new() { Spacing = 8 };

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public AdvancedSettingsView()
    {
        this.BindService<ManagedMatrixClient, AdvancedSettingsView>(MatrixClientProperty);
        Loaded += async (_, _) => await RefreshAsync();

        Content = new SettingsSection(
            "Advanced",
            new Button { Text = "Copy access token", BackgroundColor = Colors.Transparent }
                .DynamicResource(Button.TextColorProperty, "Primary")
                .BindCommand(nameof(CopyAccessTokenCommand), source: this),
            new Label { Text = "Global account data", FontAttributes = FontAttributes.Bold },
            new Button { Text = "Refresh account data", BackgroundColor = Colors.Transparent }
                .DynamicResource(Button.TextColorProperty, "Primary")
                .BindCommand(nameof(RefreshAccountDataCommand), source: this),
            _namespaces
        );
    }

    private async Task RefreshAsync()
    {
        if (MatrixClient is null)
            return;

        var accountData = await MatrixClient.GetGlobalAccountDataAsync();
        _namespaces.Children.Clear();
        foreach (
            var group in accountData
                .GroupBy(item =>
                    item.Type.Contains('.') ? item.Type[..item.Type.LastIndexOf('.')] : item.Type
                )
                .OrderBy(group => group.Key)
        )
        {
            _namespaces.Children.Add(CreateNamespace(group.Key, group));
        }
    }

    [RelayCommand]
    private Task RefreshAccountDataAsync() => RefreshAsync();

    [RelayCommand]
    private Task CopyAccessTokenAsync() =>
        Clipboard.Default.SetTextAsync(MatrixClient?.GetAccessToken() ?? string.Empty);

    private static View CreateNamespace(
        string @namespace,
        IEnumerable<GlobalAccountData> accountData
    )
    {
        var entries = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(12, 8, 12, 12),
            IsVisible = false,
        };
        foreach (var item in accountData.OrderBy(item => item.Type))
        {
            entries.Children.Add(
                new Border
                {
                    Padding = 10,
                    StrokeThickness = 0,
                    Content = new VerticalStackLayout
                    {
                        Spacing = 3,
                        Children =
                        {
                            new Label
                            {
                                Text = item.Type,
                                FontAttributes = FontAttributes.Bold,
                                FontSize = 12,
                            },
                            new Label
                            {
                                Text = item.Content,
                                FontSize = 11,
                                LineBreakMode = LineBreakMode.WordWrap,
                                Opacity = .75,
                            },
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface")
            );
        }

        var toggle = new Button
        {
            Text = $"›  {@namespace} ({entries.Children.Count})",
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(12, 8),
            FontAttributes = FontAttributes.Bold,
        };
        toggle.DynamicResource(Button.TextColorProperty, "OnSurface");
        toggle.Clicked += (_, _) =>
        {
            entries.IsVisible = !entries.IsVisible;
            toggle.Text =
                $"{(entries.IsVisible ? "⌄" : "›")}  {@namespace} ({entries.Children.Count})";
        };

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout { Spacing = 0, Children = { toggle, entries } },
        }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2");
    }
}
