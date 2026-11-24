using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>The request succeeded for this normalized domain.</summary>
/// <param name="Domain">The normalized domain the change applied to.</param>
public sealed record TrustedDomainChanged(string Domain);

/// <summary>The request was refused; nothing changed.</summary>
/// <param name="Message">A translated explanation for the person who asked.</param>
/// <param name="IsDuplicate">True when the domain is already trusted (directly or through a parent) or already waiting
/// for review, so there is nothing to do; false when the request itself was wrong. The API maps these to 409 and 400.</param>
public sealed record TrustedDomainRefused(string Message, bool IsDuplicate = false);

/// <summary>The outcome of proposing or reviewing a trusted link domain.</summary>
public union TrustedDomainOutcome(TrustedDomainChanged, TrustedDomainRefused);

/// <summary>
/// One trusted-link-domain row for display, with account names in place of identifiers.
/// </summary>
/// <param name="Id">The row identifier, posted back by review buttons.</param>
/// <param name="Domain">The normalized domain.</param>
/// <param name="Status">Where the proposal stands.</param>
/// <param name="ProposalNote">The proposer's reason, plain text.</param>
/// <param name="ProposedBy">The proposer's username, or null for seeded rows and deleted accounts.</param>
/// <param name="DateProposed">When it was proposed (UTC).</param>
/// <param name="ReviewedBy">The administrator who confirmed or denied it, if any.</param>
/// <param name="DateReviewed">When it was confirmed or denied (UTC), if it has been.</param>
/// <param name="ReviewNote">The reviewer's note, plain text, if any.</param>
/// <param name="RevokedBy">The administrator who withdrew trust, if any.</param>
/// <param name="DateRevoked">When trust was withdrawn (UTC), if it was.</param>
/// <param name="RevocationNote">The reason trust was withdrawn, plain text, if any.</param>
public sealed record TrustedLinkDomainView(
	int Id,
	string Domain,
	TrustedLinkDomainStatus Status,
	string ProposalNote,
	string? ProposedBy,
	DateTimeOffset DateProposed,
	string? ReviewedBy,
	DateTimeOffset? DateReviewed,
	string? ReviewNote,
	string? RevokedBy,
	DateTimeOffset? DateRevoked,
	string? RevocationNote);

/// <summary>
/// Proposes, reviews and lists trusted link domains: any signed-in member proposes a domain with a reason, and an
/// administrator confirms, denies or later withdraws it. Confirming or withdrawing refreshes <see cref="TrustedLinkDomainList"/>.
/// </summary>
/// <remarks>
/// Callers enforce who may review (<c>Permission.ReviewTrustedLinkDomains</c>); this service records who acted. Review
/// changes are conditional updates on the expected status, so two administrators acting on the same row cannot both win.
/// </remarks>
/// <param name="Context">The request's database context; the table is global.</param>
/// <param name="TrustedList">The in-memory list the Markdown pipelines read.</param>
public sealed partial class TrustedLinkDomainService(WikiWikiWorldDbContext Context, TrustedLinkDomainList TrustedList)
{
	/// <summary>The longest proposal or review note accepted, in characters.</summary>
	public const int MaxNoteLength = 1000;

	/// <summary>The longest hostname DNS allows, in characters.</summary>
	private const int MaxDomainLength = 253;

	/// <summary>SQLite's extended result code for a UNIQUE constraint violation.</summary>
	private const int SqliteConstraintUniqueErrorCode = 2067;

	/// <summary>How many denied and withdrawn rows the history list shows.</summary>
	private const int HistoryLimit = 50;

	/// <summary>
	/// Matches a lowercase ASCII hostname of two or more labels, each 1–63 letters, digits or inner hyphens.
	/// </summary>
	[GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex HostnamePattern();

	/// <summary>
	/// Names a status for display in the current UI culture.
	/// </summary>
	/// <param name="Status">The stored status.</param>
	/// <returns>A short translated label, such as "Waiting for review".</returns>
	public static string StatusLabel(TrustedLinkDomainStatus Status) => Status switch
	{
		TrustedLinkDomainStatus.Pending => SharedResource.Text("Waiting for review"),
		TrustedLinkDomainStatus.Trusted => SharedResource.Text("Trusted"),
		TrustedLinkDomainStatus.Denied => SharedResource.Text("Denied"),
		TrustedLinkDomainStatus.Revoked => SharedResource.Text("Withdrawn"),
		_ => Status.ToString()
	};

	/// <summary>
	/// Turns what a person typed (a bare domain or a pasted address) into the stored form: lowercase ASCII host,
	/// IDN names in punycode, without scheme, port, path, trailing dot or a leading <c>www.</c>.
	/// </summary>
	/// <param name="Input">The typed text, such as <c>https://www.Vogue.com/fashion</c>.</param>
	/// <returns>The normalized domain, or a refusal explaining what is wrong (empty, an IP address, a bare top-level domain, or not a hostname).</returns>
	public static TrustedDomainOutcome NormalizeDomain(string? Input)
	{
		string Text = Input?.Trim() ?? string.Empty;
		if (Text.Length == 0)
		{
			return new TrustedDomainRefused(SharedResource.Text("Enter a domain, such as example.com."));
		}

		string Candidate = Text.Contains("://", StringComparison.Ordinal) ? Text : "https://" + Text;
		if (!Uri.TryCreate(Candidate, UriKind.Absolute, out Uri? Parsed)
			|| (Parsed.Scheme != Uri.UriSchemeHttp && Parsed.Scheme != Uri.UriSchemeHttps)
			|| Parsed.UserInfo.Length > 0)
		{
			return new TrustedDomainRefused(SharedResource.Text("That is not a domain name. Enter one such as example.com."));
		}

		if (Parsed.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
		{
			return new TrustedDomainRefused(SharedResource.Text("IP addresses cannot be trusted. Enter the site's domain name."));
		}

		string Host = Parsed.IdnHost.TrimEnd('.').ToLowerInvariant();
		if (Host.StartsWith("www.", StringComparison.Ordinal))
		{
			Host = Host[4..];
		}

		if (!Host.Contains('.'))
		{
			return new TrustedDomainRefused(SharedResource.Text("Enter a full domain, such as example.com or example.gov.uk, not just a top-level domain."));
		}

		if (Host.Length > MaxDomainLength || !HostnamePattern().IsMatch(Host))
		{
			return new TrustedDomainRefused(SharedResource.Text("That is not a domain name. Enter one such as example.com."));
		}

		return new TrustedDomainChanged(Host);
	}

	/// <summary>
	/// Records a member's proposal that links to a domain be followed. It stays untrusted until an administrator confirms it.
	/// </summary>
	/// <param name="DomainInput">What the member typed; normalized by <see cref="NormalizeDomain"/>.</param>
	/// <param name="Note">Why the domain should be trusted; required, at most <see cref="MaxNoteLength"/> characters.</param>
	/// <param name="ProposedByUserId">The proposing account.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The normalized domain, or a refusal when the input is invalid, the note is missing or too long, or the domain is already trusted (directly or through a parent domain) or waiting for review.</returns>
	public async Task<TrustedDomainOutcome> ProposeAsync(string? DomainInput, string? Note, Guid ProposedByUserId, CancellationToken CancellationToken)
	{
		TrustedDomainOutcome Normalized = NormalizeDomain(DomainInput);
		if (Normalized is not TrustedDomainChanged { Domain: string Domain })
		{
			return Normalized;
		}

		string TrimmedNote = Note?.Trim() ?? string.Empty;
		if (TrimmedNote.Length == 0)
		{
			return new TrustedDomainRefused(SharedResource.Text("Say why this domain should be trusted, such as whose official site it is."));
		}

		if (TrimmedNote.Length > MaxNoteLength)
		{
			return new TrustedDomainRefused(SharedResource.Format("Keep the reason under {0} characters.", MaxNoteLength));
		}

		string? CoveringDomain = TrustedList.FirstOrDefault(Trusted =>
			Domain.Equals(Trusted, StringComparison.Ordinal) || Domain.EndsWith("." + Trusted, StringComparison.Ordinal));
		if (CoveringDomain is not null)
		{
			return new TrustedDomainRefused(CoveringDomain == Domain
				? SharedResource.Format("{0} is already trusted.", Domain)
				: SharedResource.Format("{0} is already trusted, because {1} is.", Domain, CoveringDomain), IsDuplicate: true);
		}

		if (await Context.TrustedLinkDomains.AnyAsync(Row => Row.Domain == Domain && Row.Status == TrustedLinkDomainStatus.Pending, CancellationToken))
		{
			return new TrustedDomainRefused(SharedResource.Format("{0} has already been proposed and is waiting for review.", Domain), IsDuplicate: true);
		}

		Context.TrustedLinkDomains.Add(new TrustedLinkDomain
		{
			Domain = Domain,
			Status = TrustedLinkDomainStatus.Pending,
			ProposalNote = TrimmedNote,
			ProposedByUserId = ProposedByUserId,
			DateProposed = DateTimeOffset.UtcNow
		});

		try
		{
			await Context.SaveChangesAsync(CancellationToken);
		}
		catch (DbUpdateException Error) when (Error.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUniqueErrorCode })
		{
			// Someone else proposed (or an administrator confirmed) the same domain between the check and the insert.
			Context.ChangeTracker.Clear();
			return new TrustedDomainRefused(SharedResource.Format("{0} has already been proposed and is waiting for review.", Domain), IsDuplicate: true);
		}

		return new TrustedDomainChanged(Domain);
	}

	/// <summary>
	/// Confirms a pending proposal, so links to the domain and its subdomains are followed from the next page rendered.
	/// </summary>
	/// <param name="Id">The proposal row.</param>
	/// <param name="ReviewerUserId">The administrator confirming it.</param>
	/// <param name="ReviewNote">An optional note, at most <see cref="MaxNoteLength"/> characters.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The confirmed domain, or a refusal when the row is no longer pending or the note is too long.</returns>
	public Task<TrustedDomainOutcome> ConfirmAsync(int Id, Guid ReviewerUserId, string? ReviewNote, CancellationToken CancellationToken) =>
		ReviewAsync(Id, ReviewerUserId, ReviewNote, TrustedLinkDomainStatus.Trusted, CancellationToken);

	/// <summary>
	/// Turns down a pending proposal. The row is kept as history, and the domain can be proposed again.
	/// </summary>
	/// <param name="Id">The proposal row.</param>
	/// <param name="ReviewerUserId">The administrator denying it.</param>
	/// <param name="ReviewNote">An optional reason shown to the proposer, at most <see cref="MaxNoteLength"/> characters.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The denied domain, or a refusal when the row is no longer pending or the note is too long.</returns>
	public Task<TrustedDomainOutcome> DenyAsync(int Id, Guid ReviewerUserId, string? ReviewNote, CancellationToken CancellationToken) =>
		ReviewAsync(Id, ReviewerUserId, ReviewNote, TrustedLinkDomainStatus.Denied, CancellationToken);

	/// <summary>
	/// Withdraws trust from a domain, so its links get <c>nofollow</c> again from the next page rendered.
	/// </summary>
	/// <param name="Id">The trusted row.</param>
	/// <param name="RevokerUserId">The administrator withdrawing trust.</param>
	/// <param name="RevocationNote">An optional reason, at most <see cref="MaxNoteLength"/> characters.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The domain, or a refusal when the row is no longer trusted or the note is too long.</returns>
	public async Task<TrustedDomainOutcome> RevokeAsync(int Id, Guid RevokerUserId, string? RevocationNote, CancellationToken CancellationToken)
	{
		string? Note = string.IsNullOrWhiteSpace(RevocationNote) ? null : RevocationNote.Trim();
		if (Note?.Length > MaxNoteLength)
		{
			return new TrustedDomainRefused(SharedResource.Format("Keep the reason under {0} characters.", MaxNoteLength));
		}

		DateTimeOffset Now = DateTimeOffset.UtcNow;
		int Updated = await Context.TrustedLinkDomains
			.Where(Row => Row.Id == Id && Row.Status == TrustedLinkDomainStatus.Trusted)
			.ExecuteUpdateAsync(Setters => Setters
				.SetProperty(Row => Row.Status, TrustedLinkDomainStatus.Revoked)
				.SetProperty(Row => Row.RevokedByUserId, (Guid?)RevokerUserId)
				.SetProperty(Row => Row.DateRevoked, (DateTimeOffset?)Now)
				.SetProperty(Row => Row.RevocationNote, Note), CancellationToken);

		if (Updated == 0)
		{
			return new TrustedDomainRefused(SharedResource.Text("That domain is no longer trusted. The list has been refreshed."));
		}

		await TrustedList.ReloadAsync(Context, CancellationToken);
		return new TrustedDomainChanged(await GetDomainAsync(Id, CancellationToken));
	}

	/// <summary>
	/// Counts proposals waiting for an administrator, for the account-menu badge.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The number of pending proposals.</returns>
	public Task<int> CountPendingAsync(CancellationToken CancellationToken) =>
		Context.TrustedLinkDomains.CountAsync(Row => Row.Status == TrustedLinkDomainStatus.Pending, CancellationToken);

	/// <summary>
	/// Lists proposals waiting for review, oldest first.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The pending rows; empty when the queue is clear.</returns>
	public Task<IReadOnlyList<TrustedLinkDomainView>> GetPendingAsync(CancellationToken CancellationToken) =>
		LoadAsync(Context.TrustedLinkDomains
			.Where(Row => Row.Status == TrustedLinkDomainStatus.Pending)
			.OrderBy(Row => Row.DateProposed).ThenBy(Row => Row.Id), CancellationToken);

	/// <summary>
	/// Lists the trusted domains alphabetically.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The trusted rows.</returns>
	public Task<IReadOnlyList<TrustedLinkDomainView>> GetTrustedAsync(CancellationToken CancellationToken) =>
		LoadAsync(Context.TrustedLinkDomains
			.Where(Row => Row.Status == TrustedLinkDomainStatus.Trusted)
			.OrderBy(Row => Row.Domain), CancellationToken);

	/// <summary>
	/// Lists the most recent denied and withdrawn rows, newest first, for administrators.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>Up to 50 history rows.</returns>
	public Task<IReadOnlyList<TrustedLinkDomainView>> GetHistoryAsync(CancellationToken CancellationToken) =>
		LoadAsync(Context.TrustedLinkDomains
			.Where(Row => Row.Status == TrustedLinkDomainStatus.Denied || Row.Status == TrustedLinkDomainStatus.Revoked)
			.OrderByDescending(Row => Row.Status == TrustedLinkDomainStatus.Revoked ? Row.DateRevoked : Row.DateReviewed)
			.ThenByDescending(Row => Row.Id)
			.Take(HistoryLimit), CancellationToken);

	/// <summary>
	/// Lists one member's proposals, newest first, so they can see each outcome and any reviewer note.
	/// </summary>
	/// <param name="UserId">The proposing account.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>Up to 50 of the member's proposals.</returns>
	public Task<IReadOnlyList<TrustedLinkDomainView>> GetProposalsByAsync(Guid UserId, CancellationToken CancellationToken) =>
		LoadAsync(Context.TrustedLinkDomains
			.Where(Row => Row.ProposedByUserId == UserId)
			.OrderByDescending(Row => Row.DateProposed).ThenByDescending(Row => Row.Id)
			.Take(HistoryLimit), CancellationToken);

	/// <summary>
	/// Moves a pending row to <paramref name="Decision"/> with the reviewer's name and time, then refreshes the in-memory list
	/// when the domain became trusted.
	/// </summary>
	/// <param name="Id">The proposal row.</param>
	/// <param name="ReviewerUserId">The administrator deciding.</param>
	/// <param name="ReviewNote">An optional note, at most <see cref="MaxNoteLength"/> characters.</param>
	/// <param name="Decision">Trusted or Denied.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The domain, or a refusal when the row is no longer pending or the note is too long.</returns>
	private async Task<TrustedDomainOutcome> ReviewAsync(int Id, Guid ReviewerUserId, string? ReviewNote, TrustedLinkDomainStatus Decision, CancellationToken CancellationToken)
	{
		string? Note = string.IsNullOrWhiteSpace(ReviewNote) ? null : ReviewNote.Trim();
		if (Note?.Length > MaxNoteLength)
		{
			return new TrustedDomainRefused(SharedResource.Format("Keep the reason under {0} characters.", MaxNoteLength));
		}

		DateTimeOffset Now = DateTimeOffset.UtcNow;
		int Updated = await Context.TrustedLinkDomains
			.Where(Row => Row.Id == Id && Row.Status == TrustedLinkDomainStatus.Pending)
			.ExecuteUpdateAsync(Setters => Setters
				.SetProperty(Row => Row.Status, Decision)
				.SetProperty(Row => Row.ReviewedByUserId, (Guid?)ReviewerUserId)
				.SetProperty(Row => Row.DateReviewed, (DateTimeOffset?)Now)
				.SetProperty(Row => Row.ReviewNote, Note), CancellationToken);

		if (Updated == 0)
		{
			return new TrustedDomainRefused(SharedResource.Text("That proposal has already been reviewed. The list has been refreshed."));
		}

		if (Decision == TrustedLinkDomainStatus.Trusted)
		{
			await TrustedList.ReloadAsync(Context, CancellationToken);
		}

		return new TrustedDomainChanged(await GetDomainAsync(Id, CancellationToken));
	}

	/// <summary>
	/// Reads one row's domain after a set-based update, which does not load the entity.
	/// </summary>
	/// <param name="Id">The row identifier.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The domain.</returns>
	private Task<string> GetDomainAsync(int Id, CancellationToken CancellationToken) =>
		Context.TrustedLinkDomains.Where(Row => Row.Id == Id).Select(Row => Row.Domain).SingleAsync(CancellationToken);

	/// <summary>
	/// Materializes rows and replaces the proposer, reviewer and revoker identifiers with usernames in one extra query.
	/// </summary>
	/// <param name="Query">The filtered, ordered row query.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The display rows in query order.</returns>
	private async Task<IReadOnlyList<TrustedLinkDomainView>> LoadAsync(IQueryable<TrustedLinkDomain> Query, CancellationToken CancellationToken)
	{
		List<TrustedLinkDomain> Rows = await Query.AsNoTracking().ToListAsync(CancellationToken);
		if (Rows.Count == 0) return [];

		HashSet<Guid> UserIds = [.. Rows
			.SelectMany(Row => new[] { Row.ProposedByUserId, Row.ReviewedByUserId, Row.RevokedByUserId })
			.OfType<Guid>()];
		Dictionary<Guid, string> Names = UserIds.Count == 0 ? [] : await Context.Users
			.Where(User => UserIds.Contains(User.Id) && User.UserName != null)
			.ToDictionaryAsync(User => User.Id, User => User.UserName!, CancellationToken);

		string? NameOf(Guid? UserId) => UserId is Guid Id && Names.TryGetValue(Id, out string? Name) ? Name : null;

		return [.. Rows.Select(Row => new TrustedLinkDomainView(
			Row.Id, Row.Domain, Row.Status, Row.ProposalNote,
			NameOf(Row.ProposedByUserId), Row.DateProposed,
			NameOf(Row.ReviewedByUserId), Row.DateReviewed, Row.ReviewNote,
			NameOf(Row.RevokedByUserId), Row.DateRevoked, Row.RevocationNote))];
	}
}
