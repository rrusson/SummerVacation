using SummerVacation.Services;

#if WINDOWS
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace SummerVacation
{
	public partial class MainPage : ContentPage
	{
		private const string Error = "Error";
		private const string SelectDirectoryText = "S̲elect Photo Directory";
		private const string RecordNarrationText = "R̲ecord Narration";
		private const string ReRecordNarrationText = "R̲e-record";
		private const string PlayNarrationText = "P̲lay Narration";

		private readonly PhotoLibrary _photoLibrary;
		private readonly AudioNarrationService _narrationService;
		private readonly ISpeechToTextService _speechToTextService;
		private readonly IPhotoCommentService _photoCommentService;

		public MainPage(PhotoLibrary photoLibrary, AudioNarrationService narrationService, ISpeechToTextService speechToTextService, IPhotoCommentService photoCommentService)
		{
			InitializeComponent();
			_photoLibrary = photoLibrary;
			_narrationService = narrationService;
			_speechToTextService = speechToTextService;
			_photoCommentService = photoCommentService;
			_narrationService.PlaybackEnded += OnPlaybackEnded;
			SelectDirectoryButton.Text = SelectDirectoryText;
			RecordButton.Text = RecordNarrationText;
			PlayButton.Text = PlayNarrationText;
#if WINDOWS
			Loaded += OnPageLoaded;
			Unloaded += OnPageUnloaded;
#endif
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

		private async Task ShowSpeechPermissionGuidanceAsync()
		{
			string message = DeviceInfo.Platform == DevicePlatform.WinUI
				? """
					Speech transcription requires Online Speech Recognition to be enabled.

					To fix this on Windows:
					1. Open Settings
					2. Go to Privacy & security → Speech
					3. Turn on "Online speech recognition"

					Audio will still be recorded; only transcription will be skipped.
					"""
				: "Speech recognition permission was not granted. Audio will still be recorded, but transcription will be skipped.";
			await DisplayAlertAsync("Transcription Unavailable", message, "OK");
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
				await ShowTranscriptForCurrentPhotoAsync();
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, "Failed to load photo: " + ex.Message, "OK");
				StatusLabel.Text = "Error loading photo";
			}
		}

		/// <summary>
		/// Displays the saved transcript for the current photo, if any exists, and hides the overlay when
		/// there is no narration text available (e.g. after navigating to a photo with no narration).
		/// </summary>
		private async Task ShowTranscriptForCurrentPhotoAsync()
		{
			var photoFileName = Path.GetFileName(_photoLibrary.CurrentPhotoPath ?? string.Empty);
			if (string.IsNullOrWhiteSpace(photoFileName))
			{
				TranscriptOverlay.IsVisible = false;
				return;
			}

			try
			{
				var transcript = await _speechToTextService.GetTranscriptForPhotoAsync(_photoLibrary.GetTranscriptPath(), photoFileName);
				if (string.IsNullOrWhiteSpace(transcript))
				{
					TranscriptOverlay.IsVisible = false;
				}
				else
				{
					TranscriptLabel.Text = transcript;
					TranscriptOverlay.IsVisible = true;
				}
			}
			catch (Exception)
			{
				TranscriptOverlay.IsVisible = false;
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
				await SaveRecording();
				return;
			}

			try
			{
				await _narrationService.StartRecordingAsync();
				bool transcribing = await _speechToTextService.StartAsync();

				if (!transcribing)
				{
					await ShowSpeechPermissionGuidanceAsync();
				}

				StatusLabel.Text = "Recording...";
				RecordButton.Text = "Stop R̲ecording";
				TranscriptOverlay.IsVisible = false;
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

		private async Task SaveRecording()
		{
			StatusLabel.Text = "Saving recording...";
			try
			{
				var narrationPath = _photoLibrary.GetNarrationPath();
				bool hadExistingNarration = File.Exists(narrationPath);

				// Give the platform some time to fire the final RecognitionResultUpdated event after stopping.
				await Task.Delay(500).ConfigureAwait(true);
				string? recognizedText = await _speechToTextService.StopAsync();

				if (string.IsNullOrWhiteSpace(recognizedText))
				{
					if (hadExistingNarration)
					{
						bool replace = await DisplayAlertAsync(
							"No Speech Detected",
							"No words were detected in the recording. Do you want to replace the existing narration with this empty recording?",
							"Replace",
							"Keep Existing");

						if (!replace)
						{
							await _narrationService.DiscardRecordingAsync();
							StatusLabel.Text = "Existing narration kept";
							RecordButton.Text = ReRecordNarrationText;
							UpdateControlStates();
							return;
						}
					}
					else
					{
						await DisplayAlertAsync("No Speech Detected", "No words were detected in the recording.", "OK");
					}
				}

				await _narrationService.StopAndSaveAsync(narrationPath);

				var photoFileName = Path.GetFileName(_photoLibrary.CurrentPhotoPath ?? string.Empty);
				if (!string.IsNullOrWhiteSpace(photoFileName))
				{
					await _speechToTextService.SaveTranscriptAsync(
						_photoLibrary.GetTranscriptPath(),
						photoFileName,
						recognizedText,
						_photoLibrary.GetAllPhotoFileNames());
				}

				if (_photoLibrary.CurrentPhotoPath is string currentPhotoPath)
				{
					try
					{
						await _photoCommentService.EmbedCommentAsync(currentPhotoPath, recognizedText);
					}
					catch (Exception ex)
					{
						// Embedding the comment is a best-effort enhancement; don't fail the whole save if it doesn't work.
						System.Diagnostics.Debug.WriteLine($"Failed to embed comment into photo: {ex.Message}");
					}
				}

				StatusLabel.Text = "Recording saved";
				RecordButton.Text = ReRecordNarrationText;

				// Display the newly captured text immediately, as if the user tapped the photo.
				await ShowTranscriptForCurrentPhotoAsync();
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to save recording: {ex.Message}", "OK");
				StatusLabel.Text = "Failed to save recording";
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
				PlayButton.Text = PlayNarrationText;
				StatusLabel.Text = "Playback stopped";
				UpdateControlStates();
				return;
			}

			try
			{
				await _narrationService.PlayAsync(_photoLibrary.GetNarrationPath());
				PlayButton.Text = "Stop P̲layback";
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
				PlayButton.Text = PlayNarrationText;
				StatusLabel.Text = "Playback finished";
				UpdateControlStates();
			});
		}

		private async void OnPhotoTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
		{
			if (TranscriptOverlay.IsVisible)
			{
				TranscriptOverlay.IsVisible = false;
				return;
			}

			var photoFileName = Path.GetFileName(_photoLibrary.CurrentPhotoPath ?? string.Empty);
			if (string.IsNullOrWhiteSpace(photoFileName))
			{
				return;
			}

			try
			{
				var transcript = await _speechToTextService.GetTranscriptForPhotoAsync(_photoLibrary.GetTranscriptPath(), photoFileName);
				TranscriptLabel.Text = transcript ?? "No transcription available for this photo.";
				TranscriptOverlay.IsVisible = true;
			}
			catch (Exception)
			{
				await DisplayAlertAsync(Error, "Unable to load the transcript for this photo.", "OK");
			}
		}

		private void OnTranscriptOverlayTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
		{
			TranscriptOverlay.IsVisible = false;
		}

		private void UpdateControlStates()
		{
			bool hasPhotos = _photoLibrary.HasPhotos;
			bool isRecording = _narrationService.IsRecording;
			bool isPlaying = _narrationService.IsPlaying;
			bool hasNarration = hasPhotos && File.Exists(_photoLibrary.GetNarrationPath());

			var navColor = (Color)Microsoft.Maui.Controls.Application.Current!.Resources["NavigationBarColor"];
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
				RecordButton.Text = hasNarration ? ReRecordNarrationText : RecordNarrationText;
			}

			bool playEnabled = hasPhotos && !isRecording && hasNarration;
			PlayButton.IsEnabled = playEnabled;
			PlayButton.BackgroundColor = playEnabled ? navColor : disabledColor;
		}

		// Windows Hotkeys Wiring: Left Arrow = Previous, Right Arrow = Next, R = Record, P = Play, S = Select Directory
#if WINDOWS
		private void OnPageLoaded(object? sender, EventArgs e)
		{
			if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
			{
				frameworkElement.KeyDown += OnWindowsKeyDown;
				frameworkElement.IsTabStop = true;
				frameworkElement.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
			}
		}

		private void OnPageUnloaded(object? sender, EventArgs e)
		{
			if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
			{
				frameworkElement.KeyDown -= OnWindowsKeyDown;
			}
		}

		private void OnWindowsKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Handled)
			{
				return;
			}

			switch (e.Key)
			{
				case VirtualKey.Left when PreviousButton.IsEnabled:
					OnPreviousClicked(this, EventArgs.Empty);
					e.Handled = true;
					break;
				case VirtualKey.Right when NextButton.IsEnabled:
					OnNextClicked(this, EventArgs.Empty);
					e.Handled = true;
					break;
				case VirtualKey.R when RecordButton.IsEnabled:
					OnRecordClicked(this, EventArgs.Empty);
					e.Handled = true;
					break;
				case VirtualKey.P when PlayButton.IsEnabled:
					OnPlayClicked(this, EventArgs.Empty);
					e.Handled = true;
					break;
				case VirtualKey.S when SelectDirectoryButton.IsEnabled:
					OnSelectDirectoryClicked(this, EventArgs.Empty);
					e.Handled = true;
					break;
			}
		}
#endif
	}
}