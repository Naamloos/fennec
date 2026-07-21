namespace Dev.Naamloos.Fennec.App.Services;

public sealed class AppNavigationService(IServiceProvider services)
{
    public void ShowLogin() => App.SetRootPage(services.GetRequiredService<Login>());

    public void ShowShell() => App.SetRootPage(services.GetRequiredService<AppShell>());
}
