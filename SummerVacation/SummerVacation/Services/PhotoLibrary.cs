using CommunityToolkit.Maui.Storage;

namespace SummerVacation.Services;

/// <summary>
/// Manages the current photo directory, photo list, navigation state, and narration path resolution.
/// </summary>
public sealed class PhotoLibrary
{
	private const string LastDirectoryKey = "LastSelectedDirectory";

	// Desktop platforms support writing to arbitrary directories chosen by the user.
	private static bool CanWriteToPhotoDirectory =>
		DeviceInfo.Platform == DevicePlatform.WinUI ||
		DeviceInfo.Platform == DevicePlatform.macOS ||
		DeviceInfo.Platform == DevicePlatform.MacCatalyst;

	private List<string> _photoFiles = [];

	public string? CurrentDirectory { get; private set; }
	public int CurrentPhotoIndex { get; private set; } = -1;
	public int PhotoCount => _photoFiles.Count;
	public bool HasPhotos => _photoFiles.Count != 0;
	public string? CurrentPhotoPath => CurrentPhotoIndex >= 0 && CurrentPhotoIndex < _photoFiles.Count
		? _photoFiles[CurrentPhotoIndex]
		: null;

	/// <summary>
	/// Loads photos from the last saved directory. Returns true if photos were found.
	/// </summary>
	public bool TryRestoreSavedDirectory()
	{
		var savedDirectory = Preferences.Default.Get(LastDirectoryKey, string.Empty);
		if (string.IsNullOrEmpty(savedDirectory) || !Directory.Exists(savedDirectory))
			return false;

		LoadPhotosFromDirectory(savedDirectory);
		return HasPhotos;
	}

	/// <summary>
	/// Opens the system folder picker and loads photos from the chosen directory.
	/// Returns true if photos were found, false if the user cancelled or no photos exist.
	/// </summary>
	public async Task<bool> PickDirectoryAsync(CancellationToken cancellationToken = default)
	{
		var savedDirectory = Preferences.Default.Get(LastDirectoryKey, string.Empty);
		var initialPath = !string.IsNullOrEmpty(savedDirectory) && Directory.Exists(savedDirectory)
			? savedDirectory
			: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

		var result = await FolderPicker.PickAsync(initialPath, cancellationToken);

		if (!result.IsSuccessful || string.IsNullOrEmpty(result.Folder?.Path))
			return false;

		Preferences.Default.Set(LastDirectoryKey, result.Folder.Path);
		LoadPhotosFromDirectory(result.Folder.Path);
		return true;
	}

	/// <summary>Moves to the previous photo. Returns false if already at the first photo.</summary>
	public bool MovePrevious()
	{
		if (CurrentPhotoIndex <= 0)
			return false;

		CurrentPhotoIndex--;
		return true;
	}

	/// <summary>Moves to the next photo. Returns false if already at the last photo.</summary>
	public bool MoveNext()
	{
		if (CurrentPhotoIndex >= _photoFiles.Count - 1)
			return false;

		CurrentPhotoIndex++;
		return true;
	}

	/// <summary>
	/// Returns the file system path where the narration for the current photo should be stored,
	/// or an empty string if there is no current photo.
	/// </summary>
	public string GetNarrationPath()
	{
		var photoPath = CurrentPhotoPath;
		if (photoPath is null)
			return string.Empty;

		var narrationFileName = Path.ChangeExtension(Path.GetFileName(photoPath), ".mp3");

		if (CanWriteToPhotoDirectory)
			return Path.Combine(CurrentDirectory!, narrationFileName);

		// On Android/iOS write access to arbitrary directories is restricted — use app-private storage.
		var narrationDir = Path.Combine(FileSystem.Current.AppDataDirectory, "PhotoNarration");
		return Path.Combine(narrationDir, narrationFileName);
	}

	private void LoadPhotosFromDirectory(string directory)
	{
		CurrentDirectory = directory;
		_photoFiles = Directory.GetFiles(directory, "*.jpg", SearchOption.TopDirectoryOnly)
			.Concat(Directory.GetFiles(directory, "*.jpeg", SearchOption.TopDirectoryOnly))
			.OrderBy(f => f)
			.ToList();

		CurrentPhotoIndex = _photoFiles.Count != 0 ? 0 : -1;
	}
}
