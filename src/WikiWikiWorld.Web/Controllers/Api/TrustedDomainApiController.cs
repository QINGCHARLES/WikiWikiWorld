using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// Lets API clients, such as the article-writing bot, see the trusted link domains and propose new ones. Proposals wait
/// for an administrator's review at <c>/Admin/TrustedDomains</c>, exactly like proposals made at <c>/wiki:trusted-domains</c>.
/// Trust is global across sites.
/// </summary>
/// <param name="TrustedDomains">Proposes and lists trusted domains.</param>
[Route("api/trusted-domains")]
[ApiController]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class TrustedDomainApiController(TrustedLinkDomainService TrustedDomains) : ControllerBase
{
	/// <summary>
	/// Lists the trusted domains and the caller's own proposals with their outcomes, so a client can check before proposing.
	/// </summary>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The trusted domains (subdomains of each are trusted too) and up to 50 of the caller's proposals, newest first.</returns>
	[HttpGet]
	[ProducesResponseType<TrustedDomainsResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	public async Task<ActionResult<TrustedDomainsResponse>> GetAsync(CancellationToken CancellationToken)
	{
		Guid? UserId = PermissionService.GetUserId(User);
		if (UserId is null) return Unauthorized();

		IReadOnlyList<TrustedLinkDomainView> Trusted = await TrustedDomains.GetTrustedAsync(CancellationToken);
		IReadOnlyList<TrustedLinkDomainView> Mine = await TrustedDomains.GetProposalsByAsync(UserId.Value, CancellationToken);
		return new TrustedDomainsResponse(
			[.. Trusted.Select(Row => Row.Domain)],
			[.. Mine.Select(Row => new TrustedDomainProposalInfo(
				Row.Domain,
				Row.Status.ToString(),
				Row.DateProposed,
				Row.Status == TrustedLinkDomainStatus.Revoked ? Row.RevocationNote : Row.ReviewNote))]);
	}

	/// <summary>
	/// Proposes a domain whose links should be followed. It stays untrusted until an administrator confirms it.
	/// </summary>
	/// <param name="Request">The domain (a bare domain or any address on the site) and the reason, such as whose official site it is.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>200 with the normalized domain; 400 when the domain or reason is invalid; 409 when the domain is already trusted or already waiting for review.</returns>
	[HttpPost]
	[Consumes("application/json")]
	[ProducesResponseType<TrustedDomainProposalResult>(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
	public async Task<IActionResult> ProposeAsync(TrustedDomainProposalRequest Request, CancellationToken CancellationToken)
	{
		Guid? UserId = PermissionService.GetUserId(User);
		if (UserId is null) return Unauthorized();

		// API responses are an English contract (PROJECT.md); the service's messages otherwise follow the site culture.
		// Resource keys are the English text, so the invariant culture yields them unchanged.
		CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
		return await TrustedDomains.ProposeAsync(Request.Domain, Request.Reason, UserId.Value, CancellationToken) switch
		{
			TrustedDomainChanged Changed => Ok(new TrustedDomainProposalResult(Changed.Domain, nameof(TrustedLinkDomainStatus.Pending))),
			TrustedDomainRefused { IsDuplicate: true } Duplicate => Conflict(Duplicate.Message),
			TrustedDomainRefused Refused => BadRequest(Refused.Message),
			null => throw new InvalidOperationException("ProposeAsync returned no outcome.")
		};
	}
}

/// <summary>The body of a trusted-domain proposal.</summary>
/// <param name="Domain">A bare domain such as <c>vogue.com</c>, or any address on the site; it is reduced to the host without <c>www.</c>.</param>
/// <param name="Reason">Why the domain should be trusted, such as "Official site of Vogue magazine"; required, at most 1,000 characters.</param>
public sealed record TrustedDomainProposalRequest(string? Domain, string? Reason);

/// <summary>A proposal that was accepted for review.</summary>
/// <param name="Domain">The normalized domain that was proposed.</param>
/// <param name="Status">Always <c>Pending</c>: an administrator still has to confirm it.</param>
public sealed record TrustedDomainProposalResult(string Domain, string Status);

/// <summary>The trusted domains and the caller's proposals.</summary>
/// <param name="Trusted">Trusted domains, alphabetically; every subdomain of each is trusted too.</param>
/// <param name="MyProposals">The caller's proposals, newest first, up to 50.</param>
public sealed record TrustedDomainsResponse(IReadOnlyList<string> Trusted, IReadOnlyList<TrustedDomainProposalInfo> MyProposals);

/// <summary>One of the caller's proposals.</summary>
/// <param name="Domain">The proposed domain.</param>
/// <param name="Status"><c>Pending</c>, <c>Trusted</c>, <c>Denied</c> or <c>Revoked</c>.</param>
/// <param name="DateProposed">When it was proposed (UTC).</param>
/// <param name="AdministratorNote">The reviewer's note for a confirmed or denied proposal, or the reason trust was withdrawn; null when there is none.</param>
public sealed record TrustedDomainProposalInfo(string Domain, string Status, DateTimeOffset DateProposed, string? AdministratorNote);
