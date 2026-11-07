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

    /// <summary>
    /// Gets or sets the requested or submitted slug, including a prefilled slug for a new user page.
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

	/// <summary>Gets whether an existing revision was loaded or submitted for editing; a prefilled creation slug alone is not an edit.</summary>
	public bool IsEditMode => !string.IsNullOrWhiteSpace(OriginalUrlSlug);

    /// <summary>
    /// Gets a value indicating whether the current article can be reverted to a previous revision.
    /// </summary>
    public bool CanRevert { get; private set; }

    /// <summary>
    /// Gets the existing file revision for this article if it has an associated file.
    /// </summary>
    public FileRevision? ExistingFileRevision { get; private set; }

    /// <summary>
    /// Gets the public image URL for the existing file revision, if available.
    /// </summary>
    public string? ExistingFileImageUrl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this article has an existing file associated with it.
    /// </summary>
    public bool HasExistingFile => ExistingFileRevision is not null;

	/// <summary>Loads an existing article for editing or offers creation of the authenticated owner's missing user page.</summary>
	/// <param name="CancellationToken">Cancels article lookup and edit-state loading.</param>
	/// <returns>The editor, a canonical redirect, or an authentication/ownership/missing-article response.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken = default)
	{
		if (User.Identity?.IsAuthenticated != true) return Challenge();
		OriginalUrlSlug = string.Empty;
		if (string.IsNullOrWhiteSpace(UrlSlug)) return Page();

		string RequestedUrlSlug = UrlSlug;
		UrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
		ArticleRevisionsBySlugSpec Spec = new(UrlSlug, IsCurrent: true);
		ArticleRevision? CurrentArticle = await Context.ArticleRevisions.AsNoTracking().WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);
		if (CurrentArticle is null)
		{
			if (!UrlSlug.StartsWith('@')) return NotFound("Article not found.");
			User? CurrentUser = await UserManager.GetUserAsync(User);
			if (CurrentUser?.UserName is not string UserName) return Challenge();
			if (!string.Equals(UrlSlug[1..], UserName, StringComparison.OrdinalIgnoreCase)) return Forbid();

			UrlSlug = $"@{UserName}";
			ModelState.Remove(nameof(UrlSlug));
			Title = UrlSlug;
			DisplayTitle = $"User: {UserName}";
			SelectedType = ArticleType.User;
			return Page();
		}

		if (ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, CurrentArticle.Type))
			return RedirectPermanent($"{ArticleUrlHelper.BuildArticlePath(CurrentArticle)}/edit");

		OriginalUrlSlug = CurrentArticle.UrlSlug;
		// Model binding may have retained a file: prefix; the form edits the database's bare slug.
		ModelState.Remove(nameof(UrlSlug));
		UrlSlug = CurrentArticle.UrlSlug;
		Title = CurrentArticle.Title;
		DisplayTitle = CurrentArticle.DisplayTitle ?? string.Empty;
		ArticleText = CurrentArticle.Text;
		SelectedType = CurrentArticle.Type;
		await PopulateEditStateAsync(CurrentArticle.UrlSlug, CancellationToken);
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
             return await OnGetAsync(CancellationToken);
        }
    }

	/// <summary>Creates or edits the submitted article, retaining draft values and mode on validation failure.</summary>
	/// <param name="CancellationToken">Cancels the revision or file workflow.</param>
	/// <returns>A redirect after saving, the submitted form with errors, or an authentication/ownership refusal.</returns>
	/// <remarks>User-page requests force the User article type before validation; file attachments cannot change that type.</remarks>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		if (User.Identity?.IsAuthenticated != true) return Challenge();
		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null) return Challenge();

		if (UrlSlug?.StartsWith('@') == true)
		{
			if (!string.Equals(User.Identity?.Name, UrlSlug[1..], StringComparison.OrdinalIgnoreCase)) return Forbid();
			SelectedType = ArticleType.User;
			if (UploadedFile is { Length: > 0 })
			{
				ErrorMessage = "User home pages cannot have an attached file.";
				return Page();
			}
		}

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

		return IsEditMode
			? await HandleEditAsync(CurrentUserId.Value, CancellationToken)
			: await HandleCreateAsync(CurrentUserId.Value, CancellationToken);
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
        FileRevision? CreatedFileRevision = null;

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
                await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
                ErrorMessage = $"Error uploading file: {Ex.Message}";
                return Page();
            }
        }

        try
        {
            IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

                ArticleRevision? ExistingArticleInsideTransaction = await Context.ArticleRevisions
                    .AsNoTracking()
                    .WithSpecification(Spec)
                    .FirstOrDefaultAsync(CT);

                if (ExistingArticleInsideTransaction is not null)
                {
                    await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
                    throw new InvalidOperationException("An article with this URL slug already exists.");
                }

                if (CanonicalFileId.HasValue &&
                    StagedUpload is not null)
                {
                    FileRevision NewFile = new()
                    {
                        CanonicalFileId = CanonicalFileId.Value,
                        StagingUploadId = StagedUpload.StagingUploadId,
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

                    // Captured so the file can be finalized at its per-revision path
                    // after SaveChanges assigns the revision id.
                    CreatedFileRevision = NewFile;
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

                await Context.SaveChangesAsync(CT);

                await Transaction.CommitAsync(CT);
            }, CancellationToken);

            if (StagedUpload is not null && CreatedFileRevision is not null)
            {
                try
                {
                    FileUploadStorageService.FinalizeStagedUpload(StagedUpload, SiteId, CreatedFileRevision.Id);
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
            await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
            ErrorMessage = Ex.Message;
            return Page();
        }
        catch (Exception Ex)
        {
            await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
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
        if (UploadedFile is null || UploadedFile.Length == 0)
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
                await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
                return Page();
            }
            catch (Exception Ex)
            {
                ErrorMessage = $"Error updating article: {Ex.Message}";
                await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
                return Page();
            }
        }

        if (!IsValidImageFile(UploadedFile, out string ValidationError))
        {
            ErrorMessage = ValidationError;
            await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
            return Page();
        }

        ArticleRevisionsBySlugSpec Spec = new(OriginalUrlSlug, IsCurrent: true);
        ArticleRevision? CurrentArticle = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(Spec)
            .FirstOrDefaultAsync(CancellationToken);

        if (CurrentArticle is null)
        {
            return NotFound("Article not found.");
        }

        Guid TargetCanonicalFileId = CurrentArticle.CanonicalFileId ?? Guid.NewGuid();
        StagedFileUpload? StagedUpload = null;
        FileRevision? CreatedFileRevision = null;

        try
        {
            StagedUpload = await FileUploadStorageService.StageUploadedFileAsync(UploadedFile, SiteId, TargetCanonicalFileId, CancellationToken);
            SelectedType = ArticleType.File;
        }
        catch (Exception Ex)
        {
            await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
            ErrorMessage = $"Error uploading file: {Ex.Message}";
            await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
            return Page();
        }

        try
        {
            IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

                ArticleRevisionsBySlugSpec InsideSpec = new(OriginalUrlSlug, IsCurrent: true);
                ArticleRevision? ArticleToUpdate = await Context.ArticleRevisions
                    .WithSpecification(InsideSpec)
                    .FirstOrDefaultAsync(CT);

                if (ArticleToUpdate is null)
                {
                    await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
                    throw new InvalidOperationException("Article not found.");
                }

                if (!OriginalUrlSlug.Equals(UrlSlug, StringComparison.OrdinalIgnoreCase))
                {
                    ArticleRevisionsBySlugSpec ConflictSpec = new(UrlSlug!, IsCurrent: true);
                    ArticleRevision? ConflictArticle = await Context.ArticleRevisions
                        .AsNoTracking()
                        .WithSpecification(ConflictSpec)
                        .FirstOrDefaultAsync(CT);

                    if (ConflictArticle is not null)
                    {
                        await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
                        throw new InvalidOperationException("An article with this URL slug already exists.");
                    }
                }

                FileRevision? CurrentFile = await Context.FileRevisions
                    .Where(f => f.CanonicalFileId == TargetCanonicalFileId && f.IsCurrent == true)
                    .FirstOrDefaultAsync(CT);

                string? PreservedSource = CurrentFile?.Source;

                if (CurrentFile is not null)
                {
                    CurrentFile.IsCurrent = false;
                    Context.FileRevisions.Update(CurrentFile);
                }

                FileRevision NewFile = new()
                {
                    CanonicalFileId = TargetCanonicalFileId,
                    StagingUploadId = StagedUpload.StagingUploadId,
                    Type = FileType.Image2D,
                    Filename = StagedUpload.OriginalFileName,
                    MimeType = StagedUpload.ContentType,
                    FileSizeBytes = StagedUpload.FileSizeBytes,
                    ImageWidthPixels = StagedUpload.ImageWidthPixels > 0 ? StagedUpload.ImageWidthPixels : null,
                    ImageHeightPixels = StagedUpload.ImageHeightPixels > 0 ? StagedUpload.ImageHeightPixels : null,
                    Source = PreservedSource,
                    RevisionReason = "Uploaded new file version",
                    SourceAndRevisionReasonCulture = Culture,
                    CreatedByUserId = CurrentUserId,
                    DateCreated = DateTimeOffset.UtcNow,
                    IsCurrent = true
                };

                Context.FileRevisions.Add(NewFile);
                CreatedFileRevision = NewFile;

                ArticleToUpdate.IsCurrent = false;
                Context.ArticleRevisions.Update(ArticleToUpdate);

                ArticleRevision NewArticleRevision = new()
                {
                    CanonicalArticleId = ArticleToUpdate.CanonicalArticleId,
                    SiteId = SiteId,
                    Culture = Culture,
                    Title = Title,
                    DisplayTitle = DisplayTitle,
                    UrlSlug = UrlSlug!,
                    Type = SelectedType,
                    CanonicalFileId = TargetCanonicalFileId,
                    Text = ArticleText,
                    RevisionReason = "Updated image file and article",
                    CreatedByUserId = CurrentUserId,
                    DateCreated = DateTimeOffset.UtcNow,
                    IsCurrent = true
                };

                Context.ArticleRevisions.Add(NewArticleRevision);

                await Context.SaveChangesAsync(CT);
                await Transaction.CommitAsync(CT);
            }, CancellationToken);

            if (StagedUpload is not null && CreatedFileRevision is not null)
            {
                try
                {
                    FileUploadStorageService.FinalizeStagedUpload(StagedUpload, SiteId, CreatedFileRevision.Id);
                }
                catch (Exception Ex)
                {
                    ErrorMessage = $"Image revision was created, but the uploaded file is awaiting filesystem recovery: {Ex.Message}";
                    await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
                    return Page();
                }
            }

            return Redirect(ArticleUrlHelper.BuildArticlePath(UrlSlug!, SelectedType));
        }
        catch (InvalidOperationException Ex) when (Ex.Message.Contains("URL slug already exists"))
        {
            await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
            ErrorMessage = Ex.Message;
            await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
            return Page();
        }
        catch (Exception Ex)
        {
            await FileUploadStorageService.CleanupUncommittedUploadAsync(StagedUpload, CancellationToken);
            ErrorMessage = $"Error updating article: {Ex.Message}";
            await PopulateEditStateAsync(OriginalUrlSlug, CancellationToken);
            return Page();
        }
    }

    /// <summary>
    /// Populates state needed when rendering the edit form, such as revert availability and existing file details.
    /// </summary>
    /// <param name="LookupUrlSlug">The URL slug used to find the current article.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    private async Task PopulateEditStateAsync(string LookupUrlSlug, CancellationToken CancellationToken)
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

        if (CurrentArticle.CanonicalFileId.HasValue)
        {
            ExistingFileRevision = await Context.FileRevisions
                .AsNoTracking()
                .Where(f => f.CanonicalFileId == CurrentArticle.CanonicalFileId.Value && f.IsCurrent == true)
                .FirstOrDefaultAsync(CancellationToken);

            if (ExistingFileRevision is not null)
            {
                ExistingFileImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, ExistingFileRevision.CanonicalFileId, ExistingFileRevision.Id, ExistingFileRevision.Filename);
            }
        }
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

