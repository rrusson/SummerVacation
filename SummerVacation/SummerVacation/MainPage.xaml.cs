using CommunityToolkit.Maui.Storage;

using Plugin.Maui.Audio;

namespace SummerVacation
{
	public partial class MainPage : ContentPage
	{
		private const string LastDirectoryKey = "LastSelectedDirectory";
		private const string Error = "Error";

		// Desktop platforms support writing to arbitrary directories chosen by the user
		private static bool CanWriteToPhotoDirectory =>
			DeviceInfo.Platform == DevicePlatform.WinUI ||
			DeviceInfo.Platform == DevicePlatform.macOS ||
			DeviceInfo.Platform == DevicePlatform.MacCatalyst;

		private readonly IAudioManager _audioManager;
		private IAudioRecorder? _audioRecorder;
		private IAudioPlayer? _audioPlayer;
		private bool _isRecording;

		private string? _currentDirectory;
		private List<string> _photoFiles = [];
		private int _currentPhotoIndex = -1;

		public MainPage(IAudioManager audioManager)
		{
			InitializeComponent();
			_audioManager = audioManager;
			UpdateControlStates();
		}

		protected override async void OnAppearing()
		{
			base.OnAppearing();

			var savedDirectory = Preferences.Default.Get(LastDirectoryKey, string.Empty);
			if (string.IsNullOrEmpty(savedDirectory) || !Directory.Exists(savedDirectory))
				return;

			_currentDirectory = savedDirectory;
			DirectoryPathLabel.Text = _currentDirectory;
			StatusLabel.Text = "Loading photos...";

			_photoFiles = Directory.GetFiles(_currentDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
				.Concat(Directory.GetFiles(_currentDirectory, "*.jpeg", SearchOption.TopDirectoryOnly))
				.OrderBy(f => f)
				.ToList();

			if (_photoFiles.Count != 0)
			{
				_currentPhotoIndex = 0;
				await LoadCurrentPhoto();
			}
			else
			{
				StatusLabel.Text = "No photos found in saved directory";
			}

			UpdateControlStates();
		}

		private async void OnSelectDirectoryClicked(object sender, EventArgs e)
		{
			try
			{
				if (!await HasPermission())
				{
					StatusLabel.Text = "Permission to access photos is required.";
					return;
				}

				var savedDirectory = Preferences.Default.Get(LastDirectoryKey, string.Empty);
				var initialPath = !string.IsNullOrEmpty(savedDirectory) && Directory.Exists(savedDirectory)
					? savedDirectory
					: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

				FolderPickerResult result = await FolderPicker.PickAsync(initialPath);

				if (!result.IsSuccessful || string.IsNullOrEmpty(result.Folder?.Path))
				{
					return;
				}

				_currentDirectory = result.Folder.Path;
				Preferences.Default.Set(LastDirectoryKey, _currentDirectory);
				DirectoryPathLabel.Text = _currentDirectory;
				StatusLabel.Text = "Loading photos...";

				// Get all jpg files in the directory and sort them by name
				_photoFiles = Directory.GetFiles(_currentDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
					.Concat(Directory.GetFiles(_currentDirectory, "*.jpeg", SearchOption.TopDirectoryOnly))
					.OrderBy(f => f)
					.ToList();

				if (_photoFiles.Count != 0)
				{
					_currentPhotoIndex = 0;
					await LoadCurrentPhoto();
				}
				else
				{
					StatusLabel.Text = "No photos found in selected directory";
					NoPhotoLabel.IsVisible = true;
					PhotoDisplay.Source = null;
					_currentPhotoIndex = -1;
				}

				UpdateControlStates();
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to access the selected directory: " + ex.Message, "OK");
				StatusLabel.Text = "Directory selection failed";
			}
		}

		private async Task<bool> HasPermission()
		{
			// Request storage permissions for Android
			if (DeviceInfo.Platform == DevicePlatform.Android)
			{
				var storageStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
				if (storageStatus != PermissionStatus.Granted)
				{
					await DisplayAlertAsync(
						"Permission Required",
						"Storage permission is required to access photos.",
						"OK");
					return false;
				}

				if (OperatingSystem.IsAndroidVersionAtLeast(33))
				{
					var photoStatus = await Permissions.RequestAsync<Permissions.Photos>();
					if (photoStatus != PermissionStatus.Granted)
					{
						await DisplayAlertAsync(
							"Permission Required",
							"Photos permission is required to access images.",
							"OK");
						return false;
					}
				}
			}
			return true;
		}

		private async Task LoadCurrentPhoto()
		{
			if (_currentPhotoIndex >= 0 && _currentPhotoIndex < _photoFiles.Count)
			{
				try
				{
					// Create a FileImageSource instead of using a stream
					PhotoDisplay.Source = ImageSource.FromFile(_photoFiles[_currentPhotoIndex]);
					NoPhotoLabel.IsVisible = false;

					// Update status with current photo info
					var fileName = Path.GetFileName(_photoFiles[_currentPhotoIndex]);
					StatusLabel.Text = $"Photo {_currentPhotoIndex + 1} of {_photoFiles.Count}: {fileName}";
				}
				catch (Exception ex)
				{
					await DisplayAlertAsync(Error, "Failed to load photo: " + ex.Message, "OK");
					StatusLabel.Text = "Error loading photo";
				}
			}
			else
			{
				// Reset the image display when no photo is available
				PhotoDisplay.Source = null;
				NoPhotoLabel.IsVisible = true;
				StatusLabel.Text = "No photo to display";
			}
		}

		private async void OnPreviousClicked(object sender, EventArgs e)
		{
			if (_currentPhotoIndex > 0)
			{
				_currentPhotoIndex--;
				await LoadCurrentPhoto();
				UpdateControlStates();
			}
		}

		private async void OnNextClicked(object sender, EventArgs e)
		{
			if (_currentPhotoIndex < _photoFiles.Count - 1)
			{
				_currentPhotoIndex++;
				await LoadCurrentPhoto();
				UpdateControlStates();
			}
		}

		private string GetNarrationPath()
		{
			if (_currentPhotoIndex < 0 || _currentPhotoIndex >= _photoFiles.Count)
			{
				return string.Empty;
			}

			var photoFileName = Path.GetFileName(_photoFiles[_currentPhotoIndex]);
			var narrationFileName = Path.ChangeExtension(photoFileName, ".mp3");

			// On desktop, save alongside the photos in the same directory (user already granted access via the picker)
			if (CanWriteToPhotoDirectory)
			{
				return Path.Combine(_currentDirectory!, narrationFileName);
			}

			// On Android/iOS, write access to arbitrary directories is restricted — use the app's private storage
			var narrationDir = Path.Combine(FileSystem.Current.AppDataDirectory, "PhotoNarration");
			return Path.Combine(narrationDir, narrationFileName);
		}

		private async void OnRecordClicked(object sender, EventArgs e)
		{
			if (_isRecording)
			{
				await SaveRecordingAsync().ConfigureAwait(true);
				UpdateControlStates();
				return;
			}

			// Start recording...
			try
			{
				// Request microphone permission
				var status = await Permissions.RequestAsync<Permissions.Microphone>();
				if (status != PermissionStatus.Granted)
				{
					await DisplayAlertAsync(
						"Permission Required",
						"Microphone permission is required to record audio.",
						"OK");
					return;
				}

				// Ensure any active playback is stopped and the file is released before recording
				StopAndDisposePlayer();

				_audioRecorder = _audioManager.CreateRecorder();

				await _audioRecorder.StartAsync();
				StatusLabel.Text = "Recording...";
				RecordButton.Text = "Stop Recording";
				_isRecording = true;
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to start recording: " + ex.Message, "OK");
				StatusLabel.Text = "Failed to start recording";
				_isRecording = false;
			}

			UpdateControlStates();
		}

		private async Task SaveRecordingAsync()
		{
			if (_audioRecorder == null)
			{
				return;
			}

			StatusLabel.Text = "Saving recording...";

			try
			{
				IAudioSource? audioFile = await _audioRecorder.StopAsync().ConfigureAwait(true);
				_audioRecorder = null;

				var narrationPath = GetNarrationPath();
				if (string.IsNullOrEmpty(narrationPath))
				{
					_isRecording = false;
					return;
				}

				// Ensure the directory exists
				var directory = Path.GetDirectoryName(narrationPath);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}

				// Check if file already exists and delete it
				if (File.Exists(narrationPath))
				{
					StopAndDisposePlayer();
					File.Delete(narrationPath);
				}

				using (var audioStream = audioFile.GetAudioStream())
				using (var fileStream = File.Create(narrationPath))
				{
					// Save the recording with the same name as the photo but with .mp3 extension
					await audioStream.CopyToAsync(fileStream);
				}

				StatusLabel.Text = "Recording saved";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to save recording: {ex.Message}\nPath: {GetNarrationPath()}", "OK");
				StatusLabel.Text = "Failed to save recording";
			}

			_isRecording = false;
		}

		private void OnPlaybackEnded(object s, EventArgs args)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				StopAndDisposePlayer();
				PlayButton.Text = "Play Narration";
				StatusLabel.Text = "Playback finished";
				UpdateControlStates();
			});
		}

		private void StopAndDisposePlayer()
		{
			if (_audioPlayer == null)
			{
				return;
			}

			var player = _audioPlayer;
			_audioPlayer = null; // Null out first to prevent re-entry

			if (player.IsPlaying)
			{
				player.Stop();
			}

			player.PlaybackEnded -= OnPlaybackEnded; // Unsubscribe before dispose
			player.Dispose();
		}

		private async void OnPlayClicked(object sender, EventArgs e)
		{
			if (_isRecording)
			{
				return; // Do not play while recording
			}

			try
			{
				// If we're already playing something, stop it
				if (_audioPlayer?.IsPlaying == true)
				{
					StopAndDisposePlayer();
					PlayButton.Text = "Play Narration";
					StatusLabel.Text = "Playback stopped";
					UpdateControlStates();
					return;
				}

				string narrationPath = GetNarrationPath();
				if (string.IsNullOrEmpty(narrationPath) || !File.Exists(narrationPath))
				{
					StatusLabel.Text = "No narration available for this photo";
					return;
				}

				// Create a new audio player from an in-memory copy so the file is not locked
				byte[] audioBytes = await File.ReadAllBytesAsync(narrationPath).ConfigureAwait(true);
				_audioPlayer = _audioManager.CreatePlayer(new MemoryStream(audioBytes));

				// Handle playback completion
				_audioPlayer.PlaybackEnded += OnPlaybackEnded;

				// Start playback
				_audioPlayer.Play();
				PlayButton.Text = "Stop Playback";
				StatusLabel.Text = "Playing narration...";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to play audio: {ex.Message}", "OK");
				StatusLabel.Text = "Playback failed";
				StopAndDisposePlayer();
			}

			UpdateControlStates();
		}

		private void UpdateControlStates()
		{
			bool hasPhotos = _photoFiles.Count != 0;
			bool isRecording = _isRecording;
			bool isPlaying = _audioPlayer?.IsPlaying == true;
			bool hasNarration = hasPhotos && File.Exists(GetNarrationPath());

			var navColor = (Color)Application.Current!.Resources["NavigationBarColor"];
			var disabledColor = Colors.Gray;

			bool prevEnabled = hasPhotos && _currentPhotoIndex > 0 && !isRecording && !isPlaying;
			PreviousButton.IsEnabled = prevEnabled;
			PreviousButton.BackgroundColor = prevEnabled ? navColor : disabledColor;

			bool nextEnabled = hasPhotos && _currentPhotoIndex < _photoFiles.Count - 1 && !isRecording && !isPlaying;
			NextButton.IsEnabled = nextEnabled;
			NextButton.BackgroundColor = nextEnabled ? navColor : disabledColor;

			RecordButton.IsEnabled = hasPhotos && !isPlaying;
			if (!isRecording)
			{
				RecordButton.BackgroundColor = hasNarration ? Colors.Red : Colors.Green;
				RecordButton.Text = hasNarration ? "Re-record" : "Record Narration";
			}

			bool playEnabled = hasPhotos && !isRecording && hasNarration;
			PlayButton.IsEnabled = playEnabled;
			PlayButton.BackgroundColor = playEnabled ? navColor : disabledColor;
		}
	}
}