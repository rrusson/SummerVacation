namespace SummerVacation.Services
{
	public class MockSpeechToTextService : ISpeechToTextService
	{
		private bool _isDisposed;

		public Task SaveTranscriptAsync(string transcriptPath, string photoFileName, string? recognizedText, IReadOnlyList<string> allPhotoFileNames, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<string?> GetTranscriptForPhotoAsync(string transcriptPath, string photoFileName, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task<bool> StartAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(true);
		}

		public Task<string?> StopAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task RenamePhotoInTranscriptAsync(string transcriptPath, string oldFileName, string newFileName, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!_isDisposed)
			{
				_isDisposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
