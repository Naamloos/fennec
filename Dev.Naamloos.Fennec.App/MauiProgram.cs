using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Plugin.Maui.Audio;
using Dev.Naamloos.Fennec.Sdk;
using MaterialColorUtilities.Maui;
using MauiIcons.Material;
using Dev.Naamloos.Fennec.Sdk.Helpers;


#if ANDROID || IOS || MACCATALYST
using Nalu;
#endif

namespace Dev.Naamloos.Fennec.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMaterialColors(options =>
            {
                options.FallbackSeed = 0xE6BB8B;
                options.EnableDynamicColor = false;
            })
            .UseMaterialMauiIcons()
            .UseMauiApp<App>()
            .UseMauiCommunityToolkitMediaElement(true)
            .UseMauiCommunityToolkit(opt =>
            {
                opt.SetShouldEnableSnackbarOnWindows(true);
            })
            .AddAudio()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID || IOS || MACCATALYST
        builder.UseNaluVirtualScroll();
#endif

        // Services
        builder.Services.AddSingleton<AsyncSecureStorage>();
        builder.Services.AddSingleton(sp =>
        {
            var secureStore = sp.GetRequiredService<AsyncSecureStorage>();
            return new ManagedMatrixClient(DeviceInfo.Current.Platform.ToString(), Path.Combine(FileSystem.AppDataDirectory, "fennec"), secureStore);
        });
        builder.Services.AddSingleton<SessionVerificationService>();
        builder.Services.AddSingleton<AppNavigationService>();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<MatrixRecoveryService>();
        builder.Services.AddSingleton<UserSettingsService>();

        // Pages
        builder.Services.AddTransient<Login>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<Startup>();

        return builder.Build();
    }
}
