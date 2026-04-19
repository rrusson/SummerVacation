using System;
using System.Collections.Generic;
using System.Text;

namespace SummerVacation.Services
{
	public class MockSpeechToTextService : ISpeechToTextService
	{
		private bool _isDisposed;

		public async Task SaveTranscriptAsync(string transcriptPath, string photoFileName, string? recognizedText, IReadOnlyList<string> allPhotoFileNames, CancellationToken cancellationToken = default)
		{
		}

		public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
		{
			return true;
		}

		public async Task<string?> StopAsync(CancellationToken cancellationToken = default)
		{
			return null;
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
