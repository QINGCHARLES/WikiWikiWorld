namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Shared helper for validating uploaded image files.
/// </summary>
public static class ImageValidationHelper
{
	/// <summary>
	/// The set of allowed image file extensions (case-insensitive).
	/// </summary>
	public static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".heic"
	};

	/// <summary>
	/// The set of allowed image MIME types (case-insensitive).
	/// </summary>
	public static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/jpeg", "image/jpg", "image/png", "image/gif",
		"image/webp", "image/avif", "image/heic"
	};

	/// <summary>
	/// The maximum allowed file size in bytes (10 MB).
	/// </summary>
	public const long MaxFileSizeBytes = 10 * 1024 * 1024;

	/// <summary>
	/// Validates that an uploaded file is a valid image.
	/// </summary>
	/// <param name="UploadedFile">The file to validate.</param>
	/// <param name="ValidationError">The error message if validation fails.</param>
	/// <returns>True if the file is a valid image; otherwise, false.</returns>
	public static bool IsValidImageFile(IFormFile UploadedFile, out string ValidationError)
	{
		ValidationError = string.Empty;

		string FileExtension = Path.GetExtension(UploadedFile.FileName);
		if (!AllowedImageExtensions.Contains(FileExtension))
		{
			ValidationError = $"File extension {FileExtension} is not allowed. Allowed extensions: {string.Join(", ", AllowedImageExtensions)}";
			return false;
		}

		if (!AllowedMimeTypes.Contains(UploadedFile.ContentType))
		{
			ValidationError = $"File type {UploadedFile.ContentType} is not allowed. Only image files are permitted.";
			return false;
		}

		if (UploadedFile.Length > MaxFileSizeBytes)
		{
			ValidationError = "File size exceeds the maximum allowed size of 10 MB.";
			return false;
		}

		try
		{
			using Stream Stream = UploadedFile.OpenReadStream();
			using BinaryReader Reader = new(Stream);

			byte[] Signature = Reader.ReadBytes(8);
			if (!IsImageFileSignature(Signature, FileExtension))
			{
				ValidationError = "The file does not appear to be a valid image.";
				return false;
			}

			return true;
		}
		catch (Exception Ex)
		{
			ValidationError = $"Error validating image: {Ex.Message}";
			return false;
		}
	}

	/// <summary>
	/// Checks if the file signature matches the expected image format.
	/// </summary>
	/// <param name="Signature">The file signature bytes.</param>
	/// <param name="FileExtension">The file extension.</param>
	/// <returns>True if the signature is valid for the extension; otherwise, false.</returns>
	public static bool IsImageFileSignature(byte[] Signature, string FileExtension)
	{
		// JPEG: FF D8 FF
		if (FileExtension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
			FileExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
		{
			return Signature.Length >= 3 &&
				   Signature[0] == 0xFF &&
				   Signature[1] == 0xD8 &&
				   Signature[2] == 0xFF;
		}

		// PNG: 89 50 4E 47 0D 0A 1A 0A
		if (FileExtension.Equals(".png", StringComparison.OrdinalIgnoreCase))
		{
			return Signature.Length >= 8 &&
				   Signature[0] == 0x89 &&
				   Signature[1] == 0x50 &&
				   Signature[2] == 0x4E &&
				   Signature[3] == 0x47 &&
				   Signature[4] == 0x0D &&
				   Signature[5] == 0x0A &&
				   Signature[6] == 0x1A &&
				   Signature[7] == 0x0A;
		}

		// GIF: 47 49 46 38
		if (FileExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
		{
			return Signature.Length >= 4 &&
				   Signature[0] == 0x47 &&
				   Signature[1] == 0x49 &&
				   Signature[2] == 0x46 &&
				   Signature[3] == 0x38;
		}

		// WebP: 52 49 46 46 (RIFF)
		if (FileExtension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return Signature.Length >= 4 &&
				   Signature[0] == 0x52 &&
				   Signature[1] == 0x49 &&
				   Signature[2] == 0x46 &&
				   Signature[3] == 0x46;
		}

		// For AVIF and HEIC, trust extension and MIME type
		if (FileExtension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
			FileExtension.Equals(".heic", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}
}
