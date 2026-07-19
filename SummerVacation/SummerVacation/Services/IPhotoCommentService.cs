namespace SummerVacation.Services;

/// <summary>
/// Embeds recognized narration text as a comment into a photo's file metadata (EXIF for JPEG,
/// tEXt chunk for PNG) and resaves the file in place without altering its creation/modified timestamps.
/// </summary>
public interface IPhotoCommentService
{
	/// <summary>
	/// Embeds <paramref name="comment"/> into the image file at <paramref name="photoPath"/> and resaves it in place.
	/// Supports .jpg/.jpeg and .png files. The file's creation and last-write timestamps are preserved.
	/// Returns <see langword="false"/> if the file extension is unsupported or the comment is null/empty.
	/// </summary>
	/// <param name="photoPath">The path to the photo file. Can be null.</param>
	/// <param name="comment">The comment to embed. Can be null.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	Task<bool> EmbedCommentAsync(string? photoPath, string? comment, CancellationToken cancellationToken = default);
}
