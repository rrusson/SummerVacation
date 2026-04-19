using SummerVacation.Services;

namespace SummerVacation
{
	public partial class MainPage : ContentPage
	{
		private const string Error = "Error";

		private readonly PhotoLibrary _photoLibrary;
		private readonly AudioNarrationService _narrationService;

		public MainPage(PhotoLibrary photoLibrary, AudioNarrationService narrationService)
		{
			InitializeComponent();
			_photoLibrary = photoLibrary;
			_narrationService = narrationService;
			_narrationService.PlaybackEnded += OnPlaybackEnded;
			UpdateControlStates();
		}

		protected override async void OnAppearing()
		{
			base.OnAppearing();

			StatusLabel.Text = "Loading photos...";
			if (!_photoLibrary.TryRestoreSavedDirectory())
			{
				StatusLabel.Text = "No photos found in saved directory";
				UpdateControlStates();
				return;
			}

			DirectoryPathLabel.Text = _photoLibrary.CurrentDirectory;
			await LoadCurrentPhotoAsync();
			UpdateControlStates();
		}

		private async void OnSelectDirectoryClicked(object sender, EventArgs e)
		{
			try
			{
				if (!await HasStoragePermissionAsync())
				{
					StatusLabel.Text = "Permission to access photos is required.";
					return;
				}

				StatusLabel.Text = "Loading photos...";
				bool picked = await _photoLibrary.PickDirectoryAsync();

				if (!picked)
				{
					return;
				}

				DirectoryPathLabel.Text = _photoLibrary.CurrentDirectory;

				if (_photoLibrary.HasPhotos)
				{
					await LoadCurrentPhotoAsync();
				}
				else
				{
					StatusLabel.Text = "No photos found in selected directory";
					NoPhotoLabel.IsVisible = true;
					PhotoDisplay.Source = null;
				}

				UpdateControlStates();
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to access the selected directory: " + ex.Message, "OK");
				StatusLabel.Text = "Directory selection failed";
			}
		}

		private async Task<bool> HasStoragePermissionAsync()
		{
			if (DeviceInfo.Platform != DevicePlatform.Android)
			{
				return true;
			}

			var storageStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
			if (storageStatus != PermissionStatus.Granted)
			{
				await DisplayAlertAsync("Permission Required", "Storage permission is required to access photos.", "OK");
				return false;
			}

			if (OperatingSystem.IsAndroidVersionAtLeast(33))
			{
				var photoStatus = await Permissions.RequestAsync<Permissions.Photos>();
				if (photoStatus != PermissionStatus.Granted)
				{
					await DisplayAlertAsync("Permission Required", "Photos permission is required to access images.", "OK");
					return false;
				}
			}

			return true;
		}

		private async Task LoadCurrentPhotoAsync()
		{
			var photoPath = _photoLibrary.CurrentPhotoPath;
			if (photoPath is null)
			{
				PhotoDisplay.Source = null;
				NoPhotoLabel.IsVisible = true;
				StatusLabel.Text = "No photo to display";
				return;
			}

			try
			{
				PhotoDisplay.Source = ImageSource.FromFile(photoPath);
				NoPhotoLabel.IsVisible = false;
				var fileName = Path.GetFileName(photoPath);
				StatusLabel.Text = $"Photo {_photoLibrary.CurrentPhotoIndex + 1} of {_photoLibrary.PhotoCount}: {fileName}";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to load photo: " + ex.Message, "OK");
				StatusLabel.Text = "Error loading photo";
			}
		}

		private async void OnPreviousClicked(object sender, EventArgs e)
		{
			if (_photoLibrary.MovePrevious())
			{
				await LoadCurrentPhotoAsync();
				UpdateControlStates();
			}
		}

		private async void OnNextClicked(object sender, EventArgs e)
		{
			if (_photoLibrary.MoveNext())
			{
				await LoadCurrentPhotoAsync();
				UpdateControlStates();
			}
		}

		private async void OnRecordClicked(object sender, EventArgs e)
		{
			if (_narrationService.IsRecording)
			{
				StatusLabel.Text = "Saving recording...";
				try
				{
					await _narrationService.StopAndSaveAsync(_photoLibrary.GetNarrationPath());
					StatusLabel.Text = "Recording saved";
					RecordButton.Text = "Re-record";
				}
				catch (Exception ex)
				{
					await DisplayAlertAsync(Error, $"Failed to save recording: {ex.Message}", "OK");
					StatusLabel.Text = "Failed to save recording";
				}

				UpdateControlStates();
				return;
			}

			try
			{
				await _narrationService.StartRecordingAsync();
				StatusLabel.Text = "Recording...";
				RecordButton.Text = "Stop Recording";
			}
			catch (UnauthorizedAccessException ex)
			{
				await DisplayAlertAsync("Permission Required", ex.Message, "OK");
				StatusLabel.Text = "Failed to start recording";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to start recording: " + ex.Message, "OK");
				StatusLabel.Text = "Failed to start recording";
			}

			UpdateControlStates();
		}

		private async void OnPlayClicked(object sender, EventArgs e)
		{
			if (_narrationService.IsRecording)
			{
				return;
			}

			if (_narrationService.IsPlaying)
			{
				_narrationService.StopPlayback();
				PlayButton.Text = "Play Narration";
				StatusLabel.Text = "Playback stopped";
				UpdateControlStates();
				return;
			}

			try
			{
				await _narrationService.PlayAsync(_photoLibrary.GetNarrationPath());
				PlayButton.Text = "Stop Playback";
				StatusLabel.Text = "Playing narration...";
			}
			catch (FileNotFoundException)
			{
				StatusLabel.Text = "No narration available for this photo";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to play audio: {ex.Message}", "OK");
				StatusLabel.Text = "Playback failed";
			}

			UpdateControlStates();
		}

		private void OnPlaybackEnded(object? sender, EventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				PlayButton.Text = "Play Narration";
				StatusLabel.Text = "Playback finished";
				UpdateControlStates();
			});
		}

		private void UpdateControlStates()
		{
			bool hasPhotos = _photoLibrary.HasPhotos;
			bool isRecording = _narrationService.IsRecording;
			bool isPlaying = _narrationService.IsPlaying;
			bool hasNarration = hasPhotos && File.Exists(_photoLibrary.GetNarrationPath());

			var navColor = (Color)Application.Current!.Resources["NavigationBarColor"];
			var disabledColor = Colors.Gray;

			bool prevEnabled = hasPhotos && _photoLibrary.CurrentPhotoIndex > 0 && !isRecording && !isPlaying;
			PreviousButton.IsEnabled = prevEnabled;
			PreviousButton.BackgroundColor = prevEnabled ? navColor : disabledColor;

			bool nextEnabled = hasPhotos && _photoLibrary.CurrentPhotoIndex < _photoLibrary.PhotoCount - 1 && !isRecording && !isPlaying;
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