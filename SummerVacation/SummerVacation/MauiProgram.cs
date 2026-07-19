using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Media;

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

			// Add speech-to-text service — fall back to a no-op when the device doesn't support it.
#if ANDROID
			bool hasSpeechToText = Android.Speech.SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context);

			if (hasSpeechToText)
			{
				builder.Services.AddSingleton<ISpeechToText>(SpeechToText.Default);
				builder.Services.AddTransient<ISpeechToTextService, SpeechToTextService>();
			}
			else
			{
				builder.Services.AddSingleton<ISpeechToText>(new Services.NullSpeechToText());
				builder.Services.AddTransient<ISpeechToTextService, MockSpeechToTextService>();
			}
#else
			builder.Services.AddSingleton<ISpeechToText>(SpeechToText.Default);
			builder.Services.AddTransient<ISpeechToTextService, SpeechToTextService>();
#endif

			// Register other services
			builder.Services.AddTransient<PhotoLibrary>();
			builder.Services.AddTransient<AudioNarrationService>();
			builder.Services.AddTransient<IPhotoCommentService, PhotoCommentService>();

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
