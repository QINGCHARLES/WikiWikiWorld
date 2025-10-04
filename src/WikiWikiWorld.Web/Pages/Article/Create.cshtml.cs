using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.IO;
using System.Linq;

namespace WikiWikiWorld.Web.Pages.Article;

[Authorize] // ✅ Requires user authentication
public sealed class CreateModel(
	IArticleRevisionRepository ArticleRevisionRepository,
	IFileRevisionRepository FileRevisionRepository,
	UserManager<ApplicationUser> UserManager,
	IWebHostEnvironment WebHostEnvironment,
	SiteResolverService SiteResolverService)
	: BasePageModel(SiteResolverService)
{
	private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".heic"
	};

	private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/jpeg", "image/jpg", "image/png", "image/gif",
		"image/webp", "image/avif", "image/heic"
	};

	[BindProperty]
	public string Title { get; set; } = string.Empty;

	[BindProperty]
	public string DisplayTitle { get; set; } = string.Empty;

	[BindProperty]
	public string UrlSlug { get; set; } = string.Empty;

	[BindProperty]
	public string ArticleText { get; set; } = string.Empty;

	[BindProperty]
	public ArticleType SelectedType { get; set; } = ArticleType.Article; // ✅ Default to "Article"

	[BindProperty]
	public IFormFile? UploadedFile { get; set; }

	public string ErrorMessage { get; private set; } = string.Empty;

	public List<ArticleType> AvailableArticleTypes { get; } = [.. Enum.GetValues<ArticleType>()
		.Where(Type => Type != ArticleType.User)]; // ✅ Exclude "User"

	public async Task<IActionResult> OnPostAsync()
	{
		if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(UrlSlug) || string.IsNullOrWhiteSpace(ArticleText))
		{
			ErrorMessage = "All fields are required.";
			return Page();
		}

		// ✅ Ensure user is logged in
		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		// ✅ Check if the article already exists
		ArticleRevision? ExistingArticle = await ArticleRevisionRepository.GetCurrentBySiteIdCultureAndUrlSlugAsync(
			SiteId,
			Culture,
			UrlSlug);

		if (ExistingArticle is not null)
		{
			ErrorMessage = "An article with this URL slug already exists.";
			return Page();
		}

		// Generate a canonical article ID
		Guid CanonicalArticleId = Guid.NewGuid();

		// Handle file upload if present
		Guid? CanonicalFileId = null;
		if (UploadedFile is not null && UploadedFile.Length > 0)
		{
			// Validate image file
			if (!IsValidImageFile(UploadedFile, out string ValidationError))
			{
				ErrorMessage = ValidationError;
				return Page();
			}

			try
			{
				// Generate file ID and path
				CanonicalFileId = Guid.NewGuid();
				string OriginalFileName = Path.GetFileName(UploadedFile.FileName);
				string FileExtension = Path.GetExtension(OriginalFileName);
				string UniqueFileName = $"{CanonicalFileId}{FileExtension}";

				// Create site files images directory if it doesn't exist
				string SiteFilesDirectory = Path.Combine(WebHostEnvironment.WebRootPath, "sitefiles", SiteId.ToString(), "images");
				Directory.CreateDirectory(SiteFilesDirectory);

				// Save the file to the file system
				string FilePath = Path.Combine(SiteFilesDirectory, UniqueFileName);
				using FileStream FileStream = new(FilePath, FileMode.Create);
				await UploadedFile.CopyToAsync(FileStream);

				// Save the file revision to database
				await FileRevisionRepository.InsertAsync(
					CanonicalFileId: CanonicalFileId,
					Type: FileType.Image2D,
					Filename: OriginalFileName,
					MimeType: UploadedFile.ContentType,
					FileSizeBytes: UploadedFile.Length,
					Source: null,
					RevisionReason: "Initial upload",
					SourceAndRevisionReasonCulture: Culture,
					CreatedByUserId: CurrentUserId.Value);

				// If this is a file-type article, update the article type
				SelectedType = ArticleType.File;
			}
			catch (Exception Ex)
			{
				ErrorMessage = $"Error uploading file: {Ex.Message}";
				return Page();
			}
		}

		// ✅ Insert new article
		await ArticleRevisionRepository.InsertAsync(
			CanonicalArticleId: CanonicalArticleId,
			SiteId: SiteId,
			Culture: Culture,
			Title: Title,
			DisplayTitle: DisplayTitle, // Use the DisplayTitle from the form
			UrlSlug: UrlSlug,
			Type: SelectedType, // ✅ Store selected type
			CanonicalFileId: CanonicalFileId,
			Text: ArticleText,
			RevisionReason: "New article creation",
			CreatedByUserId: CurrentUserId.Value);

		// Redirect to the newly created article
		return Redirect($"/{UrlSlug}");
	}

	// ✅ Helper method to get the current user ID
	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}

	/// <summary>
	/// Validates that the uploaded file is a valid image by checking:
	/// 1. The file extension matches allowed image extensions
	/// 2. The MIME type is an allowed image type
	/// 3. The file size is reasonable for an image
	/// </summary>
	private static bool IsValidImageFile(IFormFile UploadedFile, out string ValidationError)
	{
		ValidationError = string.Empty;

		// Check file extension
		string FileExtension = Path.GetExtension(UploadedFile.FileName);
		if (!AllowedImageExtensions.Contains(FileExtension))
		{
			ValidationError = $"File extension {FileExtension} is not allowed. Allowed image extensions: {string.Join(", ", AllowedImageExtensions)}";
			return false;
		}

		// Check MIME type
		if (!AllowedMimeTypes.Contains(UploadedFile.ContentType))
		{
			ValidationError = $"File type {UploadedFile.ContentType} is not allowed. Only image files are permitted.";
			return false;
		}

		// Check file size (limit to 10 MB)
		const long MaxFileSizeBytes = 10 * 1024 * 1024;
		if (UploadedFile.Length > MaxFileSizeBytes)
		{
			ValidationError = $"File size exceeds the maximum allowed size of 10 MB.";
			return false;
		}

		// Basic signature check - read first few bytes to verify it's an image
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
	/// Checks file signatures against known image format signatures
	/// </summary>
	private static bool IsImageFileSignature(byte[] Signature, string FileExtension)
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

		// WebP: 52 49 46 46 ?? ?? ?? ?? 57 45 42 50
		// We can check only the first 4 bytes (RIFF) as a simplification
		if (FileExtension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return Signature.Length >= 4 &&
				   Signature[0] == 0x52 &&
				   Signature[1] == 0x49 &&
				   Signature[2] == 0x46 &&
				   Signature[3] == 0x46;
		}

		// For AVIF and HEIC, we would need more complex checks
		// For now, we'll trust the extension and MIME type for these formats
		if (FileExtension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
			FileExtension.Equals(".heic", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}
}