using CommunityToolkit.Maui;

using Microsoft.Extensions.Logging;

using Plugin.Maui.Audio;

using SummerVacation.Services;
using SummerVacation.Shared.Services;

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

			// Register services
			builder.Services.AddTransient<PhotoLibrary>();
			builder.Services.AddTransient<AudioNarrationService>();

			// Register pages and app shell with dependency injection
			builder.Services.AddTransient<MainPage>();
			builder.Services.AddTransient<App>();

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
