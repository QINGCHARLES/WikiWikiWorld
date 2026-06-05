using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Pages.Wiki;

/// <summary>
/// Submission form letting authenticated users offer a remote download URL for a publication awaiting moderator ingestion.
/// </summary>
[Authorize]
public sealed class UploadModel(
	WikiWikiWorldDbContext Context,
	UserManager<User> UserManager,
	SiteResolverService SiteResolverService)
	: BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the URL slug of the article the user is submitting from, if any.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? ArticleSlug { get; set; }

	/// <summary>
	/// Gets or sets the remote download URL the submitter is offering.
	/// </summary>
	[BindProperty]
	public string DownloadUrl { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional transfer-service passphrase.
	/// </summary>
	[BindProperty]
	public string? SitePassword { get; set; }

	/// <summary>
	/// Gets or sets the optional archive passphrase.
	/// </summary>
	[BindProperty]
	public string? ArchivePassword { get; set; }

	/// <summary>
	/// Gets or sets the selected copyright-exemption row id.
	/// </summary>
	[BindProperty]
	public long CopyrightExemptionId { get; set; }

	/// <summary>
	/// Gets or sets the free-text notes the submitter provided.
	/// </summary>
	[BindProperty]
	public string? SubmitterNotes { get; set; }

	/// <summary>
	/// Gets the copyright exemptions available for selection.
	/// </summary>
	public List<PublicationUploadCopyrightExemption> Exemptions { get; private set; } = [];

	/// <summary>
	/// Gets the article the submission is associated with, if any.
	/// </summary>
	public ArticleRevision? AssociatedArticle { get; private set; }

	/// <summary>
	/// Gets the error message displayed when validation fails.
	/// </summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the last submission succeeded.
	/// </summary>
	public bool SubmissionSucceeded { get; private set; }

	/// <summary>
	/// Handles the GET request.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page result.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		await LoadFormContextAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Handles the POST submission.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page result.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		await LoadFormContextAsync(CancellationToken);

		Guid? CurrentUserId = GetCurrentUserId();
		if (CurrentUserId is null)
		{
			return Challenge();
		}

		string TrimmedUrl = DownloadUrl?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(TrimmedUrl)
			|| !Uri.TryCreate(TrimmedUrl, UriKind.Absolute, out Uri? ParsedUrl)
			|| (ParsedUrl.Scheme != Uri.UriSchemeHttp && ParsedUrl.Scheme != Uri.UriSchemeHttps))
		{
			ErrorMessage = "Please enter a valid http or https download URL.";
			return Page();
		}

		if (CopyrightExemptionId <= 0
			|| !Exemptions.Any(E => E.Id == CopyrightExemptionId))
		{
			ErrorMessage = "Please choose the copyright exemption that applies.";
			return Page();
		}

		PublicationUploadQueueStatus? SubmittedStatus = await Context.PublicationUploadQueueStatuses
			.AsNoTracking()
			.FirstOrDefaultAsync(S => S.Code == "SUBMITTED", CancellationToken);

		if (SubmittedStatus is null)
		{
			ErrorMessage = "The ingestion queue is not configured correctly. Please contact a site administrator.";
			return Page();
		}

		PublicationUploadQueue NewRow = new()
		{
			SiteId = SiteId,
			Culture = Culture,
			CanonicalArticleId = AssociatedArticle?.CanonicalArticleId,
			DownloadUrl = TrimmedUrl,
			SitePassword = NullIfBlank(SitePassword),
			ArchivePassword = NullIfBlank(ArchivePassword),
			CopyrightExemptionId = CopyrightExemptionId,
			StatusId = SubmittedStatus.Id,
			SubmitterNotes = NullIfBlank(SubmitterNotes),
			SubmittedByUserId = CurrentUserId.Value,
			DateCreated = DateTimeOffset.UtcNow,
		};

		Context.PublicationUploadQueue.Add(NewRow);
		await Context.SaveChangesAsync(CancellationToken);

		string Destination = string.IsNullOrWhiteSpace(ArticleSlug)
			? "/wiki:upload?submitted=1"
			: $"/wiki:upload?ArticleSlug={Uri.EscapeDataString(ArticleSlug!)}&submitted=1";
		return Redirect(Destination);
	}

	private async Task LoadFormContextAsync(CancellationToken CancellationToken)
	{
		Exemptions = await Context.PublicationUploadCopyrightExemptions
			.AsNoTracking()
			.OrderBy(E => E.Id)
			.ToListAsync(CancellationToken);

		if (!string.IsNullOrWhiteSpace(ArticleSlug))
		{
			string LookupSlug = ArticleUrlHelper.NormalizeLookupSlug(ArticleSlug);
			ArticleRevisionsBySlugSpec Spec = new(LookupSlug, IsCurrent: true);
			AssociatedArticle = await Context.ArticleRevisions
				.AsNoTracking()
				.WithSpecification(Spec)
				.FirstOrDefaultAsync(CancellationToken);
		}

		SubmissionSucceeded = Request.Query["submitted"] == "1";
	}

	private Guid? GetCurrentUserId()
	{
		string? UserIdString = UserManager.GetUserId(User);
		return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
	}

	private static string? NullIfBlank(string? Value)
		=> string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
}
