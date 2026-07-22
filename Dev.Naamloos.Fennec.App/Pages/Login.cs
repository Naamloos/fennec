using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using static uniffi.matrix_sdk_ffi.AuthData;

namespace Dev.Naamloos.Fennec.App.Pages;

public partial class Login : ContentPage
{
    // Static list of homeservers
    public static IReadOnlyList<MatrixHomeserverOption> Homeservers { get; } =
    [
        new("Matrix.org", "https://matrix.org", "matrix.org"),
        new("envs.net", "https://matrix.envs.net", "envs.net"),
        new("tchncs.de", "https://matrix.tchncs.de", "tchncs.de"),
        new("Naamloos", "https://m.naamloos.dev", "m.naamloos.dev")
    ];

    // Binding properties
    public string HomeServer
    {
        get { return _homeserver; }
        set
        {
            if (_homeserver != value)
            {
                _homeserver = value;
                _selectedServer = null;
                OnPropertyChanged(nameof(HomeServer));
            }
        }
    }
    public MatrixHomeserverOption SelectedServer
    {
        get { return _selectedServer ?? Homeservers[0]; }
        set
        {
            if (_selectedServer != value)
            {
                _selectedServer = value;
                _homeserver = value.Url;
                OnPropertyChanged(nameof(SelectedServer));
                OnPropertyChanged(nameof(HomeServer));
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (_username == value)
            {
                return;
            }

            _username = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value)
            {
                return;
            }

            _password = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value)
            {
                return;
            }
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    // Backing fields
    private string _homeserver = string.Empty;
	private MatrixHomeserverOption? _selectedServer = Homeservers[0];
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _isLoading = false;
    private string _errorMessage = string.Empty;

    // Services
    private ManagedMatrixClient _client;
    private AppNavigationService _navigation;

    public Login(ManagedMatrixClient client, AppNavigationService navigation)
	{
        BindingContext = this;
        _client = client;
        _navigation = navigation;
        build();
	}

    // Commands
    [RelayCommand]
    public async Task LoginAsync()
    {
        IsLoading = true;
        try
        {
            var normalizedHomeserver = NormalizeHomeserver(_homeserver);
            var serverName = _selectedServer is not null &&
                             normalizedHomeserver == NormalizeHomeserver(_selectedServer.Url)
                ? _selectedServer.ServerName
                : new Uri(normalizedHomeserver).Authority;
            await _client.LoginAsync(
                normalizedHomeserver,
                NormalizeUsername(_username, serverName),
                _password);
            _navigation.ShowShell();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal static string NormalizeHomeserver(string? value)
    {
        var result = value?.Trim() ?? string.Empty;
        if (!result.Contains("://", StringComparison.Ordinal)) result = $"https://{result}";
        if (!Uri.TryCreate(result, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("Enter a valid homeserver.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    internal static string NormalizeUsername(string? value, string serverName)
    {
        var result = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result)) throw new ArgumentException("Enter a username.");
        if (result.Contains(':')) return result.StartsWith('@') ? result : $"@{result}";
        return $"@{result.TrimStart('@')}:{serverName}";
    }

    // UI Builder
    private void build()
	{
        Content = new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Content = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                MaximumWidthRequest = 480,
                Spacing = 12,
                Children =
                {
                    new Label()
                    {
                        Text = "Fennec",
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label()
                    {
                        Text = "A Matrix client for .NET MAUI",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Italic
                    },
                    new Picker()
                    {
                        Title = "Choose a homeserver",
                        ItemsSource = Homeservers.ToList(),
                    }.Bind(Picker.SelectedItemProperty, nameof(SelectedServer), BindingMode.TwoWay)
                    .Bind(Picker.IsEnabledProperty, nameof(IsLoading), BindingMode.Default, new InvertedBoolConverter()),
                    new Entry()
                    {
                        Placeholder = "Homeserver URL",
                        Text = HomeServer
                    }.Bind(Entry.TextProperty, nameof(HomeServer), BindingMode.TwoWay)
                    .Bind(Entry.IsEnabledProperty, nameof(IsLoading), BindingMode.Default, new InvertedBoolConverter()),
                    new Entry()
                    {
                        Placeholder = "Username",
                        Text = Username,
                    }.Bind(Entry.TextProperty, nameof(Username), BindingMode.TwoWay)
                    .Bind(Entry.IsEnabledProperty, nameof(IsLoading), BindingMode.Default, new InvertedBoolConverter()),
                    new Entry()
                    {
                        Placeholder = "Password",
                        IsPassword = true,
                        Text = Password
                    }.Bind(Entry.TextProperty, nameof(Password), BindingMode.TwoWay)
                    .Bind(Entry.IsEnabledProperty, nameof(IsLoading), BindingMode.Default, new InvertedBoolConverter()),
                    new Label()
                    {
                        TextColor = Colors.Red,
                    }.Bind(Label.TextProperty, nameof(ErrorMessage)),
                    new Button()
                    {
                        Text= "Login",
                    }.Bind(Button.CommandProperty, nameof(LoginCommand))
                    .Bind(Button.IsEnabledProperty, nameof(IsLoading), BindingMode.Default, new InvertedBoolConverter()),
                    new ActivityIndicator()
                    {
                        IsRunning = true,
                    }.Bind(ActivityIndicator.IsVisibleProperty, nameof(IsLoading))
                    .Bind(ActivityIndicator.IsRunningProperty, nameof(IsLoading))
                }
            }
        };
    }
}

public record MatrixHomeserverOption(string Name, string Url, string ServerName)
{
    public override string ToString()
    {
        return this.Name;
    }
};