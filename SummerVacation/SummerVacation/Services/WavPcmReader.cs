#if WINDOWS
using System.Text;

namespace SummerVacation.Services;

/// <summary>
/// Minimal RIFF/WAV reader for 16-bit PCM audio. Tolerates extra chunks (e.g. JUNK) and 18-byte fmt chunks
/// that some recorders produce and that the Windows SAPI file APIs reject.
/// </summary>
internal static class WavPcmReader
{
	public static bool TryRead(byte[] wav, out byte[] pcm, out int sampleRate, out int channels)
	{
		pcm = [];
		sampleRate = 0;
		channels = 0;

		if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF")
		{
			return false;
		}

		var position = 12;
		while (position + 8 <= wav.Length)
		{
			var chunkId = Encoding.ASCII.GetString(wav, position, 4);
			var chunkSize = BitConverter.ToInt32(wav, position + 4);
			var dataStart = position + 8;

			if (chunkId == "fmt " && dataStart + 16 <= wav.Length)
			{
				var isPcm = BitConverter.ToInt16(wav, dataStart) == 1;
				channels = BitConverter.ToInt16(wav, dataStart + 2);
				sampleRate = BitConverter.ToInt32(wav, dataStart + 4);
				var bitsPerSample = BitConverter.ToInt16(wav, dataStart + 14);
				if (!isPcm || bitsPerSample != 16 || channels is < 1 or > 2)
				{
					return false;
				}
			}
			else if (chunkId == "data")
			{
				if (sampleRate == 0)
				{
					return false;
				}

				var length = chunkSize < 0 || dataStart + chunkSize > wav.Length ? wav.Length - dataStart : chunkSize;
				pcm = new byte[length];
				Array.Copy(wav, dataStart, pcm, 0, length);
				return true;
			}

			if (chunkSize < 0)
			{
				return false;
			}

			position = dataStart + chunkSize + (chunkSize & 1);
		}

		return false;
	}

	/// <summary>Converts 16-bit PCM to mono float samples at <paramref name="targetRate"/> using linear interpolation.</summary>
	public static float[] ToMonoFloat(byte[] pcm, int sampleRate, int channels, int targetRate)
	{
		var frames = pcm.Length / (2 * channels);
		var mono = new float[frames];
		for (var i = 0; i < frames; i++)
		{
			var sum = 0;
			for (var c = 0; c < channels; c++)
			{
				sum += BitConverter.ToInt16(pcm, ((i * channels) + c) * 2);
			}

			mono[i] = sum / (float)channels / short.MaxValue;
		}

		if (sampleRate == targetRate)
		{
			return mono;
		}

		var outLength = (int)((long)frames * targetRate / sampleRate);
		var resampled = new float[outLength];
		var ratio = (double)sampleRate / targetRate;
		for (var i = 0; i < outLength; i++)
		{
			var src = i * ratio;
			var index = (int)src;
			var next = Math.Min(index + 1, frames - 1);
			var fraction = (float)(src - index);
			resampled[i] = mono[index] + ((mono[next] - mono[index]) * fraction);
		}

		return resampled;
	}
}
#endif
