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
		var tempPath = await StopToTempFileAsync().ConfigureAwait(true);
		if (tempPath is not null)
		{
			CommitRecording(tempPath, destinationPath);
		}
	}

	/// <summary>
	/// Stops the active recording and writes the (normalized) audio to a temporary file.
	/// Returns <see langword="null"/> when no recording is active.
	/// </summary>
	public async Task<string?> StopToTempFileAsync()
	{
		if (_audioRecorder is null)
		{
			IsRecording = false;
			return null;
		}

		var audioSource = await _audioRecorder.StopAsync().ConfigureAwait(true);
		_audioRecorder = null;
		IsRecording = false;

		using var audioStream = audioSource.GetAudioStream();
		using var buffer = new MemoryStream();
		await audioStream.CopyToAsync(buffer);
		var audioBytes = buffer.ToArray();
		NormalizeWavPeak(audioBytes);

		var tempPath = Path.Combine(Path.GetTempPath(), $"narration-{Guid.NewGuid():N}.wav");
		await File.WriteAllBytesAsync(tempPath, audioBytes);
		return tempPath;
	}

	/// <summary>
	/// Moves a recording produced by <see cref="StopToTempFileAsync"/> to <paramref name="destinationPath"/>, replacing any existing file.
	/// </summary>
	public void CommitRecording(string tempPath, string destinationPath)
	{
		var directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (File.Exists(destinationPath))
		{
			StopPlayback();
		}

		File.Move(tempPath, destinationPath, overwrite: true);
	}

	/// <summary>
	/// Boosts quiet 16-bit PCM WAV audio in place so its peak reaches about 90% of full scale.
	/// Other formats are left untouched.
	/// </summary>
	private static void NormalizeWavPeak(byte[] wav)
	{
		const int MaxGain = 30;
		const double TargetPeak = 0.9 * short.MaxValue;

		if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
		{
			return;
		}

		var isPcm16 = false;
		var position = 12;
		while (position + 8 <= wav.Length)
		{
			var chunkId = System.Text.Encoding.ASCII.GetString(wav, position, 4);
			var chunkSize = BitConverter.ToInt32(wav, position + 4);
			var dataStart = position + 8;

			if (chunkId == "fmt " && dataStart + 16 <= wav.Length)
			{
				isPcm16 = BitConverter.ToInt16(wav, dataStart) == 1 && BitConverter.ToInt16(wav, dataStart + 14) == 16;
			}
			else if (chunkId == "data")
			{
				if (!isPcm16)
				{
					return;
				}

				var end = chunkSize < 0 || dataStart + chunkSize > wav.Length ? wav.Length : dataStart + chunkSize;
				ApplyGain(wav, dataStart, end - (end - dataStart) % 2, TargetPeak, MaxGain);
				return;
			}

			if (chunkSize < 0)
			{
				return;
			}

			position = dataStart + chunkSize + (chunkSize & 1);
		}
	}

	private static void ApplyGain(byte[] wav, int start, int end, double targetPeak, int maxGain)
	{
		var peak = 0;
		for (var i = start; i < end; i += 2)
		{
			peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
		}

		if (peak == 0)
		{
			return;
		}

		var gain = Math.Min(targetPeak / peak, maxGain);
		if (gain <= 1.05)
		{
			return;
		}

		for (var i = start; i < end; i += 2)
		{
			var scaled = (int)Math.Round(BitConverter.ToInt16(wav, i) * gain);
			var sample = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
			wav[i] = (byte)(sample & 0xFF);
			wav[i + 1] = (byte)((sample >> 8) & 0xFF);
		}
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
		await PlayWithDurationAsync(narrationPath).ConfigureAwait(true);
	}

	/// <summary>
	/// Plays the narration at <paramref name="narrationPath"/> and returns its duration when available.
	/// Throws <see cref="FileNotFoundException"/> if the file does not exist.
	/// </summary>
	/// <param name="narrationPath">The audio file to play.</param>
	/// <param name="cancellationToken">A token that cancels playback setup before audio starts.</param>
	public async Task<TimeSpan?> PlayWithDurationAsync(string narrationPath, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		StopPlayback();

		if (!File.Exists(narrationPath))
		{
			throw new FileNotFoundException("Narration file not found.", narrationPath);
		}

		// Read into memory so the file is not locked during playback.
		var audioBytes = await File.ReadAllBytesAsync(narrationPath, cancellationToken).ConfigureAwait(true);
		cancellationToken.ThrowIfCancellationRequested();
		_audioPlayer = _audioManager.CreatePlayer(new MemoryStream(audioBytes));
		_audioPlayer.PlaybackEnded += OnPlayerPlaybackEnded;
		var duration = _audioPlayer.Duration > 0
			? TimeSpan.FromSeconds(_audioPlayer.Duration)
			: (TimeSpan?)null;
		_audioPlayer.Play();
		return duration;
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
