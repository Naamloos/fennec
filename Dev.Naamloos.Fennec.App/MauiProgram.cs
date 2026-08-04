using CommunityToolkit.Maui;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MaterialColorUtilities.Maui;
using MauiIcons.Material;
using Microsoft.Extensions.Logging;
using MPowerKit.VirtualizeListView;
using Plugin.Maui.Audio;

namespace Dev.Naamloos.Fennec.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp
            .CreateBuilder()
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
                opt.SetPopupOptionsDefaults(
                    new DefaultPopupOptionsSettings
                    {
                        PageOverlayColor = Color.FromArgb("#66000000"),
                    }
                );
            })
            .AddAudio()
            .UseMPowerKitListView()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<AsyncSecureStorage>();
        builder.Services.AddSingleton(sp =>
        {
            var secureStore = sp.GetRequiredService<AsyncSecureStorage>();
            return new ManagedMatrixClient(
                DeviceInfo.Current.Platform.ToString(),
                Path.Combine(FileSystem.AppDataDirectory, "fennec"),
                secureStore
            );
        });
        builder.Services.AddSingleton<SessionVerificationService>();
        builder.Services.AddSingleton<AppNavigationService>();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<MatrixRecoveryService>();
        builder.Services.AddSingleton<UserSettingsService>();

        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<
                Components.AttachmentEntry,
                Platforms.Android.AttachmentEntryHandler
            >();
#elif WINDOWS
            handlers.AddHandler<
                Components.AttachmentEntry,
                Platforms.Windows.AttachmentEntryHandler
            >();
#endif
        });

        // Pages
        builder.Services.AddTransient<Login>();
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<Startup>();

        return builder.Build();
    }
}
