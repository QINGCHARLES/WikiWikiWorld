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

using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for creating and editing articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="FileStorageOptions">The file storage options.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
[Authorize]
public sealed class CreateEditModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    IOptions<FileStorageOptions> FileStorageOptions,
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
    /// <summary>
    /// Gets or sets the URL slug of the article (if editing).
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? UrlSlug { get; set; }

    // Original slug for edit mode (persists the original value during postback)
    /// <summary>
    /// Gets or sets the original URL slug to track changes during editing.
    /// </summary>
    [BindProperty]
    public string OriginalUrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the article title.
    /// </summary>
    [BindProperty]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the article display title.
    /// </summary>
    [BindProperty]
    public string DisplayTitle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the article markdown content.
    /// </summary>
    [BindProperty]
    public string ArticleText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected article type.
    /// </summary>
    [BindProperty]
    public ArticleType SelectedType { get; set; } = ArticleType.Article;

    /// <summary>
    /// Gets or sets an optional file upload.
    /// </summary>
    [BindProperty]
    public IFormFile? UploadedFile { get; set; }

    /// <summary>
    /// Gets any error message generated during processing.
    /// </summary>
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the list of available article types for selection.
    /// </summary>
    public List<ArticleType> AvailableArticleTypes { get; } = [.. Enum.GetValues<ArticleType>()
        .Where(Type => Type != ArticleType.User)];

    // Determines if we're in edit mode based on presence of UrlSlug
    /// <summary>
    /// Gets a value indicating whether the page is in edit mode.
    /// </summary>
    public bool IsEditMode => !string.IsNullOrWhiteSpace(UrlSlug);

    /// <summary>
    /// Gets a value indicating whether the current article can be reverted to a previous revision.
    /// </summary>
    public bool CanRevert { get; private set; }

    /// <summary>
    /// Handles the GET request to load the form.
    /// </summary>
    /// <returns>The page or result.</returns>
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

            // Check if there is a previous revision to revert to
            // We need to find any revision for the same CanonicalArticleId that is NOT the current one (IsCurrent = false)
            // and is not deleted.
            var PreviousRevisionSpec = new ArticleRevisionsByCanonicalIdSpec(CurrentArticle.CanonicalArticleId, null);
            // We just need to know if ANY exist, so AnyAsync is sufficient.
            // Note: ArticleRevisionsByCanonicalIdSpec might return all revisions, we need to filter.
            // But Spec implementation isn't visible here, assuming it filters by CanonicalId only.
            // Let's manually construct the query to be safe and efficient.
            CanRevert = await Context.ArticleRevisions
                .Where(x => x.CanonicalArticleId == CurrentArticle.CanonicalArticleId && 
                            x.SiteId == SiteId &&
                            x.Culture == Culture &&
                            !x.IsCurrent && 
                            x.DateDeleted == null)
                .AnyAsync();
        }
        // Create mode: Form starts blank
        if (!IsEditMode && !string.IsNullOrWhiteSpace(UrlSlug) && UrlSlug.StartsWith('@'))
        {
            // User Home Page Creation Mode
            string Username = UrlSlug[1..];

            // Verify that the current user matches the requested username
            if (!string.Equals(User.Identity?.Name, Username, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            Title = UrlSlug;
            DisplayTitle = $"User: {Username}";
            SelectedType = ArticleType.User;
        }

        return Page();
    }

    /// <summary>
    /// Handles the POST request to revert an article to a previous revision.
    /// </summary>
    /// <returns>A redirect or page result.</returns>
    public async Task<IActionResult> OnPostRevertAsync()
    {
        // Ensure user is authenticated
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Challenge();
        }

        Guid? CurrentUserId = GetCurrentUserId();
        if (CurrentUserId is null)
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(OriginalUrlSlug))
        {
             return NotFound("Article not found.");
        }

        // Fetch the current revision
        var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, OriginalUrlSlug, IsCurrent: true);
        ArticleRevision? CurrentArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

        if (CurrentArticle is null)
        {
             return NotFound("Article not found.");
        }

        // Fetch the most recent previous revision
        var PreviousRevision = await Context.ArticleRevisions
            .Where(x => x.CanonicalArticleId == CurrentArticle.CanonicalArticleId &&
                        x.SiteId == SiteId &&
                        x.Culture == Culture &&
                        !x.IsCurrent &&
                        x.DateDeleted == null)
            .OrderByDescending(x => x.DateCreated)
            .FirstOrDefaultAsync();

        if (PreviousRevision is null)
        {
             ErrorMessage = "No previous revision found to revert to.";
             return await OnGetAsync(); // Reload page with error
        }

        // Use explicit transaction with high durability to ensure atomicity of the revision swap.
        // Revert operations modify audit trail and should survive crashes.
        // Use explicit transaction with high durability to ensure atomicity of the revision swap.
        // Revert operations modify audit trail and should survive crashes.
        await using var Transaction = await Context.Database.BeginImmediateTransactionAsync();
        using (WriteDurabilityScope.High())
        {
            // Soft delete the current revision
            CurrentArticle.IsCurrent = false;
            CurrentArticle.DateDeleted = DateTimeOffset.UtcNow;
            Context.ArticleRevisions.Update(CurrentArticle);

            // Restore the previous revision
            PreviousRevision.IsCurrent = true;
            
            // Optionally, we could create a NEW revision that helps track who did the revert,
            // but the user requirement was specific: "updates the flags in the db to soft delete the current revision, unflag it as current revision and reflag the previous one as current revision."
            Context.ArticleRevisions.Update(PreviousRevision);
            
            await Context.SaveChangesAsync();
        }
        await Transaction.CommitAsync();

        return Redirect($"/{PreviousRevision.UrlSlug}");
    }

    /// <summary>
    /// Handles the POST request to create or update an article.
    /// </summary>
    /// <returns>A redirect or page result with errors.</returns>
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

        // Enforce ArticleType.User for user home pages
        if (UrlSlug?.StartsWith('@') == true)
        {
            string Username = UrlSlug[1..];
            // Verify that the current user matches the requested username
            if (!string.Equals(User.Identity?.Name, Username, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            SelectedType = ArticleType.User;
        }

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
                // Re-evaluate CanRevert logic if needed, but this is create mode so no revert.
                return Page();
            }

            try
            {
                CanonicalFileId = Guid.NewGuid();
                string OriginalFileName = Path.GetFileName(UploadedFile.FileName);
                string FileExtension = Path.GetExtension(OriginalFileName);
                string UniqueFileName = $"{CanonicalFileId}{FileExtension}";

                string SiteFilesDirectory = Path.Combine(
                    FileStorageOptions.Value.SiteFilesPath,
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
                // Need to re-populate CanRevert before returning Page()
                CanRevert = await Context.ArticleRevisions
                    .Where(x => x.CanonicalArticleId == CurrentArticle.CanonicalArticleId && 
                                x.SiteId == SiteId &&
                                x.Culture == Culture &&
                                !x.IsCurrent && 
                                x.DateDeleted == null)
                    .AnyAsync();
                return Page();
            }
        }

        // Use explicit transaction to ensure atomicity of the revision swap.
        // If creating the new revision fails, we don't want to leave the article with no current revision.
        // Use explicit transaction to ensure atomicity of the revision swap.
        // If creating the new revision fails, we don't want to leave the article with no current revision.
        await using var Transaction = await Context.Database.BeginImmediateTransactionAsync();

        // Set current revision to not current
        CurrentArticle.IsCurrent = false;
        Context.ArticleRevisions.Update(CurrentArticle);

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
        await Transaction.CommitAsync();

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
