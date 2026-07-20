using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Models;

namespace Dev.Naamloos.Fennec.App.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    public static IReadOnlyList<MatrixInstance> Instances { get; } =
    [
        new("Matrix.org", "https://matrix.org", "matrix.org"),
        new("envs.net", "https://matrix.envs.net", "envs.net"),
        new("tchncs.de", "https://matrix.tchncs.de", "tchncs.de"),
        new("Naamloos", "https://m.naamloos.dev", "m.naamloos.dev")
    ];

    private readonly MatrixService _matrixService;
    private readonly AppNavigationService _navigation;

    public LoginViewModel(MatrixService matrixService, AppNavigationService navigation)
    {
        _matrixService = matrixService;
        _navigation = navigation;
        SelectedInstance = Instances[0];

#if DEBUG
        System.Diagnostics.Debug.Assert(
            NormalizeUsername("naamloos", "m.naamloos.dev") == "@naamloos:m.naamloos.dev");
#endif
    }

    [ObservableProperty]
    public partial MatrixInstance? SelectedInstance { get; set; }

    [ObservableProperty]
    public partial string Homeserver { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnSelectedInstanceChanged(MatrixInstance? value)
    {
        if (value is not null) Homeserver = value.Url;
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public void EnsureCorrectRoot()
    {
        if (_matrixService.IsLoggedIn) _navigation.ShowShell();
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var normalizedHomeserver = NormalizeHomeserver(Homeserver);
            var serverName = SelectedInstance is not null &&
                             normalizedHomeserver == NormalizeHomeserver(SelectedInstance.Url)
                ? SelectedInstance.ServerName
                : new Uri(normalizedHomeserver).Authority;
            await _matrixService.LoginAsync(
                normalizedHomeserver,
                NormalizeUsername(Username, serverName),
                Password);
            _navigation.ShowShell();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() => !IsBusy;

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
}
