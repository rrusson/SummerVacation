namespace SummerVacation.Services
{
	public interface ISpeechToTextService : IDisposable
	{
		Task SaveTranscriptAsync(string transcriptPath, string photoFileName, string? recognizedText, IReadOnlyList<string> allPhotoFileNames, CancellationToken cancellationToken = default);

		/// <summary>Reads the transcript file and returns the transcribed text for <paramref name="photoFileName"/>, or null if not found.</summary>
		Task<string?> GetTranscriptForPhotoAsync(string transcriptPath, string photoFileName, CancellationToken cancellationToken = default);

		Task<bool> StartAsync(CancellationToken cancellationToken = default);

		Task<string?> StopAsync(CancellationToken cancellationToken = default);
	}
}