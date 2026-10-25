#if WINDOWS
using System.Globalization;
using System.Text;

using Whisper.net;
using Whisper.net.Ggml;

namespace SummerVacation.Services;

/// <summary>
/// Offline transcription using OpenAI's Whisper model via Whisper.net (both MIT licensed, free).
/// The model file is downloaded once on first use and cached in the app data folder.
/// </summary>
internal static class WhisperFileTranscriber
{
	private const int WhisperSampleRate = 16000;
	private const GgmlType ModelType = GgmlType.Base;
	private static readonly SemaphoreSlim ModelLock = new(1, 1);

	public static async Task<string?> TranscribeAsync(string audioPath, CultureInfo culture, CancellationToken cancellationToken)
	{
		var wav = await File.ReadAllBytesAsync(audioPath, cancellationToken).ConfigureAwait(false);
		if (!WavPcmReader.TryRead(wav, out var pcm, out var sampleRate, out var channels))
		{
			System.Diagnostics.Debug.WriteLine("[Whisper] Unsupported WAV format; only 16-bit PCM is supported.");
			return null;
		}

		var samples = WavPcmReader.ToMonoFloat(pcm, sampleRate, channels, WhisperSampleRate);
		var modelPath = await EnsureModelAsync(cancellationToken).ConfigureAwait(false);

		using var factory = WhisperFactory.FromPath(modelPath);
		using var processor = factory.CreateBuilder()
			.WithLanguage(culture.TwoLetterISOLanguageName)
			.Build();

		var text = new StringBuilder();
		await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
		{
			text.Append(segment.Text);
		}

		var result = text.ToString().Trim();
		System.Diagnostics.Debug.WriteLine($"[Whisper] Transcription: '{result}'");
		return result.Length > 0 ? result : null;
	}

	private static async Task<string> EnsureModelAsync(CancellationToken cancellationToken)
	{
		var modelPath = Path.Combine(FileSystem.AppDataDirectory, "models", $"ggml-{ModelType.ToString().ToLowerInvariant()}.bin");
		if (File.Exists(modelPath))
		{
			return modelPath;
		}

		await ModelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (File.Exists(modelPath))
			{
				return modelPath;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
			var partialPath = modelPath + ".download";
			using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ModelType, cancellationToken: cancellationToken).ConfigureAwait(false))
			using (var fileStream = File.Create(partialPath))
			{
				await modelStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
			}

			File.Move(partialPath, modelPath, overwrite: true);
			return modelPath;
		}
		finally
		{
			ModelLock.Release();
		}
	}
}
#endif
