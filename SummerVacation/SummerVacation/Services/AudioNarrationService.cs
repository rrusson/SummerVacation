using Plugin.Maui.Audio;

namespace SummerVacation.Services;

/// <summary>
/// Manages audio recording and playback for photo narrations.
/// </summary>
public sealed class AudioNarrationService : IDisposable
{
	private readonly IAudioManager _audioManager;
	private IAudioRecorder? _audioRecorder;
	private IAudioPlayer? _audioPlayer;

	public event EventHandler? PlaybackEnded;

	public bool IsRecording { get; private set; }

	public bool IsPlaying => _audioPlayer?.IsPlaying == true;

	public AudioNarrationService(IAudioManager audioManager)
	{
		_audioManager = audioManager;
	}

	/// <summary>
	/// Requests microphone permission and starts recording.
	/// Throws <see cref="UnauthorizedAccessException"/> if permission is denied.
	/// </summary>
	public async Task StartRecordingAsync()
	{
		var status = await Permissions.RequestAsync<Permissions.Microphone>();
		if (status != PermissionStatus.Granted)
		{
			throw new UnauthorizedAccessException("Microphone permission is required to record audio.");
		}

		StopPlayback();

		_audioRecorder = _audioManager.CreateRecorder();
		await _audioRecorder.StartAsync();
		IsRecording = true;
	}

	/// <summary>
	/// Stops the active recording and saves it to <paramref name="destinationPath"/>.
	/// </summary>
	public async Task StopAndSaveAsync(string destinationPath)
	{
		if (_audioRecorder is null)
		{
			IsRecording = false; 
			return;
		}

		var audioSource = await _audioRecorder.StopAsync().ConfigureAwait(true);
		_audioRecorder = null;
		IsRecording = false;

		var directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (File.Exists(destinationPath))
		{
			StopPlayback();
			File.Delete(destinationPath);
		}

		using var audioStream = audioSource.GetAudioStream();
		using var fileStream = File.Create(destinationPath);
		await audioStream.CopyToAsync(fileStream);
	}

	/// <summary>
	/// Stops the active recording without saving it, discarding the captured audio.
	/// Use this when the user chooses not to replace an existing narration.
	/// </summary>
	public async Task DiscardRecordingAsync()
	{
		if (_audioRecorder is null)
		{
			IsRecording = false;
			return;
		}

		try
		{
			await _audioRecorder.StopAsync().ConfigureAwait(true);
		}
		finally
		{
			_audioRecorder = null;
			IsRecording = false;
		}
	}

	/// <summary>
	/// Plays the narration at <paramref name="narrationPath"/>.
	/// Throws <see cref="FileNotFoundException"/> if the file does not exist.
	/// </summary>
	public async Task PlayAsync(string narrationPath)
	{
		if (!File.Exists(narrationPath))
		{
			throw new FileNotFoundException("Narration file not found.", narrationPath);
		}

		// Read into memory so the file is not locked during playback.
		var audioBytes = await File.ReadAllBytesAsync(narrationPath);
		_audioPlayer = _audioManager.CreatePlayer(new MemoryStream(audioBytes));
		_audioPlayer.PlaybackEnded += OnPlayerPlaybackEnded;
		_audioPlayer.Play();
	}

	/// <summary>Stops playback and releases the player.</summary>
	public void StopPlayback()
	{
		if (_audioPlayer is null)
		{
			return;
		}

		var player = _audioPlayer;
		_audioPlayer = null;

		if (player.IsPlaying)
		{
			player.Stop();
		}

		player.PlaybackEnded -= OnPlayerPlaybackEnded;
		player.Dispose();
	}

	private void OnPlayerPlaybackEnded(object? sender, EventArgs e)
	{
		StopPlayback();
		PlaybackEnded?.Invoke(this, EventArgs.Empty);
	}

	public void Dispose()
	{
		StopPlayback();
		_audioRecorder = null;
	}
}
