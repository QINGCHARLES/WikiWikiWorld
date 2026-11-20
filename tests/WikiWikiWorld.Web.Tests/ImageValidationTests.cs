using Microsoft.AspNetCore.Http;
using System.Text;
using WikiWikiWorld.Web.Helpers;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies that image upload validation accepts supported web-baseline formats and blocks HEIC/HEIF uploads.
/// </summary>
public sealed class ImageValidationTests
{
	private static readonly byte[] HeicHeaderBytes =
	[
		0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63
	];

	/// <summary>
	/// Verifies that <see cref="ImageValidationHelper.IsValidImageFile"/> rejects HEIC and HEIF uploads,
	/// including mismatched content types or HEIC payloads renamed to JPEG.
	/// </summary>
	/// <param name="FileName">The uploaded file name.</param>
	/// <param name="ContentType">The uploaded MIME type.</param>
	/// <param name="UseHeicBytes">Whether to supply HEIC container header bytes.</param>
	[Theory]
	[InlineData("photo.heic", "image/heic", true)]
	[InlineData("photo.HEIC", "image/heic", true)]
	[InlineData("photo.heif", "image/heif", true)]
	[InlineData("photo.jpg", "image/heic", false)]
	[InlineData("photo.jpg", "image/jpeg", true)]
	public void IsValidImageFile_RejectsHeicAndHeifUploads(string FileName, string ContentType, bool UseHeicBytes)
	{
		byte[] Payload = UseHeicBytes ? HeicHeaderBytes : [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
		IFormFile File = CreateFormFile(FileName, ContentType, Payload);

		bool IsValid = ImageValidationHelper.IsValidImageFile(File, out string ValidationError);

		Assert.False(IsValid);
		Assert.False(string.IsNullOrWhiteSpace(ValidationError));
	}

	/// <summary>
	/// Verifies that <see cref="ImageValidationHelper.IsImageFileSignature"/> rejects HEIC and HEIF extensions.
	/// </summary>
	/// <param name="FileExtension">The file extension to check.</param>
	[Theory]
	[InlineData(".heic")]
	[InlineData(".HEIC")]
	[InlineData(".heif")]
	[InlineData(".HEIF")]
	public void IsImageFileSignature_RejectsHeicAndHeifExtensions(string FileExtension)
	{
		bool IsValid = ImageValidationHelper.IsImageFileSignature(HeicHeaderBytes, FileExtension);

		Assert.False(IsValid);
	}

	/// <summary>
	/// Verifies that the allowed extensions, MIME types, and HTML accept list exclude HEIC/HEIF
	/// and contain all supported web-baseline image formats.
	/// </summary>
	[Fact]
	public void AllowedFormats_ExcludeHeicAndIncludeSupportedBaselineFormats()
	{
		Assert.DoesNotContain(".heic", ImageValidationHelper.AllowedImageExtensions);
		Assert.DoesNotContain(".heif", ImageValidationHelper.AllowedImageExtensions);
		Assert.DoesNotContain("image/heic", ImageValidationHelper.AllowedMimeTypes);
		Assert.DoesNotContain("image/heif", ImageValidationHelper.AllowedMimeTypes);
		Assert.DoesNotContain("heic", ImageValidationHelper.AcceptedFileTypes, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("heif", ImageValidationHelper.AcceptedFileTypes, StringComparison.OrdinalIgnoreCase);

		string[] ExpectedExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".svg"];
		foreach (string Extension in ExpectedExtensions)
		{
			Assert.Contains(Extension, ImageValidationHelper.AllowedImageExtensions);
			Assert.Contains(Extension, ImageValidationHelper.AcceptedFileTypes, StringComparison.OrdinalIgnoreCase);
		}

		string[] ExpectedMimeTypes =
		[
			"image/jpeg", "image/jpg", "image/png", "image/gif",
			"image/webp", "image/avif", "image/svg+xml"
		];
		foreach (string MimeType in ExpectedMimeTypes)
		{
			Assert.Contains(MimeType, ImageValidationHelper.AllowedMimeTypes);
		}
	}

	/// <summary>
	/// Verifies that <see cref="ImageValidationHelper.IsValidImageFile"/> continues to accept valid
	/// JPEG, PNG, GIF, WebP, AVIF, and SVG uploads.
	/// </summary>
	/// <param name="FileName">The uploaded file name.</param>
	/// <param name="ContentType">The uploaded MIME type.</param>
	/// <param name="SignatureBytes">The representative file header bytes.</param>
	[Theory]
	[MemberData(nameof(GetValidBaselineSamples))]
	public void IsValidImageFile_AcceptsSupportedBaselineFormats(string FileName, string ContentType, byte[] SignatureBytes)
	{
		IFormFile File = CreateFormFile(FileName, ContentType, SignatureBytes);

		bool IsValid = ImageValidationHelper.IsValidImageFile(File, out string ValidationError);

		Assert.True(IsValid, ValidationError);
		Assert.Equal(string.Empty, ValidationError);
	}

	/// <summary>
	/// Provides representative valid header payloads for each supported image format.
	/// </summary>
	/// <returns>Test cases containing filename, MIME type, and file bytes.</returns>
	public static TheoryData<string, string, byte[]> GetValidBaselineSamples()
	{
		TheoryData<string, string, byte[]> Cases = new();
		Cases.Add("cover.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]);
		Cases.Add("cover.jpeg", "image/jpg", [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x10, 0x45, 0x78]);
		Cases.Add("scan.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		Cases.Add("anim.gif", "image/gif", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00]);
		Cases.Add("page.webp", "image/webp", [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]);
		Cases.Add("hero.avif", "image/avif", [0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66]);
		Cases.Add("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
		return Cases;
	}

	/// <summary>
	/// Wraps raw payload bytes in an <see cref="IFormFile"/> with the specified filename and content type.
	/// </summary>
	/// <param name="FileName">The reported file name.</param>
	/// <param name="ContentType">The reported MIME type.</param>
	/// <param name="Payload">The file byte contents.</param>
	/// <returns>An in-memory <see cref="IFormFile"/> ready for validation.</returns>
	private static IFormFile CreateFormFile(string FileName, string ContentType, byte[] Payload)
	{
		MemoryStream Stream = new(Payload);
		return new FormFile(Stream, 0, Payload.Length, "UploadedFile", FileName)
		{
			Headers = new HeaderDictionary(),
			ContentType = ContentType
		};
	}
}
