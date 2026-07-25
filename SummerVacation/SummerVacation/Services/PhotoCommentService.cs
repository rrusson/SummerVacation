using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

using ISImage = SixLabors.ImageSharp.Image;

namespace SummerVacation.Services;

/// <inheritdoc cref="IPhotoCommentService"/>
public sealed class PhotoCommentService : IPhotoCommentService
{
	private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png"];

	public async Task<bool> EmbedCommentAsync(string? photoPath, string? comment, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrWhiteSpace(photoPath) || !File.Exists(photoPath))
		{
			return false;
		}

		var extension = Path.GetExtension(photoPath).ToLowerInvariant();
		if (!SupportedExtensions.Contains(extension))
		{
			return false;
		}

		// Preserve the original timestamps so re-saving doesn't disturb the photo's file metadata.
		var creationTimeUtc = File.GetCreationTimeUtc(photoPath);
		var lastWriteTimeUtc = File.GetLastWriteTimeUtc(photoPath);
		var lastAccessTimeUtc = File.GetLastAccessTimeUtc(photoPath);

		var tempPath = photoPath + ".tmp" + extension;

		try
		{
			using (var image = await ISImage.LoadAsync(photoPath, cancellationToken).ConfigureAwait(false))
			{
				image.Metadata.ExifProfile ??= new ExifProfile();
				image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, comment);

				if (extension == ".png")
				{
					var textData = image.Metadata.GetPngMetadata().TextData;
					var existingEntries = textData.Where(entry => entry.Keyword == "Comment").ToList();
					foreach (var existingEntry in existingEntries)
					{
						textData.Remove(existingEntry);
					}
					textData.Add(new PngTextData("Comment", comment, string.Empty, string.Empty));

					await image.SaveAsPngAsync(tempPath, new PngEncoder(), cancellationToken).ConfigureAwait(false);
				}
				else
				{
					await image.SaveAsJpegAsync(tempPath, new JpegEncoder(), cancellationToken).ConfigureAwait(false);
				}
			}

			File.Copy(tempPath, photoPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				File.Delete(tempPath);
			}
		}

		// Restore the original timestamps that Save/Copy would otherwise reset.
		File.SetCreationTimeUtc(photoPath, creationTimeUtc);
		File.SetLastWriteTimeUtc(photoPath, lastWriteTimeUtc);
		File.SetLastAccessTimeUtc(photoPath, lastAccessTimeUtc);

		return true;
	}

	/// <inheritdoc/>
	public string? GenerateFileNameFromComment(string? comment, int wordCount = 10)
	{
		if (string.IsNullOrWhiteSpace(comment))
		{
			return null;
		}

		var words = comment
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Take(wordCount)
			.Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
			.Where(word => word.Length > 0)
			.Select(word => char.ToUpperInvariant(word[0]) + word[1..])
			.ToArray();

		if (words.Length == 0)
		{
			return null;
		}

		return string.Concat(words);
	}
}
