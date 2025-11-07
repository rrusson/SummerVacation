using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using SummerVacation.Shared.Services;
using SummerVacation.Services;
using Plugin.Maui.Audio;

namespace SummerVacation
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Add audio recording service
            builder.Services.AddSingleton(AudioManager.Current);

            // Register MainPage with dependency injection
            builder.Services.AddTransient<MainPage>();

            // Add device-specific services used by the SummerVacation.Shared project
            builder.Services.AddSingleton<IFormFactor, FormFactor>();

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
