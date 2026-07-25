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
		{
			return false;
		}

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
		{
			return false;
		}

		Preferences.Default.Set(LastDirectoryKey, result.Folder.Path);
		LoadPhotosFromDirectory(result.Folder.Path);
		return true;
	}

	/// <summary>Moves to the previous photo. Returns false if already at the first photo.</summary>
	public bool MovePrevious()
	{
		if (CurrentPhotoIndex <= 0)
		{
			return false;
		}

		CurrentPhotoIndex--;
		return true;
	}

	/// <summary>Moves to the next photo. Returns false if already at the last photo.</summary>
	public bool MoveNext()
	{
		if (CurrentPhotoIndex >= _photoFiles.Count - 1)
		{
			return false;
		}

		CurrentPhotoIndex++;
		return true;
	}

	/// <summary>
	/// Renames the current photo file (and its narration file, if any) to use <paramref name="newBaseName"/>
	/// while preserving the original extension. If a file with the target name already exists, a numeric
	/// suffix is appended to avoid overwriting it. Returns the old and new file names (without directory)
	/// so callers can keep the transcript file in sync, or <see langword="null"/> if the rename could not
	/// be performed (no current photo, empty name, platform can't write to the directory, or name unchanged).
	/// </summary>
	public (string OldFileName, string NewFileName)? RenameCurrentPhoto(string newBaseName)
	{
		var photoPath = CurrentPhotoPath;
		if (photoPath is null || string.IsNullOrWhiteSpace(newBaseName) || !CanWriteToPhotoDirectory)
		{
			return null;
		}

		var directory = Path.GetDirectoryName(photoPath)!;
		var extension = Path.GetExtension(photoPath);
		var oldFileName = Path.GetFileName(photoPath);

		var newFileName = newBaseName + extension;
		if (string.Equals(newFileName, oldFileName, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var newPath = Path.Combine(directory, newFileName);

		// Avoid collisions with an existing file by appending a numeric suffix.
		int suffix = 1;
		while (File.Exists(newPath))
		{
			newFileName = $"{newBaseName}{suffix}{extension}";
			newPath = Path.Combine(directory, newFileName);
			suffix++;
		}

		File.Move(photoPath, newPath);
		_photoFiles[CurrentPhotoIndex] = newPath;

		// Keep the narration file matched with its photo by renaming it too, if one exists.
		var oldNarrationPath = Path.Combine(directory, Path.ChangeExtension(oldFileName, ".mp3"));
		if (File.Exists(oldNarrationPath))
		{
			var newNarrationPath = Path.Combine(directory, Path.ChangeExtension(newFileName, ".mp3"));
			if (!File.Exists(newNarrationPath))
			{
				File.Move(oldNarrationPath, newNarrationPath);
			}
		}

		return (oldFileName, newFileName);
	}

	/// <summary>
	/// Returns the file system path of the narration transcript file for the current photo directory,
	/// or an empty string if no directory is loaded.
	/// </summary>
	public string GetTranscriptPath()
	{
		if (CurrentDirectory is null)
		{
			return string.Empty;
		}

		if (CanWriteToPhotoDirectory)
		{
			return Path.Combine(CurrentDirectory, "narrations.txt");
		}

		var narrationDir = Path.Combine(FileSystem.Current.AppDataDirectory, "PhotoNarration");
		return Path.Combine(narrationDir, "narrations.txt");
	}

	/// <summary>
	/// Returns the file names (without directory) for all loaded photos.
	/// </summary>
	public IReadOnlyList<string> GetAllPhotoFileNames() =>
		_photoFiles.Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToList();

	/// <summary>
	/// Returns the file system path where the narration for the current photo should be stored,
	/// or an empty string if there is no current photo.
	/// </summary>
	public string GetNarrationPath()
	{
		var photoPath = CurrentPhotoPath;
		if (photoPath is null)
		{
			return string.Empty;
		}

		var narrationFileName = Path.ChangeExtension(Path.GetFileName(photoPath), ".mp3");

		if (CanWriteToPhotoDirectory)
		{
			return Path.Combine(CurrentDirectory!, narrationFileName);
		}

		// On Android/iOS write access to arbitrary directories is restricted — use app-private storage.
		var narrationDir = Path.Combine(FileSystem.Current.AppDataDirectory, "PhotoNarration");
		return Path.Combine(narrationDir, narrationFileName);
	}

	private static readonly HashSet<string> PhotoExtensions =
		new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tiff", ".tif" };

	private void LoadPhotosFromDirectory(string directory)
	{
		CurrentDirectory = directory;
		_photoFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
			.Where(f => PhotoExtensions.Contains(Path.GetExtension(f)))
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
			.ToList();

		CurrentPhotoIndex = _photoFiles.Count != 0 ? 0 : -1;
	}
}
