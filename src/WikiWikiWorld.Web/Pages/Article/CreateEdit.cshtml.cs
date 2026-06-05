using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for creating and editing articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="ArticleRevisionService">The article revision workflow service.</param>
/// <param name="FileUploadStorageService">The file upload storage service.</param>
[Authorize]
public sealed class CreateEditModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService,
    ArticleRevisionService ArticleRevisionService,
    FileUploadStorageService FileUploadStorageService)
    : BasePageModel(SiteResolverService)
{
    private static readonly HashSet<string> AllowedImageExtensions = ImageValidationHelper.AllowedImageExtensions;

    private static readonly HashSet<string> AllowedMimeTypes = ImageValidationHelper.AllowedMimeTypes;

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

        string? RequestedUrlSlug = UrlSlug;

        if (!string.IsNullOrWhiteSpace(UrlSlug))
        {
            UrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
        }

        // Edit mode: Load existing article data
        if (IsEditMode)
        {
            ArticleRevisionsBySlugSpec Spec = new(UrlSlug!, IsCurrent: true);
            ArticleRevision? CurrentArticle = await Context.ArticleRevisions.AsNoTracking().WithSpecification(Spec).FirstOrDefaultAsync();

            if (CurrentArticle is null)
            {
                return NotFound("Article not found.");
            }

            if (!string.IsNullOrWhiteSpace(RequestedUrlSlug) && ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, CurrentArticle.Type))
            {
                return RedirectPermanent($"{ArticleUrlHelper.BuildArticlePath(CurrentArticle)}/edit");
            }

            // Populate form with existing data
            OriginalUrlSlug = CurrentArticle.UrlSlug;
            // Clear ModelState so asp-for renders the property value, not the query string
            // (which may include the "file:" namespace prefix).
            ModelState.Remove(nameof(UrlSlug));
            UrlSlug = CurrentArticle.UrlSlug;
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
                            !x.IsCurrent)
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
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A redirect or page result.</returns>
    public async Task<IActionResult> OnPostRevertAsync(CancellationToken CancellationToken)
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

        try
        {
            ArticleRevision PreviousRevision = await ArticleRevisionService.RevertToPreviousRevisionAsync(OriginalUrlSlug, CancellationToken);
            return Redirect(ArticleUrlHelper.BuildArticlePath(PreviousRevision));
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound && Ex.Message == "Article not found.")
        {
             return NotFound("Article not found.");
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
        {
             ErrorMessage = Ex.Message;
             return await OnGetAsync();
        }
    }

    /// <summary>
    /// Handles the POST request to create or update an article.
    /// </summary>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A redirect or page result with errors.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(UrlSlug) || string.IsNullOrWhiteSpace(ArticleText))
        {
            ErrorMessage = "All required fields must be filled out.";
            return Page();
        }

        if (ArticleUrlHelper.IsRestrictedSlug(UrlSlug))
        {
            ErrorMessage = "The chosen URL slug conflicts with a reserved system path and cannot be used.";
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
            return await HandleEditAsync(CurrentUserId.Value, CancellationToken);
        }
        else
        {
            return await HandleCreateAsync(CurrentUserId.Value, CancellationToken);
        }
    }

    /// <summary>
    /// Handles the creation of a new article.
    /// </summary>
    /// <param name="CurrentUserId">The ID of the current user.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A redirect to the new article or a page with errors.</returns>
    private async Task<IActionResult> HandleCreateAsync(Guid CurrentUserId, CancellationToken CancellationToken)
    {
        // Check if article with this slug already exists
        ArticleRevisionsBySlugSpec Spec = new(UrlSlug!, IsCurrent: true);
        ArticleRevision? ExistingArticle = await Context.ArticleRevisions.AsNoTracking().WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);

        if (ExistingArticle is not null)
        {
            ErrorMessage = "An article with this URL slug already exists.";
            return Page();
        }
        Guid CanonicalArticleId = Guid.NewGuid();
        Guid? CanonicalFileId = null;
        StagedFileUpload? StagedUpload = null;

        if (UploadedFile is not null && UploadedFile.Length > 0)
        {
            if (!IsValidImageFile(UploadedFile, out string ValidationError))
            {
                ErrorMessage = ValidationError;
                return Page();
            }

            try
            {
                StagedUpload = await FileUploadStorageService.StageUploadedFileAsync(UploadedFile, SiteId, Guid.NewGuid(), CancellationToken);
                CanonicalFileId = StagedUpload.CanonicalFileId;
                SelectedType = ArticleType.File;
            }
            catch (Exception Ex)
            {
                FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
                ErrorMessage = $"Error uploading file: {Ex.Message}";
                return Page();
            }
        }

        try
        {
            IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

                ArticleRevision? ExistingArticleInsideTransaction = await Context.ArticleRevisions
                    .AsNoTracking()
                    .WithSpecification(Spec)
                    .FirstOrDefaultAsync(CT);

                if (ExistingArticleInsideTransaction is not null)
                {
                    FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
                    throw new InvalidOperationException("An article with this URL slug already exists.");
                }

                if (CanonicalFileId.HasValue &&
                    StagedUpload is not null)
                {
                    FileRevision NewFile = new()
                    {
                        CanonicalFileId = CanonicalFileId.Value,
                        Type = FileType.Image2D,
                        Filename = StagedUpload.OriginalFileName,
                        MimeType = StagedUpload.ContentType,
                        FileSizeBytes = StagedUpload.FileSizeBytes,
                        ImageWidthPixels = StagedUpload.ImageWidthPixels > 0 ? StagedUpload.ImageWidthPixels : null,
                        ImageHeightPixels = StagedUpload.ImageHeightPixels > 0 ? StagedUpload.ImageHeightPixels : null,
                        Source = null,
                        RevisionReason = "Initial upload",
                        SourceAndRevisionReasonCulture = Culture,
                        CreatedByUserId = CurrentUserId,
                        DateCreated = DateTimeOffset.UtcNow,
                        IsCurrent = true
                    };

                    Context.FileRevisions.Add(NewFile);
                }

                ArticleRevision NewArticle = new()
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

                using (WriteDurabilityScope.High())
                {
                    await Context.SaveChangesAsync(CT);
                }

                await Transaction.CommitAsync(CT);
            }, CancellationToken);

            if (StagedUpload is not null)
            {
                try
                {
                    FileUploadStorageService.FinalizeStagedUpload(StagedUpload);
                }
                catch (Exception Ex)
                {
                    ErrorMessage = $"Article was created, but the uploaded file is still waiting for filesystem recovery: {Ex.Message}";
                    return Page();
                }
            }

            return Redirect(ArticleUrlHelper.BuildArticlePath(UrlSlug!, SelectedType));
        }
        catch (InvalidOperationException Ex) when (Ex.Message.Contains("URL slug already exists"))
        {
            FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
            ErrorMessage = Ex.Message;
            return Page();
        }
        catch (Exception Ex)
        {
            FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
            ErrorMessage = $"Error creating article: {Ex.Message}";
            return Page();
        }
    }

    /// <summary>
    /// Handles the editing of an existing article.
    /// </summary>
    /// <param name="CurrentUserId">The ID of the current user.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A redirect to the updated article or a page with errors.</returns>
    private async Task<IActionResult> HandleEditAsync(Guid CurrentUserId, CancellationToken CancellationToken)
    {
        try
        {
            ArticleRevisionUpdateCommand Command = new(
                OriginalUrlSlug: OriginalUrlSlug,
                NewUrlSlug: UrlSlug!,
                SiteId: SiteId,
                Culture: Culture,
                Title: Title,
                DisplayTitle: DisplayTitle,
                Type: SelectedType,
                CanonicalFileId: null,
                Text: ArticleText,
                RevisionReason: "User edit",
                CreatedByUserId: CurrentUserId);

            await ArticleRevisionService.UpdateArticleAsync(Command, CancellationToken);
            return Redirect(ArticleUrlHelper.BuildArticlePath(UrlSlug!, SelectedType));
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
        {
            return NotFound("Article not found.");
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.Conflict)
        {
            ErrorMessage = Ex.Message;
            await PopulateCanRevertAsync(OriginalUrlSlug, CancellationToken);
            return Page();
        }
    }

    /// <summary>
    /// Populates whether the current article can be reverted.
    /// </summary>
    /// <param name="LookupUrlSlug">The URL slug used to find the current article.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    private async Task PopulateCanRevertAsync(string LookupUrlSlug, CancellationToken CancellationToken)
    {
        ArticleRevisionsBySlugSpec Spec = new(LookupUrlSlug, IsCurrent: true);
        ArticleRevision? CurrentArticle = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(Spec)
            .FirstOrDefaultAsync(CancellationToken);

        if (CurrentArticle is null)
        {
            CanRevert = false;
            return;
        }

        CanRevert = await Context.ArticleRevisions
            .Where(x => x.CanonicalArticleId == CurrentArticle.CanonicalArticleId && !x.IsCurrent)
            .AnyAsync(CancellationToken);
    }

    /// <summary>
    /// Gets the current user's ID from the UserManager.
    /// </summary>
    /// <returns>The current user's ID, or null if not found.</returns>
    private Guid? GetCurrentUserId()
    {
        string? UserIdString = UserManager.GetUserId(User);
        return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
    }

    /// <summary>
    /// Validates that an uploaded file is a valid image.
    /// </summary>
    /// <param name="UploadedFile">The file to validate.</param>
    /// <param name="ValidationError">The error message if validation fails.</param>
    /// <returns>True if the file is a valid image; otherwise, false.</returns>
    private static bool IsValidImageFile(IFormFile UploadedFile, out string ValidationError)
        => ImageValidationHelper.IsValidImageFile(UploadedFile, out ValidationError);
}

