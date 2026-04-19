namespace SummerVacation.Services
{
	public interface ISpeechToTextService : IDisposable
	{
		Task SaveTranscriptAsync(string transcriptPath, string photoFileName, string? recognizedText, IReadOnlyList<string> allPhotoFileNames, CancellationToken cancellationToken = default);
		
		Task<bool> StartAsync(CancellationToken cancellationToken = default);
		
		Task<string?> StopAsync(CancellationToken cancellationToken = default);
	}
}