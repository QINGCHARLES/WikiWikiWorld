namespace WikiWikiWorld.Data.Models;

/// <summary>
/// Where a trusted-link-domain proposal stands. Stored as an integer; do not renumber.
/// </summary>
public enum TrustedLinkDomainStatus
{
	/// <summary>Proposed and waiting for an administrator's decision. Links still get <c>nofollow</c>.</summary>
	Pending = 0,

	/// <summary>Confirmed by an administrator. Links to the domain and its subdomains are followed.</summary>
	Trusted = 1,

	/// <summary>Turned down by an administrator. The row is kept as history; the domain may be proposed again.</summary>
	Denied = 2,

	/// <summary>Was trusted, then withdrawn by an administrator. Kept as history; the domain may be proposed again.</summary>
	Revoked = 3
}

/// <summary>
/// One proposal that external links to a domain be followed (no <c>rel="nofollow"</c>), with its review outcome.
/// </summary>
/// <remarks>
/// Trust is global across every site and culture. Each proposal is its own row, so a denied or revoked domain keeps
/// its history when proposed again; a partial unique index allows only one pending or trusted row per domain.
/// Trusting <c>example.com</c> also trusts every subdomain of it (see <c>LinkDomainHelper.IsApprovedDomain</c>).
/// </remarks>
public sealed class TrustedLinkDomain
{
	/// <summary>
	/// Gets or sets the row identifier.
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Gets or sets the normalized host: lowercase ASCII (IDN names in punycode), no scheme, port, path or leading <c>www.</c>.
	/// </summary>
	public required string Domain { get; set; }

	/// <summary>
	/// Gets or sets where the proposal stands.
	/// </summary>
	public TrustedLinkDomainStatus Status { get; set; }

	/// <summary>
	/// Gets or sets the proposer's plain-text reason for trusting the domain, such as "official site of Vogue".
	/// </summary>
	public required string ProposalNote { get; set; }

	/// <summary>
	/// Gets or sets the account that proposed the domain, or null for domains seeded from the old configuration list.
	/// </summary>
	public Guid? ProposedByUserId { get; set; }

	/// <summary>
	/// Gets or sets when the domain was proposed (UTC).
	/// </summary>
	public DateTimeOffset DateProposed { get; set; }

	/// <summary>
	/// Gets or sets the administrator who confirmed or denied the proposal, or null while pending or when seeded.
	/// </summary>
	public Guid? ReviewedByUserId { get; set; }

	/// <summary>
	/// Gets or sets when the proposal was confirmed or denied (UTC), or null while pending.
	/// </summary>
	public DateTimeOffset? DateReviewed { get; set; }

	/// <summary>
	/// Gets or sets the reviewer's optional plain-text note, typically the reason for a denial.
	/// </summary>
	public string? ReviewNote { get; set; }

	/// <summary>
	/// Gets or sets the administrator who withdrew trust, or null unless <see cref="Status"/> is <see cref="TrustedLinkDomainStatus.Revoked"/>.
	/// </summary>
	public Guid? RevokedByUserId { get; set; }

	/// <summary>
	/// Gets or sets when trust was withdrawn (UTC), or null unless revoked.
	/// </summary>
	public DateTimeOffset? DateRevoked { get; set; }

	/// <summary>
	/// Gets or sets the administrator's optional plain-text reason for withdrawing trust.
	/// </summary>
	public string? RevocationNote { get; set; }
}
