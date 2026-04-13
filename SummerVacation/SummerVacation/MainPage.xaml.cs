using System.Collections.ObjectModel;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Plugin.Maui.Audio;
using SummerVacation.Services;
using SummerVacation.Shared.Services;

namespace SummerVacation
{
    public partial class MainPage : ContentPage
    {
        private readonly IAudioManager _audioManager;
        private IAudioRecorder? _audioRecorder;
        private IAudioPlayer? _audioPlayer;
        private bool _isRecording;

        private string? currentDirectory;
        private List<string> photoFiles = new();
        private int currentPhotoIndex = -1;

        public MainPage(IAudioManager audioManager)
        {
            InitializeComponent();
            _audioManager = audioManager;
            UpdateControlStates();
        }

        private async void OnSelectDirectoryClicked(object sender, EventArgs e)
        {
            try
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
                        return;
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
                            return;
                        }
                    }
                }

                FolderPickerResult result = await FolderPicker.PickAsync(Convert.ToString(DeviceInfo.Platform == DevicePlatform.Android
                    ? FolderPicker.PickAsync(initialPath: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));

                // If platform is Android, start at Pictures directory
                //FolderPickerResult resultX = await FolderPicker.PickAsync(initialPath: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

                if (result.IsSuccessful && !string.IsNullOrEmpty(result.Folder?.Path))
                {
                    currentDirectory = result.Folder.Path;
                    DirectoryPathLabel.Text = currentDirectory;
                    StatusLabel.Text = "Loading photos...";

                    // Get all jpg files in the directory and sort them by name
                    photoFiles = Directory.GetFiles(currentDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(currentDirectory, "*.jpeg", SearchOption.TopDirectoryOnly))
                        .OrderBy(f => f)
                        .ToList();

                    if (photoFiles.Any())
                    {
                        currentPhotoIndex = 0;
                        await LoadCurrentPhoto();
                    }
                    else
                    {
                        StatusLabel.Text = "No photos found in selected directory";
                        NoPhotoLabel.IsVisible = true;
                        PhotoDisplay.Source = null;
                        currentPhotoIndex = -1;
                    }

                    UpdateControlStates();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", "Failed to access the selected directory: " + ex.Message, "OK");
                StatusLabel.Text = "Directory selection failed";
            }
        }

        private async Task LoadCurrentPhoto()
        {
            if (currentPhotoIndex >= 0 && currentPhotoIndex < photoFiles.Count)
            {
                try
                {
                    // Create a FileImageSource instead of using a stream
                    PhotoDisplay.Source = ImageSource.FromFile(photoFiles[currentPhotoIndex]);
                    NoPhotoLabel.IsVisible = false;

                    // Update status with current photo info
                    var fileName = Path.GetFileName(photoFiles[currentPhotoIndex]);
                    StatusLabel.Text = $"Photo {currentPhotoIndex + 1} of {photoFiles.Count}: {fileName}";
                }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("Error", "Failed to load photo: " + ex.Message, "OK");
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
            if (currentPhotoIndex > 0)
            {
                currentPhotoIndex--;
                await LoadCurrentPhoto();
                UpdateControlStates();
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            if (currentPhotoIndex < photoFiles.Count - 1)
            {
                currentPhotoIndex++;
                await LoadCurrentPhoto();
                UpdateControlStates();
            }
        }

        private string GetNarrationPath()
        {
            if (currentPhotoIndex < 0 || currentPhotoIndex >= photoFiles.Count)
                return string.Empty;

            // Always save in the app's private directory under PhotoNarration folder
            var appDataDir = FileSystem.Current.AppDataDirectory;
            var narrationDir = Path.Combine(appDataDir, "PhotoNarration");

            // Use the photo's filename (without path) as the base for the narration filename
            var photoFileName = Path.GetFileName(photoFiles[currentPhotoIndex]);
            var narrationFileName = Path.ChangeExtension(photoFileName, ".mp3");

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

                _audioRecorder = _audioManager.CreateRecorder();

                await _audioRecorder.StartAsync();
                StatusLabel.Text = "Recording...";
                RecordButton.Text = "Stop Recording";
                _isRecording = true;
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", "Failed to start recording: " + ex.Message, "OK");
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

                // Save the recording with the same name as the photo but with .mp3 extension
                var narrationPath = GetNarrationPath();
                if (!string.IsNullOrEmpty(narrationPath))
                {
                    // Ensure the directory exists
                    var directory = Path.GetDirectoryName(narrationPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Check if file already exists and delete it
                    if (File.Exists(narrationPath))
                    {
                        File.Delete(narrationPath);
                    }

                    using (var audioStream = audioFile.GetAudioStream())
                    using (var fileStream = File.Create(narrationPath))
                    {
                        await audioStream.CopyToAsync(fileStream);
                    }

                    StatusLabel.Text = "Recording saved";
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync(
                    "Error",
                    $"Failed to save recording: {ex.Message}\nPath: {GetNarrationPath()}",
                    "OK");
                StatusLabel.Text = "Failed to save recording";
            }

            RecordButton.Text = "Record Narration";
            _isRecording = false;
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
                    _audioPlayer.Stop();
                    _audioPlayer = null;
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

                // Create a new audio player directly from the file
                _audioPlayer = _audioManager.CreatePlayer(File.OpenRead(narrationPath));

                // Handle playback completion
                _audioPlayer.PlaybackEnded += (s, args) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _audioPlayer = null;
                        PlayButton.Text = "Play Narration";
                        StatusLabel.Text = "Playback finished";
                        UpdateControlStates();
                    });
                };

                // Start playback
                _audioPlayer.Play();
                PlayButton.Text = "Stop Playback";
                StatusLabel.Text = "Playing narration...";
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Failed to play audio: {ex.Message}", "OK");
                StatusLabel.Text = "Playback failed";
                _audioPlayer = null;
            }

            UpdateControlStates();
        }


        private void UpdateControlStates()
        {
            bool hasPhotos = photoFiles.Any();
            bool isRecording = _isRecording;
            bool isPlaying = _audioPlayer?.IsPlaying == true;

            PreviousButton.IsEnabled = hasPhotos && currentPhotoIndex > 0 && !isRecording && !isPlaying;
            NextButton.IsEnabled = hasPhotos && currentPhotoIndex < photoFiles.Count - 1 && !isRecording && !isPlaying;
            RecordButton.IsEnabled = hasPhotos && !isPlaying;
            PlayButton.IsEnabled = hasPhotos && !isRecording; //&& File.Exists(GetNarrationPath());
        }
    }
}