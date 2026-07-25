using CommunityToolkit.Maui.Media;

namespace SummerVacation.Services;

/// <summary>
/// No-op implementation of <see cref="ISpeechToText"/> used on platforms or devices
/// where speech recognition is not available (e.g. Android emulators).
/// All operations complete successfully without performing any recognition.
/// </summary>
internal sealed class NoOpSpeechToText : ISpeechToText
{
	public event EventHandler<SpeechToTextRecognitionResultUpdatedEventArgs>? RecognitionResultUpdated
	{
		add { }
		remove { }
	}

	public event EventHandler<SpeechToTextRecognitionResultCompletedEventArgs>? RecognitionResultCompleted
	{
		add { }
		remove { }
	}

	public event EventHandler<SpeechToTextStateChangedEventArgs>? StateChanged
	{
		add { }
		remove { }
	}

	public SpeechToTextState CurrentState => SpeechToTextState.Stopped;

	public Task<bool> RequestPermissions(CancellationToken cancellationToken = default) => Task.FromResult(false);

	public Task StartListenAsync(SpeechToTextOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task StopListenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
