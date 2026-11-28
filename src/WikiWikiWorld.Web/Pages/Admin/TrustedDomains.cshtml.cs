using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>
/// The administrators' review queue for trusted link domains (<c>/Admin/TrustedDomains</c>): confirm or deny pending
/// proposals, withdraw trust from a domain, and see recent decisions. Each action applies to link rendering immediately.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="TrustedDomains">Reviews and lists trusted domains.</param>
[Authorize(Policy = nameof(Permission.ReviewTrustedLinkDomains))]
public sealed class TrustedDomainsModel(
	SiteResolverService SiteResolverService,
	TrustedLinkDomainService TrustedDomains) : BasePageModel(SiteResolverService)
{
	private const int PageSize = 50;

	/// <summary>
	/// Gets or sets the result of the last action (kept across the redirect).
	/// </summary>
	[TempData]
	public string? SuccessMessage { get; set; }

	/// <summary>
	/// Gets or sets why the last action was refused (kept across the redirect).
	/// </summary>
	[TempData]
	public string? ErrorMessage { get; set; }

	/// <summary>
	/// Gets the current page of proposals waiting for review, oldest first.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> Pending { get; private set; } = [];

	/// <summary>Gets or sets the current page of the pending queue.</summary>
	[BindProperty(SupportsGet = true)]
	public int PageNumber { get; set; } = 1;

	/// <summary>Gets the total number of pending proposals across all pages.</summary>
	public int PendingCount { get; private set; }

	/// <summary>Gets the number of pages in the pending queue.</summary>
	public int PageCount { get; private set; } = 1;

	/// <summary>
	/// Gets the trusted domains, alphabetically.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> Trusted { get; private set; } = [];

	/// <summary>
	/// Gets the most recent denied and withdrawn domains, newest first.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> History { get; private set; } = [];

	/// <summary>
	/// Loads the queue, the trusted list and recent decisions.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The page.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		PendingCount = await TrustedDomains.CountPendingAsync(CancellationToken);
		PageCount = Math.Max(1, (int)Math.Ceiling((double)PendingCount / PageSize));
		PageNumber = Math.Clamp(PageNumber, 1, PageCount);
		Pending = await TrustedDomains.GetPendingAsync((PageNumber - 1) * PageSize, PageSize, CancellationToken);
		Trusted = await TrustedDomains.GetTrustedAsync(CancellationToken);
		History = await TrustedDomains.GetHistoryAsync(CancellationToken);
		return Page();
	}

	// Success formats passed to ActAsync, listed for scripts/localization.py:
	// l10n: "{0} is now trusted. Its links are followed.", "{0} was denied.", "{0} is no longer trusted. Its links are nofollow again."

	/// <summary>
	/// Confirms a pending proposal, so links to the domain are followed from the next page rendered.
	/// </summary>
	/// <param name="Id">The proposal row.</param>
	/// <param name="Note">An optional note shown to the proposer.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the queue with the outcome.</returns>
	public Task<IActionResult> OnPostConfirmAsync(int Id, string? Note, CancellationToken CancellationToken) =>
		ActAsync((UserId, Token) => TrustedDomains.ConfirmAsync(Id, UserId, Note, Token), "{0} is now trusted. Its links are followed.", CancellationToken);

	/// <summary>Confirms selected pending proposals with no reviewer note and reports how many changed.</summary>
	/// <param name="SelectedIds">The identifiers posted by the queue checkboxes.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the pending queue, or a refusal for an invalid request.</returns>
	public async Task<IActionResult> OnPostConfirmSelectedAsync([FromForm] int[]? SelectedIds, CancellationToken CancellationToken)
	{
		Guid? UserId = PermissionService.GetUserId(User);
		if (UserId is null) return Challenge();
		if (!ModelState.IsValid || SelectedIds is { Length: > PageSize } || SelectedIds?.Any(Id => Id <= 0) == true)
		{
			return BadRequest();
		}

		if (SelectedIds is null or [])
		{
			ErrorMessage = SharedResource.Text("Select at least one proposal to confirm.");
		}
		else
		{
			int[] Ids = [.. SelectedIds.Distinct()];
			int Confirmed = await TrustedDomains.ConfirmManyAsync(Ids, UserId.Value, CancellationToken);
			if (Confirmed > 0)
			{
				SuccessMessage = SharedResource.Format("{0} of {1} selected proposals confirmed.", Confirmed, Ids.Length);
			}
			else
			{
				ErrorMessage = SharedResource.Text("The selected proposals are no longer waiting for review.");
			}
		}

		return RedirectToPage(new { pagenumber = PageNumber > 1 ? (int?)PageNumber : null });
	}

	/// <summary>
	/// Denies a pending proposal; the proposer sees the outcome and note on their page.
	/// </summary>
	/// <param name="Id">The proposal row.</param>
	/// <param name="Note">An optional reason shown to the proposer.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the queue with the outcome.</returns>
	public Task<IActionResult> OnPostDenyAsync(int Id, string? Note, CancellationToken CancellationToken) =>
		ActAsync((UserId, Token) => TrustedDomains.DenyAsync(Id, UserId, Note, Token), "{0} was denied.", CancellationToken);

	/// <summary>
	/// Withdraws trust from a domain, so its links are nofollow again from the next page rendered.
	/// </summary>
	/// <param name="Id">The trusted row.</param>
	/// <param name="Note">An optional reason, recorded with the change.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the queue with the outcome.</returns>
	public Task<IActionResult> OnPostRevokeAsync(int Id, string? Note, CancellationToken CancellationToken) =>
		ActAsync((UserId, Token) => TrustedDomains.RevokeAsync(Id, UserId, Note, Token), "{0} is no longer trusted. Its links are nofollow again.", CancellationToken);

	/// <summary>
	/// Runs one review action as the signed-in administrator, stores the outcome message, and redirects (post/redirect/get).
	/// </summary>
	/// <param name="Action">The service call, given the administrator's id and the cancellation token.</param>
	/// <param name="SuccessFormat">The English success message with <c>{0}</c> for the domain; translated here.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the queue, or a challenge when the account id cannot be read.</returns>
	private async Task<IActionResult> ActAsync(Func<Guid, CancellationToken, Task<TrustedDomainOutcome>> Action, string SuccessFormat, CancellationToken CancellationToken)
	{
		Guid? UserId = PermissionService.GetUserId(User);
		if (UserId is null) return Challenge();

		switch (await Action(UserId.Value, CancellationToken))
		{
			case TrustedDomainChanged Changed:
				SuccessMessage = SharedResource.Format(SuccessFormat, Changed.Domain);
				break;
			case TrustedDomainRefused Refused:
				ErrorMessage = Refused.Message;
				break;
			case null:
				throw new InvalidOperationException("The review action returned no outcome.");
		}

		return RedirectToPage(new { pagenumber = PageNumber > 1 ? (int?)PageNumber : null });
	}
}
