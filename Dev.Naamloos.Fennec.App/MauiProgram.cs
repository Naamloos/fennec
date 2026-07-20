using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Plugin.Maui.Audio;
#if ANDROID || IOS || MACCATALYST
using Nalu;
#endif

namespace Dev.Naamloos.Fennec.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .AddAudio()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID || IOS || MACCATALYST
        builder.UseNaluVirtualScroll();
#endif

#if DEBUG
        builder.Logging.AddDebug();
        MessageFormatter.AssertFormatting();
#endif
        builder.Services.AddSingleton<MatrixService>();
        builder.Services.AddSingleton<AppNavigationService>();
        builder.Services.AddSingleton<ChatViewModel>();
        builder.Services.AddSingleton<StartupViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<AppShellViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<StartupPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
