#if WINDOWS
using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Text;

namespace SummerVacation.Services;

/// <summary>
/// Offline transcription of a WAV file using the legacy Windows desktop speech recognizer (SAPI).
/// Independent of the microphone and of the Windows "Online speech recognition" setting.
/// </summary>
internal static class WindowsFileTranscriber
{
	public static string? Transcribe(string audioPath, CultureInfo culture)
	{
		try
		{
			// SAPI rejects the recorder's WAV header (JUNK chunk, 18-byte fmt), so feed raw PCM from memory instead.
			if (!TryReadPcm(File.ReadAllBytes(audioPath), out var pcm, out var format))
			{
				System.Diagnostics.Debug.WriteLine("[SpeechToText] Unsupported WAV format; only 16-bit PCM is supported.");
				return null;
			}

			var recognizerInfo = SelectRecognizer(culture);
			if (recognizerInfo is null)
			{
				System.Diagnostics.Debug.WriteLine("[SpeechToText] No installed desktop speech recognizer found.");
				return null;
			}

			using var engine = new SpeechRecognitionEngine(recognizerInfo);
			engine.LoadGrammar(new DictationGrammar());
			using var stream = new MemoryStream(pcm);
			engine.SetInputToAudioStream(stream, format);

			var text = new StringBuilder();
			while (true)
			{
				RecognitionResult? result;
				try
				{
					result = engine.Recognize(TimeSpan.FromSeconds(30));
				}
				catch (InvalidOperationException)
				{
					// Thrown once the input stream has been fully consumed.
					break;
				}

				if (result is null)
				{
					break;
				}

				if (!string.IsNullOrWhiteSpace(result.Text))
				{
					text.Append(text.Length > 0 ? " " : string.Empty).Append(result.Text);
				}
			}

			System.Diagnostics.Debug.WriteLine($"[SpeechToText] File transcription ({recognizerInfo.Culture.Name}): '{text}'");
			return text.Length > 0 ? text.ToString() : null;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SpeechToText] File transcription failed: {ex}");
			return null;
		}
	}

	private static bool TryReadPcm(byte[] wav, out byte[] pcm, out SpeechAudioFormatInfo format)
	{
		format = null!;
		if (!WavPcmReader.TryRead(wav, out pcm, out var sampleRate, out var channels))
		{
			return false;
		}

		format = new SpeechAudioFormatInfo(sampleRate, AudioBitsPerSample.Sixteen, channels == 1 ? AudioChannel.Mono : AudioChannel.Stereo);
		return true;
	}

	private static RecognizerInfo? SelectRecognizer(CultureInfo culture)
	{
		var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
		return recognizers.FirstOrDefault(r => r.Culture.Equals(culture))
			?? recognizers.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
			?? recognizers.FirstOrDefault();
	}
}
#endif
