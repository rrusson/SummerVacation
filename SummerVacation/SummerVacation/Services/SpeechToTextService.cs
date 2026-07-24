using System.Globalization;

using CommunityToolkit.Maui.Media;

namespace SummerVacation.Services;

/// <summary>
/// Runs live speech recognition in parallel with audio recording and persists
/// transcription results to a shared transcript file alongside the narration MP3s.
/// Each line in the file has the format: [picture filename]: [recognized text]
/// The entire file is regenerated on every save to keep it consistent.
/// </summary>
public class SpeechToTextService : ISpeechToTextService
{
	private readonly ISpeechToText _speechToText;
	private string? _currentRecognitionResult = null;
	private bool _isListening;
	private bool _isDisposed;

	public SpeechToTextService(ISpeechToText speechToText)
	{
		_speechToText = speechToText;
	}

	/// <summary>
	/// Requests permissions and starts live speech recognition.
	/// Should be called when audio recording begins.
	/// Returns <see langword="false"/> if permissions were not granted (e.g. Online Speech Recognition
	/// is disabled in Windows Privacy Settings), in which case recording still proceeds without transcription.
	/// </summary>
	public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
	{
		if (_isListening)
		{
			return true;
		}

		var isGranted = await _speechToText.RequestPermissions(cancellationToken);
		if (!isGranted)
		{
			return false;
		}

		_currentRecognitionResult = null;

		try
		{
			await _speechToText.StartListenAsync(
				new SpeechToTextOptions
				{
					Culture = CultureInfo.CurrentCulture,
					ShouldReportPartialResults = true,
				},
				cancellationToken);
		}
		catch (UnauthorizedAccessException)
		{
			// On Windows, RequestPermissions may return true even when Online Speech Recognition
			// is disabled in Privacy Settings causing StartListenAsync to throw.
			// Treat this as a permission denial so the caller can see guidance without crashing.
			return false;
		}

		_speechToText.RecognitionResultUpdated += OnRecognitionResultUpdated;
		_speechToText.RecognitionResultCompleted += OnRecognitionResultCompleted;
		_isListening = true;
		return true;
	}

	/// <summary>
	/// Stops live speech recognition and returns the recognized text.
	/// </summary>
	public async Task<string?> StopAsync(CancellationToken cancellationToken = default)
	{
		if (!_isListening)
		{
			return _currentRecognitionResult;
		}

		try
		{
			// Always attempt to stop, even if the recognizer already auto-completed on its own
			// (e.g. after a silence timeout). Some platforms throw if StopListenAsync is called
			// when the recognizer already stopped itself; treat that as a no-op.
			await _speechToText.StopListenAsync(cancellationToken).ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"StopListenAsync threw (recognizer may have already stopped): {ex.Message}");
		}
		finally
		{
			_speechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
			_speechToText.RecognitionResultCompleted -= OnRecognitionResultCompleted;
			_isListening = false;
		}

		return _currentRecognitionResult;
	}

	/// <summary>
	/// Saves the transcript for <paramref name="photoFileName"/> to the transcript file at <paramref name="transcriptPath"/>.
	/// All entries from the existing file are preserved and the entry for <paramref name="photoFileName"/> is added or updated.
	/// The whole file is rewritten each time for simplicity.
	/// </summary>
	/// <param name="transcriptPath">The file path to save the transcript to. Should be a .txt file in the same directory as the photos and narrations.</param>
	/// <param name="photoFileName">The photo file name to associate with the transcript. Should not include directory path.</param>
	/// <param name="recognizedText">The recognized text to save for the photo. If null, an empty string will be saved.</param>
	/// <param name="allPhotoFileNames">A list of all photo file names to determine the order of entries in the transcript file.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	public async Task SaveTranscriptAsync(
		string transcriptPath,
		string photoFileName,
		string? recognizedText,
		IReadOnlyList<string> allPhotoFileNames,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(recognizedText))
		{
			return;
		}

		// Load existing entries so other photos' transcriptions are preserved.
		var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string separator = ":\t";

		if (File.Exists(transcriptPath))
		{
			foreach (var line in await File.ReadAllLinesAsync(transcriptPath, cancellationToken))
			{
				var separatorIndex = line.IndexOf(separator, StringComparison.Ordinal);
				if (separatorIndex > 0)
				{
					var key = line[..separatorIndex];
					var value = line[(separatorIndex + separator.Length)..];
					entries[key] = value;
				}
			}
		}

		entries[photoFileName] = recognizedText ?? string.Empty;

		string? directory = Path.GetDirectoryName(transcriptPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		// Write lines in photo order so the file is stable and predictable.
		var lines = allPhotoFileNames
			.Where(entries.ContainsKey)
			.Select(name => $"{name}{separator}{entries[name]}")
			.ToList();

		lines.Insert(0, $"### This file contains audio transcriptions for the photos in {directory}. ###");

		await File.WriteAllLinesAsync(transcriptPath, lines, cancellationToken);
	}

	/// <summary>
	/// Reads the transcript file and returns the transcribed text for <paramref name="photoFileName"/>, or null if not found.
	/// </summary>
	public async Task<string?> GetTranscriptForPhotoAsync(
		string transcriptPath,
		string photoFileName,
		CancellationToken cancellationToken = default)
	{
		if (!File.Exists(transcriptPath))
		{
			return null;
		}

		string separator = ":\t";
		foreach (var line in await File.ReadAllLinesAsync(transcriptPath, cancellationToken))
		{
			var separatorIndex = line.IndexOf(separator, StringComparison.Ordinal);
			if (separatorIndex > 0)
			{
				var key = line[..separatorIndex];
				if (string.Equals(key, photoFileName, StringComparison.OrdinalIgnoreCase))
				{
					var value = line[(separatorIndex + separator.Length)..];
					return string.IsNullOrWhiteSpace(value) ? null : value;
				}
			}
		}

		return null;
	}

	private void OnRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs e)
	{
		if (!string.IsNullOrEmpty(e.RecognitionResult))
		{
			_currentRecognitionResult = e.RecognitionResult;
		}
	}

	private void OnRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
	{
		// NOTE: The platform recognizer can raise this event on its own (e.g. after a brief silence timeout) well before the user taps "Stop Recording".
		// Do NOT flip _isListening or unsubscribe here; that state is owned by StartAsync/StopAsync. If we unsubscribe here, a subsequent auto-restart
		// by the platform (or a delayed final result) would be missed, and StopAsync would short-circuit without ever calling StopListenAsync,
		// silently losing any speech captured after this point.
		if (e.RecognitionResult.IsSuccessful)
		{
			_currentRecognitionResult = e.RecognitionResult.Text is null ? _currentRecognitionResult : e.RecognitionResult.Text;
		}
		else
		{
			System.Diagnostics.Debug.WriteLine($"[SpeechToText] Recognition failed: {e.RecognitionResult.Exception?.Message}");
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing && _isListening)
			{
				_speechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
				_speechToText.RecognitionResultCompleted -= OnRecognitionResultCompleted;
				// Fire-and-forget; best effort cleanup on dispose.
				_ = _speechToText.StopListenAsync(CancellationToken.None);
			}

			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
