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
	/// Gets the proposals waiting for review, oldest first.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> Pending { get; private set; } = [];

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
		Pending = await TrustedDomains.GetPendingAsync(CancellationToken);
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

		return RedirectToPage();
	}
}
