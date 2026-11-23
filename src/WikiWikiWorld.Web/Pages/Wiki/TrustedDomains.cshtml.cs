using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Wiki;

/// <summary>
/// The members' page for trusted link domains (<c>/wiki:trusted-domains</c>): lists the domains whose links are followed,
/// lets any signed-in member propose another with a reason, and shows the member their own proposals and outcomes.
/// </summary>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="TrustedDomains">Proposes and lists trusted domains.</param>
/// <param name="Permissions">Decides whether to point the member at the review queue.</param>
[Authorize]
public sealed class TrustedDomainsModel(
	SiteResolverService SiteResolverService,
	TrustedLinkDomainService TrustedDomains,
	PermissionService Permissions) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the domain or address the member typed.
	/// </summary>
	[BindProperty]
	public string InputDomain { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets why the member thinks the domain should be trusted.
	/// </summary>
	[BindProperty]
	public string InputReason { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the confirmation shown after a successful proposal (kept across the redirect).
	/// </summary>
	[TempData]
	public string? SuccessMessage { get; set; }

	/// <summary>
	/// Gets why the last proposal was refused, if it was.
	/// </summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>
	/// Gets the trusted domains, alphabetically.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> Trusted { get; private set; } = [];

	/// <summary>
	/// Gets the signed-in member's proposals, newest first.
	/// </summary>
	public IReadOnlyList<TrustedLinkDomainView> MyProposals { get; private set; } = [];

	/// <summary>
	/// Gets a value indicating whether the member may review proposals, so the page links to the queue.
	/// </summary>
	public bool CanReview { get; private set; }

	/// <summary>
	/// Gets how many proposals are waiting, shown to reviewers only.
	/// </summary>
	public int PendingCount { get; private set; }

	/// <summary>
	/// Loads the lists.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The page.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		await LoadAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Records a proposal, then redirects so a refresh does not resubmit it; redisplays the form with the reason on refusal.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A redirect back to the page, or the page with an error.</returns>
	public async Task<IActionResult> OnPostProposeAsync(CancellationToken CancellationToken)
	{
		Guid? UserId = PermissionService.GetUserId(User);
		if (UserId is null) return Challenge();

		TrustedDomainOutcome Outcome = await TrustedDomains.ProposeAsync(InputDomain, InputReason, UserId.Value, CancellationToken);
		switch (Outcome)
		{
			case TrustedDomainChanged Changed:
				SuccessMessage = SharedResource.Format("Thanks. {0} has been sent to the administrators for review.", Changed.Domain);
				return RedirectToPage();
			case TrustedDomainRefused Refused:
				ErrorMessage = Refused.Message;
				break;
			case null:
				throw new InvalidOperationException("ProposeAsync returned no outcome.");
		}

		await LoadAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Loads the trusted list, the member's proposals, and the reviewer link state.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>A task that completes when the lists are loaded.</returns>
	private async Task LoadAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		Trusted = await TrustedDomains.GetTrustedAsync(CancellationToken);
		if (PermissionService.GetUserId(User) is Guid UserId)
		{
			MyProposals = await TrustedDomains.GetProposalsByAsync(UserId, CancellationToken);
		}

		CanReview = await Permissions.HasPermissionAsync(User, Permission.ReviewTrustedLinkDomains, CancellationToken);
		if (CanReview)
		{
			PendingCount = await TrustedDomains.CountPendingAsync(CancellationToken);
		}
	}
}
