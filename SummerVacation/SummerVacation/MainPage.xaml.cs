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
		private static readonly TimeSpan MinimumSlideshowDuration = TimeSpan.FromSeconds(5);
		private static readonly TimeSpan SlideshowNarrationPadding = TimeSpan.FromSeconds(1);

		private readonly PhotoLibrary _photoLibrary;
		private readonly AudioNarrationService _narrationService;
		private readonly ISpeechToTextService _speechToTextService;
		private readonly IPhotoCommentService _photoCommentService;
		private string _currentFileName;
		private bool _isSlideshowMode;
		private bool _isSaving;
		private CancellationTokenSource? _slideshowCancellationTokenSource;

		public bool ShowTranscriptOverlay
		{
			get;
			set
			{
				field = value;
				ShowTranscriptForCurrentPhotoAsync().ConfigureAwait(true);
			}
		} = true;

		public MainPage(PhotoLibrary photoLibrary, AudioNarrationService narrationService, ISpeechToTextService speechToTextService, IPhotoCommentService photoCommentService)
		{
			InitializeComponent();
			_currentFileName = string.Empty;
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

		protected override void OnDisappearing()
		{
			_ = EndSlideshowModeAsync(resetToFirstPhoto: false);
			base.OnDisappearing();
		}

		private async void OnSelectDirectoryClicked(object sender, EventArgs e)
		{
			if (_isSaving || _isSlideshowMode || _narrationService.IsRecording)
			{
				return;
			}

			try
			{
				if (!await HasStoragePermissionAsync().ConfigureAwait(true))
				{
					StatusLabel.Text = "Permission to access photos is required.";
					return;
				}

				StatusLabel.Text = "Loading photos...";
				bool picked = await _photoLibrary.PickDirectoryAsync().ConfigureAwait(true);

				if (!picked)
				{
					return;
				}

				DirectoryPathLabel.Text = _photoLibrary.CurrentDirectory;

				if (_photoLibrary.HasPhotos)
				{
					await LoadCurrentPhotoAsync().ConfigureAwait(true);
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

		private async Task LoadCurrentPhotoAsync(CancellationToken cancellationToken = default)
		{
			var photoPath = _photoLibrary.CurrentPhotoPath;
			var photoFileName = photoPath is null ? string.Empty : Path.GetFileName(photoPath);
			var transcriptPath = _photoLibrary.GetTranscriptPath();
			if (cancellationToken.IsCancellationRequested || !IsCurrentPhoto(photoPath))
			{
				return;
			}

			if (photoPath is null)
			{
				PhotoDisplay.Source = null;
				NoPhotoLabel.IsVisible = true;
				StatusLabel.Text = "No photos to display";
				return;
			}

			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				PhotoDisplay.Source = ImageSource.FromFile(photoPath);
				NoPhotoLabel.IsVisible = false;
				_currentFileName = photoFileName;
				StatusLabel.Text = $"Photo {_photoLibrary.CurrentPhotoIndex + 1} of {_photoLibrary.PhotoCount}: {_currentFileName}";
				await ShowTranscriptForPhotoAsync(transcriptPath, photoFileName, photoPath, cancellationToken).ConfigureAwait(true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				if (cancellationToken.IsCancellationRequested || !IsCurrentPhoto(photoPath))
				{
					return;
				}

				await DisplayAlertAsync(Error, "Failed to load photo: " + ex.Message, "OK");
				StatusLabel.Text = "Error loading photo";
			}
		}

		/// <summary>
		/// Displays the saved transcript for the current photo, if any exists, and hides the overlay when
		/// there is no narration text available (e.g. after navigating to a photo with no narration) or when overlay is toggled off.
		/// </summary>
		private async Task ShowTranscriptForCurrentPhotoAsync()
		{
			var photoPath = _photoLibrary.CurrentPhotoPath;
			if (photoPath is null)
			{
				return;
			}

			await ShowTranscriptForPhotoAsync(
				_photoLibrary.GetTranscriptPath(),
				Path.GetFileName(photoPath),
				photoPath).ConfigureAwait(true);
		}

		private async Task ShowTranscriptForPhotoAsync(
			string transcriptPath,
			string photoFileName,
			string photoPath,
			CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested || !IsCurrentPhoto(photoPath))
			{
				return;
			}

			// Ensure any in-progress edit from a previous photo is discarded when switching photos.
			TranscriptEditor.IsVisible = false;
			TranscriptDoneButton.IsVisible = false;
			TranscriptLabel.IsVisible = true;

			try
			{
				var transcript = await _speechToTextService.GetTranscriptForPhotoAsync(transcriptPath, photoFileName).ConfigureAwait(true);
				if (cancellationToken.IsCancellationRequested || !IsCurrentPhoto(photoPath))
				{
					return;
				}

				TranscriptLabel.Text = transcript;
				TranscriptOverlay.IsVisible = !string.IsNullOrWhiteSpace(transcript) && ShowTranscriptOverlay;
			}
			catch (Exception)
			{
				if (!cancellationToken.IsCancellationRequested && IsCurrentPhoto(photoPath))
				{
					TranscriptOverlay.IsVisible = false;
				}
			}
		}

		private bool IsCurrentPhoto(string? photoPath) =>
			string.Equals(_photoLibrary.CurrentPhotoPath, photoPath, StringComparison.Ordinal);

		private async void OnPreviousClicked(object sender, EventArgs e)
		{
			if (!_isSaving && _photoLibrary.MovePrevious())
			{
				if (_isSlideshowMode)
				{
					await StartSlideshowForCurrentPhotoAsync().ConfigureAwait(true);
				}
				else
				{
					await LoadCurrentPhotoAsync().ConfigureAwait(true);
					UpdateControlStates();
				}
			}
		}

		private async void OnNextClicked(object sender, EventArgs e)
		{
			if (!_isSaving && _photoLibrary.MoveNext())
			{
				if (_isSlideshowMode)
				{
					await StartSlideshowForCurrentPhotoAsync().ConfigureAwait(true);
				}
				else
				{
					await LoadCurrentPhotoAsync().ConfigureAwait(true);
					UpdateControlStates();
				}
			}
		}

		private async void OnSlideshowClicked(object sender, EventArgs e)
		{
			if (_isSlideshowMode || _isSaving || _narrationService.IsRecording || !_photoLibrary.HasPhotos)
			{
				return;
			}

			_isSlideshowMode = true;
			CloseTranscriptEditor();
			_photoLibrary.MoveToFirst();
			UpdateControlStates();
			await StartSlideshowForCurrentPhotoAsync().ConfigureAwait(true);
		}

		private async Task StartSlideshowForCurrentPhotoAsync()
		{
			if (!_isSlideshowMode || _isSaving)
			{
				return;
			}

			CancelSlideshowTimer();
			var transition = new CancellationTokenSource();
			_slideshowCancellationTokenSource = transition;
			var cancellationToken = transition.Token;
			var photoPath = _photoLibrary.CurrentPhotoPath;
			var narrationPath = _photoLibrary.GetNarrationPath();

			if (photoPath is null)
			{
				await EndSlideshowModeAsync(resetToFirstPhoto: false).ConfigureAwait(true);
				return;
			}

			_narrationService.StopPlayback();
			try
			{
				await LoadCurrentPhotoAsync(cancellationToken).ConfigureAwait(true);
				if (!IsCurrentSlideshowTransition(transition, photoPath))
				{
					return;
				}

				var slideDuration = await PlayNarrationForSlideshowAsync(narrationPath, cancellationToken).ConfigureAwait(true);
				if (!IsCurrentSlideshowTransition(transition, photoPath))
				{
					return;
				}

				StartSlideshowTimer(slideDuration, transition);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
		}

		private async Task<TimeSpan> PlayNarrationForSlideshowAsync(string narrationPath, CancellationToken cancellationToken)
		{
			try
			{
				var narrationDuration = await _narrationService.PlayWithDurationAsync(narrationPath, cancellationToken).ConfigureAwait(true);
				return GetSlideshowSlideDuration(narrationDuration);
			}
			catch (FileNotFoundException)
			{
				return MinimumSlideshowDuration;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception)
			{
				return MinimumSlideshowDuration;
			}
		}

		private static TimeSpan GetSlideshowSlideDuration(TimeSpan? narrationDuration)
		{
			if (narrationDuration is null || narrationDuration <= TimeSpan.Zero)
			{
				return MinimumSlideshowDuration;
			}

			var extendedDuration = narrationDuration.Value + SlideshowNarrationPadding;
			return extendedDuration > MinimumSlideshowDuration
				? extendedDuration
				: MinimumSlideshowDuration;
		}

		private void StartSlideshowTimer(TimeSpan slideDuration, CancellationTokenSource transition)
		{
			if (ReferenceEquals(_slideshowCancellationTokenSource, transition))
			{
				_ = AdvanceSlideshowAfterDelayAsync(slideDuration, transition);
			}
		}

		private async Task AdvanceSlideshowAfterDelayAsync(TimeSpan slideDuration, CancellationTokenSource transition)
		{
			var cancellationToken = transition.Token;
			try
			{
				await Task.Delay(slideDuration, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (!IsCurrentSlideshowTransition(transition))
			{
				return;
			}

			await MainThread.InvokeOnMainThreadAsync(async () =>
			{
				if (!IsCurrentSlideshowTransition(transition))
				{
					return;
				}

				if (_photoLibrary.MoveNext())
				{
					await StartSlideshowForCurrentPhotoAsync().ConfigureAwait(true);
				}
				else
				{
					await EndSlideshowModeAsync(resetToFirstPhoto: true).ConfigureAwait(true);
				}
			}).ConfigureAwait(true);
		}

		private async Task EndSlideshowModeAsync(bool resetToFirstPhoto)
		{
			if (!_isSlideshowMode && _slideshowCancellationTokenSource is null)
			{
				return;
			}

			_isSlideshowMode = false;
			CancelSlideshowTimer();
			_narrationService.StopPlayback();
			PlayButton.Text = PlayNarrationText;
			UpdateControlStates();

			if (_photoLibrary.HasPhotos)
			{
				if (resetToFirstPhoto)
				{
					_photoLibrary.MoveToFirst();
				}

				await LoadCurrentPhotoAsync().ConfigureAwait(true);
			}

			UpdateControlStates();
		}

		private bool IsCurrentSlideshowTransition(CancellationTokenSource transition, string? photoPath = null) =>
			_isSlideshowMode &&
			ReferenceEquals(_slideshowCancellationTokenSource, transition) &&
			!transition.IsCancellationRequested &&
			(photoPath is null || IsCurrentPhoto(photoPath));

		private void CloseTranscriptEditor()
		{
			TranscriptEditor.Unfocus();
			TranscriptEditor.IsVisible = false;
			TranscriptDoneButton.IsVisible = false;
			TranscriptLabel.IsVisible = true;
		}

		private void CancelSlideshowTimer()
		{
			if (_slideshowCancellationTokenSource is null)
			{
				return;
			}

			_slideshowCancellationTokenSource.Cancel();
			_slideshowCancellationTokenSource.Dispose();
			_slideshowCancellationTokenSource = null;
		}

		private async void OnRecordClicked(object sender, EventArgs e)
		{
			if (_isSaving || _isSlideshowMode)
			{
				return;
			}

			if (_narrationService.IsRecording)
			{
				await SaveRecording();
				return;
			}

			try
			{
				await _narrationService.StartRecordingAsync();
				UpdateControlStates();
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
			var photoPath = _photoLibrary.CurrentPhotoPath;
			if (photoPath is null)
			{
				return;
			}

			var photoFileName = _currentFileName;
			var narrationPath = _photoLibrary.GetNarrationPath();
			var transcriptPath = _photoLibrary.GetTranscriptPath();
			var photoFileNames = _photoLibrary.GetAllPhotoFileNames();
			bool autoRename = AutoRenameCheckBox.IsChecked;
			bool hadExistingNarration = File.Exists(narrationPath);

			_isSaving = true;
			StatusLabel.Text = "Saving recording...";
			UpdateControlStates();
			await Task.Yield();
			try
			{
				// Give the platform some time to fire the final RecognitionResultUpdated event after stopping.
				await Task.Delay(500).ConfigureAwait(true);
				// Run on the UI thread so the follow-up alerts aren't shown from a background thread.
				string? recognizedText = await MainThread.InvokeOnMainThreadAsync(() => _speechToTextService.StopAsync()).ConfigureAwait(true);

				var tempAudioPath = await _narrationService.StopToTempFileAsync().ConfigureAwait(true);
				if (string.IsNullOrWhiteSpace(recognizedText) && tempAudioPath is not null)
				{
					StatusLabel.Text = "Transcribing...";
					recognizedText = await _speechToTextService.TranscribeFileAsync(tempAudioPath).ConfigureAwait(true);
				}

				if (string.IsNullOrWhiteSpace(recognizedText))
				{
					if (hadExistingNarration)
					{
						bool replace = await DisplayAlertAsync(
							"No Speech Detected",
							"No words were detected in the recording. Do you want to replace the existing narration with this empty recording?",
							"Replace",
							"Keep Existing").ConfigureAwait(true);

						if (!replace)
						{
							if (tempAudioPath is not null)
							{
								File.Delete(tempAudioPath);
							}

							StatusLabel.Text = "Existing narration kept";
							RecordButton.Text = ReRecordNarrationText;
							UpdateControlStates();
							return;
						}
					}
					else
					{
						await DisplayAlertAsync("No Speech Detected", "No words were detected in the recording.", "OK").ConfigureAwait(true);
					}
				}

				if (tempAudioPath is not null)
				{
					_narrationService.CommitRecording(tempAudioPath, narrationPath);
				}

				if (!string.IsNullOrWhiteSpace(photoFileName))
				{
					await _speechToTextService.SaveTranscriptAsync(
						transcriptPath,
						photoFileName,
						recognizedText,
						photoFileNames).ConfigureAwait(true);
				}

				try
				{
					await _photoCommentService.EmbedCommentAsync(photoPath, recognizedText).ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					// Embedding the comment is a best-effort enhancement; don't fail the whole save if it doesn't work.
					System.Diagnostics.Debug.WriteLine($"Failed to embed comment into photo: {ex.Message}");
				}

				await RenamePhotoFromNarrationIfRequestedAsync(recognizedText, photoPath, transcriptPath, autoRename).ConfigureAwait(true);

				StatusLabel.Text = "Recording saved";
				RecordButton.Text = ReRecordNarrationText;

				// Display the newly captured text immediately, as if the user tapped the photo.
				await ShowTranscriptForCurrentPhotoAsync().ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to save recording: {ex.Message}", "OK").ConfigureAwait(true);
				StatusLabel.Text = "Failed to save recording";
			}

			finally
			{
				_isSaving = false;
				UpdateControlStates();
			}
		}

		private async void OnPlayClicked(object sender, EventArgs e)
		{
			if (_isSaving || _isSlideshowMode || _narrationService.IsRecording)
			{
				return;
			}

			if (_narrationService.IsPlaying)
			{
				_narrationService.StopPlayback();
				PlayButton.Text = PlayNarrationText;
				StatusLabel.Text = $"Photo {_photoLibrary.CurrentPhotoIndex + 1} of {_photoLibrary.PhotoCount}: {_currentFileName}";
				UpdateControlStates();
				return;
			}

			try
			{
				await _narrationService.PlayAsync(_photoLibrary.GetNarrationPath()).ConfigureAwait(true);
				PlayButton.Text = "Stop P̲layback";
				StatusLabel.Text = "Playing narration...";
			}
			catch (FileNotFoundException)
			{
				StatusLabel.Text = "No narration available for this photo";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to play audio: {ex.Message}", "OK").ConfigureAwait(true);
				StatusLabel.Text = "Playback failed!";
			}

			UpdateControlStates();
		}

		private void OnPlaybackEnded(object? sender, EventArgs e)
		{
			MainThread.BeginInvokeOnMainThread(() =>
			{
				PlayButton.Text = PlayNarrationText;
				StatusLabel.Text = $"Photo {_photoLibrary.CurrentPhotoIndex + 1} of {_photoLibrary.PhotoCount}: {_currentFileName}";
				UpdateControlStates();
			});
		}

		private void OnAutoRenameLabelTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
		{
			if (_isSaving || _isSlideshowMode)
			{
				return;
			}

			AutoRenameCheckBox.IsChecked = !AutoRenameCheckBox.IsChecked;
		}

		/// <summary>
		/// If the auto-rename checkbox is checked, renames the current photo file (and keeps the narration
		/// file and transcript entry in sync) using the first 10 words of <paramref name="narrationText"/>
		/// </summary>
		private async Task RenamePhotoFromNarrationIfRequestedAsync(
			string? narrationText,
			string? photoPath,
			string transcriptPath,
			bool renameRequested)
		{
			if (!renameRequested || photoPath is null || string.IsNullOrWhiteSpace(narrationText))
			{
				return;
			}

			var newBaseName = _photoCommentService.GenerateFileNameFromComment(narrationText);
			if (string.IsNullOrEmpty(newBaseName))
			{
				return;
			}

			var renameResult = _photoLibrary.RenamePhoto(photoPath, newBaseName);
			if (renameResult is null)
			{
				return;
			}

			try
			{
				await _speechToTextService.RenamePhotoInTranscriptAsync(
					transcriptPath,
					renameResult.Value.OldFileName,
					renameResult.Value.NewFileName).ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to update transcript entry after rename: {ex.Message}");
			}

			var renamedPhotoPath = Path.Combine(Path.GetDirectoryName(photoPath)!, renameResult.Value.NewFileName);
			if (IsCurrentPhoto(renamedPhotoPath))
			{
				_currentFileName = renameResult.Value.NewFileName;
				PhotoDisplay.Source = ImageSource.FromFile(renamedPhotoPath);
				StatusLabel.Text = $"Photo {_photoLibrary.CurrentPhotoIndex + 1} of {_photoLibrary.PhotoCount}: {_currentFileName}";
			}
		}

		private async void OnPhotoTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
		{
			ShowTranscriptOverlay = !ShowTranscriptOverlay;
		}

		private void OnTranscriptOverlayTapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
		{
			// Ignore taps that landed on the Editor or the Done button; those have their own handlers.
			if (_isSaving || _isSlideshowMode || _narrationService.IsRecording || TranscriptEditor.IsVisible)
			{
				return;
			}

			// Switch the overlay into edit mode: hide the read-only label, show the editable Editor
			// pre-populated with the current transcript text, and reveal the "Done" button.
			TranscriptEditor.Text = TranscriptLabel.Text;
			TranscriptLabel.IsVisible = false;
			TranscriptEditor.IsVisible = true;
			TranscriptDoneButton.IsVisible = true;
			TranscriptEditor.Focus();
		}

		private async void OnTranscriptEditorCompleted(object? sender, EventArgs e)
		{
			if (_isSaving || _isSlideshowMode || _narrationService.IsRecording)
			{
				return;
			}

			// Pressing Enter/Return in the Entry saves the transcript and switches back to display mode, same as clicking the Done button.
			await SaveTranscriptEditAsync().ConfigureAwait(true);
		}

		private async Task SaveTranscriptEditAsync()
		{
			if (_isSaving || _isSlideshowMode || _narrationService.IsRecording)
			{
				return;
			}

			string editedText = TranscriptEditor.Text ?? string.Empty;
			var photoPath = _photoLibrary.CurrentPhotoPath;
			var photoFileName = _currentFileName;
			var transcriptPath = _photoLibrary.GetTranscriptPath();
			var photoFileNames = _photoLibrary.GetAllPhotoFileNames();
			bool autoRename = AutoRenameCheckBox.IsChecked;

			// Switch back to read-only display mode immediately for responsiveness.
			CloseTranscriptEditor();
			TranscriptLabel.Text = editedText;
			TranscriptOverlay.IsVisible = !string.IsNullOrWhiteSpace(editedText) && ShowTranscriptOverlay;

			if (photoPath is null || string.IsNullOrWhiteSpace(photoFileName))
			{
				return;
			}

			_isSaving = true;
			UpdateControlStates();
			try
			{
				await _speechToTextService.SaveTranscriptAsync(
					transcriptPath,
					photoFileName,
					editedText,
					photoFileNames).ConfigureAwait(true);

				try
				{
					await _photoCommentService.EmbedCommentAsync(photoPath, editedText).ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					// Embedding the comment is a best-effort enhancement; don't fail the whole save if it doesn't work.
					System.Diagnostics.Debug.WriteLine($"Failed to embed comment into photo: {ex.Message}");
				}

				await RenamePhotoFromNarrationIfRequestedAsync(editedText, photoPath, transcriptPath, autoRename).ConfigureAwait(true);

				StatusLabel.Text = "Transcript updated";
			}
			catch (Exception ex)
			{
				await DisplayAlertAsync(Error, $"Failed to save transcript: {ex.Message}", "OK").ConfigureAwait(true);
				StatusLabel.Text = "Failed to save transcript";
			}
			finally
			{
				_isSaving = false;
				UpdateControlStates();
			}
		}

		private void UpdateControlStates()
		{
			bool hasPhotos = _photoLibrary.HasPhotos;
			bool isRecording = _narrationService.IsRecording;
			bool isPlaying = _narrationService.IsPlaying;
			bool hasNarration = hasPhotos && File.Exists(_photoLibrary.GetNarrationPath());

			var navColor = (Color)Application.Current!.Resources["NavigationBarColor"];
			var disabledColor = Colors.Gray;

			bool prevEnabled = hasPhotos && !_isSaving && _photoLibrary.CurrentPhotoIndex > 0 && (_isSlideshowMode || (!isRecording && !isPlaying));
			PreviousButton.IsEnabled = prevEnabled;
			PreviousButton.BackgroundColor = prevEnabled ? navColor : disabledColor;

			bool nextEnabled = hasPhotos && !_isSaving && _photoLibrary.CurrentPhotoIndex < _photoLibrary.PhotoCount - 1 && (_isSlideshowMode || (!isRecording && !isPlaying));
			NextButton.IsEnabled = nextEnabled;
			NextButton.BackgroundColor = nextEnabled ? navColor : disabledColor;

			SelectDirectoryButton.IsEnabled = !_isSlideshowMode && !_isSaving && !isRecording;
			SelectDirectoryButton.BackgroundColor = SelectDirectoryButton.IsEnabled ? navColor : disabledColor;

			RecordButton.IsEnabled = hasPhotos && !isPlaying && !_isSaving && !_isSlideshowMode;
			if (!isRecording)
			{
				RecordButton.BackgroundColor = hasNarration ? Colors.Red : Colors.Green;
				RecordButton.Text = hasNarration ? ReRecordNarrationText : RecordNarrationText;
			}

			if (_isSlideshowMode || _isSaving)
			{
				RecordButton.IsEnabled = false;
				RecordButton.BackgroundColor = disabledColor;
			}

			bool playEnabled = hasPhotos && !isRecording && hasNarration && !_isSaving && !_isSlideshowMode;
			PlayButton.IsEnabled = playEnabled;
			PlayButton.BackgroundColor = playEnabled ? navColor : disabledColor;

			SlideshowButton.IsEnabled = hasPhotos && !_isSlideshowMode && !isRecording && !_isSaving;
			SlideshowButton.BackgroundColor = SlideshowButton.IsEnabled ? navColor : disabledColor;
			AutoRenameControlGroup.IsEnabled = !_isSlideshowMode && !_isSaving;
			AutoRenameCheckBox.IsEnabled = !_isSlideshowMode && !_isSaving;
			TranscriptEditor.IsReadOnly = _isSlideshowMode || _isSaving;
			TranscriptDoneButton.IsEnabled = !_isSlideshowMode && !_isSaving;
		}

		// Windows Hotkeys Wiring: Left Arrow = Previous, Right Arrow = Next, R = Record, P = Play, S = Select Directory, Escape = Exit Slideshow/Toggle Transcript Overlay
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

			// Don't intercept keystrokes as app hotkeys while the user is typing in the transcript editor.
			if (TranscriptEditor.IsFocused)
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
				case VirtualKey.Escape:
					if (_isSlideshowMode)
					{
						_ = EndSlideshowModeAsync(resetToFirstPhoto: false);
					}
					else
					{
						ShowTranscriptOverlay = !ShowTranscriptOverlay;
					}
					e.Handled = true;
					break;
			}
		}
#endif
	}
}