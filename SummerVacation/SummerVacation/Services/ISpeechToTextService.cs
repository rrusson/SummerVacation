namespace SummerVacation.Services
{
	public interface ISpeechToTextService : IDisposable
	{
		Task SaveTranscriptAsync(string transcriptPath, string photoFileName, string? recognizedText, IReadOnlyList<string> allPhotoFileNames, CancellationToken cancellationToken = default);

		/// <summary>Reads the transcript file and returns the transcribed text for <paramref name="photoFileName"/>, or null if not found.</summary>
		Task<string?> GetTranscriptForPhotoAsync(string transcriptPath, string photoFileName, CancellationToken cancellationToken = default);

		Task<bool> StartAsync(CancellationToken cancellationToken = default);

		Task<string?> StopAsync(CancellationToken cancellationToken = default);

		/// <summary>Transcribes a recorded audio file offline. Returns null when nothing is recognized or the platform doesn't support it.</summary>
		Task<string?> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken = default);

		/// <summary>Renames the transcript entry key from <paramref name="oldFileName"/> to <paramref name="newFileName"/>, if present.</summary>
		Task RenamePhotoInTranscriptAsync(string transcriptPath, string oldFileName, string newFileName, CancellationToken cancellationToken = default);
	}
}