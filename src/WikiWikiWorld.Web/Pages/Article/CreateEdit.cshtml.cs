using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

[Authorize]
public sealed class CreateEditModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
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

    // Used to determine edit mode - if UrlSlug is provided via query string, we're editing
    [BindProperty(SupportsGet = true)]
    public string? UrlSlug { get; set; }

    // Original slug for edit mode (persists the original value during postback)
    [BindProperty]
    public string OriginalUrlSlug { get; set; } = string.Empty;

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string DisplayTitle { get; set; } = string.Empty;

    [BindProperty]
    public string ArticleText { get; set; } = string.Empty;

    [BindProperty]
    public ArticleType SelectedType { get; set; } = ArticleType.Article;

    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    public string ErrorMessage { get; private set; } = string.Empty;

    public List<ArticleType> AvailableArticleTypes { get; } = [.. Enum.GetValues<ArticleType>()
        .Where(Type => Type != ArticleType.User)];

    // Determines if we're in edit mode based on presence of UrlSlug
    public bool IsEditMode => !string.IsNullOrWhiteSpace(UrlSlug);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Challenge();
        }

        // Edit mode: Load existing article data
        if (IsEditMode)
        {
            var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, UrlSlug!, IsCurrent: true);
            ArticleRevision? CurrentArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

            if (CurrentArticle is null)
            {
                return NotFound("Article not found.");
            }

            // Populate form with existing data
            OriginalUrlSlug = CurrentArticle.UrlSlug;
            Title = CurrentArticle.Title;
            DisplayTitle = CurrentArticle.DisplayTitle ?? string.Empty;
            ArticleText = CurrentArticle.Text;
            SelectedType = CurrentArticle.Type;
        }
        // Create mode: Form starts blank

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(UrlSlug) || string.IsNullOrWhiteSpace(ArticleText))
        {
            ErrorMessage = "All required fields must be filled out.";
            return Page();
        }

        // Ensure user is authenticated
        Guid? CurrentUserId = GetCurrentUserId();
        if (CurrentUserId is null)
        {
            return Challenge();
        }

        // In POST, determine mode by checking if OriginalUrlSlug was set (edit mode)
        bool IsEditModePost = !string.IsNullOrWhiteSpace(OriginalUrlSlug);

        if (IsEditModePost)
        {
            return await HandleEditAsync(CurrentUserId.Value);
        }
        else
        {
            return await HandleCreateAsync(CurrentUserId.Value);
        }
    }

    private async Task<IActionResult> HandleCreateAsync(Guid CurrentUserId)
    {
        // Check if article with this slug already exists
        var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, UrlSlug!, IsCurrent: true);
        ArticleRevision? ExistingArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

        if (ExistingArticle is not null)
        {
            ErrorMessage = "An article with this URL slug already exists.";
            return Page();
        }

        // Generate canonical article ID
        Guid CanonicalArticleId = Guid.NewGuid();

        // Handle file upload if present
        Guid? CanonicalFileId = null;
        if (UploadedFile is not null && UploadedFile.Length > 0)
        {
            if (!IsValidImageFile(UploadedFile, out string ValidationError))
            {
                ErrorMessage = ValidationError;
                return Page();
            }

            try
            {
                CanonicalFileId = Guid.NewGuid();
                string OriginalFileName = Path.GetFileName(UploadedFile.FileName);
                string FileExtension = Path.GetExtension(OriginalFileName);
                string UniqueFileName = $"{CanonicalFileId}{FileExtension}";

                string SiteFilesDirectory = Path.Combine(
                    WebHostEnvironment.WebRootPath,
                    "sitefiles",
                    SiteId.ToString(),
                    "images");
                Directory.CreateDirectory(SiteFilesDirectory);

                string FilePath = Path.Combine(SiteFilesDirectory, UniqueFileName);
                using FileStream FileStream = new(FilePath, FileMode.Create);
                await UploadedFile.CopyToAsync(FileStream);

                var NewFile = new FileRevision
                {
                    CanonicalFileId = CanonicalFileId.Value,
                    Type = FileType.Image2D,
                    Filename = OriginalFileName,
                    MimeType = UploadedFile.ContentType,
                    FileSizeBytes = UploadedFile.Length,
                    Source = null,
                    RevisionReason = "Initial upload",
                    SourceAndRevisionReasonCulture = Culture,
                    CreatedByUserId = CurrentUserId,
                    DateCreated = DateTimeOffset.UtcNow,
                    IsCurrent = true
                };

                Context.FileRevisions.Add(NewFile);
                await Context.SaveChangesAsync();

                SelectedType = ArticleType.File;
            }
            catch (Exception Ex)
            {
                ErrorMessage = $"Error uploading file: {Ex.Message}";
                return Page();
            }
        }

        // Insert new article
        var NewArticle = new ArticleRevision
        {
            CanonicalArticleId = CanonicalArticleId,
            SiteId = SiteId,
            Culture = Culture,
            Title = Title,
            DisplayTitle = DisplayTitle,
            UrlSlug = UrlSlug!,
            Type = SelectedType,
            CanonicalFileId = CanonicalFileId,
            Text = ArticleText,
            RevisionReason = "New article creation",
            CreatedByUserId = CurrentUserId,
            DateCreated = DateTimeOffset.UtcNow,
            IsCurrent = true
        };

        Context.ArticleRevisions.Add(NewArticle);
        await Context.SaveChangesAsync();

        return Redirect($"/{UrlSlug}");
    }

    private async Task<IActionResult> HandleEditAsync(Guid CurrentUserId)
    {
        // Fetch existing article using original slug
        var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, OriginalUrlSlug, IsCurrent: true);
        ArticleRevision? CurrentArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

        if (CurrentArticle is null)
        {
            return NotFound("Article not found.");
        }

        // Check if new URL slug conflicts with another article
        if (!OriginalUrlSlug.Equals(UrlSlug, StringComparison.OrdinalIgnoreCase))
        {
            var ConflictSpec = new ArticleRevisionsBySlugSpec(SiteId, Culture, UrlSlug!, IsCurrent: true);
            ArticleRevision? ExistingArticle = await Context.ArticleRevisions.WithSpecification(ConflictSpec).FirstOrDefaultAsync();

            if (ExistingArticle is not null)
            {
                ErrorMessage = "An article with this URL Slug already exists.";
                return Page();
            }
        }

        // Set current revision to not current
        CurrentArticle.IsCurrent = false;
        Context.ArticleRevisions.Update(CurrentArticle);
        await Context.SaveChangesAsync();

        // Insert new revision with updates
        var NewRevision = new ArticleRevision
        {
            CanonicalArticleId = CurrentArticle.CanonicalArticleId,
            SiteId = SiteId,
            Culture = Culture,
            Title = Title,
            DisplayTitle = DisplayTitle,
            UrlSlug = UrlSlug!,
            Type = SelectedType,
            CanonicalFileId = CurrentArticle.CanonicalFileId,
            Text = ArticleText,
            RevisionReason = "User edit",
            CreatedByUserId = CurrentUserId,
            DateCreated = DateTimeOffset.UtcNow,
            IsCurrent = true
        };

        Context.ArticleRevisions.Add(NewRevision);
        await Context.SaveChangesAsync();

        return Redirect($"/{UrlSlug}");
    }

    private Guid? GetCurrentUserId()
    {
        string? UserIdString = UserManager.GetUserId(User);
        return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
    }

    private static bool IsValidImageFile(IFormFile UploadedFile, out string ValidationError)
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

        const long MaxFileSizeBytes = 10 * 1024 * 1024;
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
