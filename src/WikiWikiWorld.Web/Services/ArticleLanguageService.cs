using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Reads and edits the language groups that connect an article to its translations in the current site's other cultures.
/// </summary>
/// <remarks>
/// A group is a set of <see cref="ArticleCultureLink"/> rows sharing one group identifier. Each article belongs to
/// at most one active group (enforced by a unique partial index), and a group holds at most one article per culture
/// (enforced here, because the culture lives on <see cref="ArticleRevision"/>). Groups are transitive: every member
/// lists every other member. Removed links are soft-deleted with the acting user, so changes stay auditable.
/// Cross-culture reads bypass only the culture filter; the site and soft-delete filters stay in force.
/// Writes run inside a transaction, which SQLite opens immediately, so concurrent edits serialise.
/// </remarks>
/// <param name="Context">The request's database context, scoped to the current site.</param>
public sealed class ArticleLanguageService(WikiWikiWorldDbContext Context)
{
	/// <summary>
	/// Gets the other articles in the given article's language group.
	/// </summary>
	/// <param name="CanonicalArticleId">The article whose translations to list.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result is the group's other members with a current revision, unordered; empty when the
	/// article is not linked.
	/// </returns>
	public async Task<IReadOnlyList<ArticleLanguageEntry>> GetLanguagesAsync(Guid CanonicalArticleId, CancellationToken CancellationToken)
	{
		Guid? GroupId = await GetGroupIdAsync(CanonicalArticleId, CancellationToken);
		if (GroupId is null)
			return [];

		List<ArticleLanguageEntry> Members = await GetGroupMembersAsync(GroupId.Value, CancellationToken);
		return [.. Members.Where(Member => Member.CanonicalArticleId != CanonicalArticleId)];
	}

	/// <summary>
	/// Links the current article to an article in another culture, creating, joining or merging groups as needed.
	/// </summary>
	/// <param name="Current">The current revision of the article whose page the editor is on.</param>
	/// <param name="TargetCulture">The culture of the article to link.</param>
	/// <param name="TargetSlug">The slug of the article to link, with or without the <c>file:</c> prefix.</param>
	/// <param name="ConfirmMerge">Whether the editor has confirmed merging two groups.</param>
	/// <param name="KeepByCulture">
	/// For a confirmed merge, the article to keep for each clashing culture the editor had to choose.
	/// </param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result is <see cref="ArticleLanguageOutcome.Linked"/>, <see cref="ArticleLanguageOutcome.AlreadyLinked"/>,
	/// <see cref="ArticleLanguageOutcome.Refused"/>, or <see cref="ArticleLanguageOutcome.NeedsMergeConfirmation"/> when two
	/// groups would merge, or one article would displace a linked article of the same language, and that has not been
	/// confirmed with a choice for every clash.
	/// </returns>
	public async Task<ArticleLanguageResult> LinkAsync(
		ArticleRevision Current,
		string TargetCulture,
		string TargetSlug,
		bool ConfirmMerge,
		IReadOnlyDictionary<string, Guid> KeepByCulture,
		Guid UserId,
		CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Current);

		(ArticleLanguageEntry? Target, ArticleLanguageResult? Refusal) = await FindLinkTargetAsync(Current, TargetCulture, TargetSlug, CancellationToken);
		if (Target is null)
			return Refusal!;

		ArticleLanguageEntry CurrentEntry = ToEntry(Current);

		return await InTransactionAsync(async CT =>
		{
			Guid? CurrentGroupId = await GetGroupIdAsync(Current.CanonicalArticleId, CT);
			Guid? TargetGroupId = await GetGroupIdAsync(Target.CanonicalArticleId, CT);
			DateTimeOffset Now = DateTimeOffset.UtcNow;

			if (CurrentGroupId is null && TargetGroupId is null)
			{
				AddLinks(Current.SiteId, Guid.CreateVersion7(), [Current.CanonicalArticleId, Target.CanonicalArticleId], UserId, Now);
				await Context.SaveChangesAsync(CT);
				return Linked($"Linked to {DescribeLanguage(Target)}.");
			}

			if (CurrentGroupId == TargetGroupId)
			{
				return new ArticleLanguageResult
				{
					Outcome = ArticleLanguageOutcome.AlreadyLinked,
					Message = $"“{Target.Title}” is already linked as the {SiteCultureHelper.GetDisplayName(Target.Culture)} version."
				};
			}

			if (CurrentGroupId is null || TargetGroupId is null)
			{
				// Exactly one side is grouped: the other joins it directly when its language is free there. When the
				// language is taken, the lone article is treated as a group of one and goes through the merge
				// confirmation, which names the article it would displace.
				Guid GroupId = (CurrentGroupId ?? TargetGroupId)!.Value;
				ArticleLanguageEntry Joining = CurrentGroupId is null ? CurrentEntry : Target;
				List<ArticleLanguageEntry> Members = await GetGroupMembersAsync(GroupId, CT);
				if (!Members.Any(Member => Member.Culture.Equals(Joining.Culture, StringComparison.OrdinalIgnoreCase)))
				{
					AddLinks(Current.SiteId, GroupId, [Joining.CanonicalArticleId], UserId, Now);
					await Context.SaveChangesAsync(CT);
					return Linked($"Linked to {DescribeLanguage(Target)}.");
				}
			}

			return await MergeAsync(Current, CurrentEntry, CurrentGroupId, Target, TargetGroupId, ConfirmMerge, KeepByCulture, UserId, Now, CT);
		}, CancellationToken);
	}

	/// <summary>
	/// Replaces the article linked for one culture in the current article's group with another article in that culture.
	/// </summary>
	/// <param name="Current">The current revision of the article whose page the editor is on.</param>
	/// <param name="TargetCulture">The culture whose linked article to replace.</param>
	/// <param name="NewTargetSlug">The slug of the replacement article, with or without the <c>file:</c> prefix.</param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result is <see cref="ArticleLanguageOutcome.Changed"/>, <see cref="ArticleLanguageOutcome.AlreadyLinked"/>
	/// or <see cref="ArticleLanguageOutcome.Refused"/>. A replacement that already has other translations is refused so a
	/// change never merges groups silently.
	/// </returns>
	public async Task<ArticleLanguageResult> ChangeAsync(
		ArticleRevision Current,
		string TargetCulture,
		string NewTargetSlug,
		Guid UserId,
		CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Current);

		(ArticleLanguageEntry? Target, ArticleLanguageResult? Refusal) = await FindLinkTargetAsync(Current, TargetCulture, NewTargetSlug, CancellationToken);
		if (Target is null)
			return Refusal!;

		return await InTransactionAsync(async CT =>
		{
			Guid? GroupId = await GetGroupIdAsync(Current.CanonicalArticleId, CT);
			List<ArticleLanguageEntry> Members = GroupId is null ? [] : await GetGroupMembersAsync(GroupId.Value, CT);
			ArticleLanguageEntry? Existing = Members.FirstOrDefault(Member => Member.Culture.Equals(Target.Culture, StringComparison.OrdinalIgnoreCase));
			if (GroupId is null || Existing is null)
				return ArticleLanguageResult.Refused($"This article has no {SiteCultureHelper.GetDisplayName(Target.Culture)} link to change. Use Add a language instead.");

			if (Existing.CanonicalArticleId == Target.CanonicalArticleId)
			{
				return new ArticleLanguageResult
				{
					Outcome = ArticleLanguageOutcome.AlreadyLinked,
					Message = $"“{Target.Title}” is already the linked {SiteCultureHelper.GetDisplayName(Target.Culture)} version."
				};
			}

			Guid? TargetGroupId = await GetGroupIdAsync(Target.CanonicalArticleId, CT);
			if (TargetGroupId is not null)
			{
				List<ArticleLanguageEntry> TargetMembers = await GetGroupMembersAsync(TargetGroupId.Value, CT);
				string OtherLanguages = string.Join(", ", TargetMembers
					.Where(Member => Member.CanonicalArticleId != Target.CanonicalArticleId)
					.Select(Member => SiteCultureHelper.GetDisplayName(Member.Culture)));
				return ArticleLanguageResult.Refused(
					$"“{Target.Title}” is already linked to other languages ({OtherLanguages}). Remove those links first, or use Add a language to merge the two groups.");
			}

			DateTimeOffset Now = DateTimeOffset.UtcNow;
			await SoftDeleteLinksAsync(GroupId.Value, [Existing.CanonicalArticleId], UserId, Now, CT);
			AddLinks(Current.SiteId, GroupId.Value, [Target.CanonicalArticleId], UserId, Now);
			await Context.SaveChangesAsync(CT);

			return new ArticleLanguageResult
			{
				Outcome = ArticleLanguageOutcome.Changed,
				Message = $"The {SiteCultureHelper.GetDisplayName(Target.Culture)} version is now “{Target.Title}”."
			};
		}, CancellationToken);
	}

	/// <summary>
	/// Takes an article out of the current article's language group. When one article would be left on its own,
	/// the group is dissolved.
	/// </summary>
	/// <param name="Current">The current revision of the article whose page the editor is on.</param>
	/// <param name="CanonicalArticleIdToRemove">The member to remove; may be the current article itself.</param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is <see cref="ArticleLanguageOutcome.Removed"/> or <see cref="ArticleLanguageOutcome.Refused"/>.</returns>
	public async Task<ArticleLanguageResult> RemoveAsync(
		ArticleRevision Current,
		Guid CanonicalArticleIdToRemove,
		Guid UserId,
		CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Current);

		return await InTransactionAsync(async CT =>
		{
			Guid? GroupId = await GetGroupIdAsync(Current.CanonicalArticleId, CT);
			if (GroupId is null || !await Context.ArticleCultureLinks.AnyAsync(
				Link => Link.ArticleCultureLinkGroupId == GroupId && Link.CanonicalArticleId == CanonicalArticleIdToRemove, CT))
			{
				return ArticleLanguageResult.Refused("That article is no longer linked here. The page has been refreshed.");
			}

			DateTimeOffset Now = DateTimeOffset.UtcNow;
			await SoftDeleteLinksAsync(GroupId.Value, [CanonicalArticleIdToRemove], UserId, Now, CT);

			// A group of one links nothing; dissolve it rather than leave an orphan row.
			List<Guid> Remaining = await Context.ArticleCultureLinks
				.Where(Link => Link.ArticleCultureLinkGroupId == GroupId)
				.Select(Link => Link.CanonicalArticleId)
				.ToListAsync(CT);
			if (Remaining.Count == 1)
				await SoftDeleteLinksAsync(GroupId.Value, Remaining, UserId, Now, CT);

			return new ArticleLanguageResult
			{
				Outcome = ArticleLanguageOutcome.Removed,
				Message = CanonicalArticleIdToRemove == Current.CanonicalArticleId
					? "This article is no longer linked to any other language."
					: "Link removed."
			};
		}, CancellationToken);
	}

	/// <summary>
	/// Merges the target's group into the current article's group, or reports what a merge would do. A side that is
	/// not in any group takes part as a group of one, so linking a lone article into a group that already has its
	/// language follows the same confirmation as joining two groups.
	/// </summary>
	/// <param name="Current">The current page's revision.</param>
	/// <param name="CurrentEntry">The current page as a group entry.</param>
	/// <param name="CurrentGroupId">
	/// The current page's group, which survives the merge, or <see langword="null"/> when the current page is not linked.
	/// </param>
	/// <param name="Target">The article being linked.</param>
	/// <param name="TargetGroupId">
	/// The target's group, which is absorbed (or survives when the current page is not linked), or
	/// <see langword="null"/> when the target is not linked. At least one group must be given.
	/// </param>
	/// <param name="ConfirmMerge">Whether the editor has confirmed the merge.</param>
	/// <param name="KeepByCulture">The editor's choice of article for each clashing culture that needs one.</param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="Now">The timestamp for created and deleted rows.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result is <see cref="ArticleLanguageOutcome.Linked"/> after a merge, or
	/// <see cref="ArticleLanguageOutcome.NeedsMergeConfirmation"/> with the merged members and clashes when the merge is
	/// unconfirmed or a clash lacks a valid choice. Clashes are recomputed inside the transaction, so a confirmation made
	/// against groups that have since changed is asked again rather than applied.
	/// </returns>
	private async Task<ArticleLanguageResult> MergeAsync(
		ArticleRevision Current,
		ArticleLanguageEntry CurrentEntry,
		Guid? CurrentGroupId,
		ArticleLanguageEntry Target,
		Guid? TargetGroupId,
		bool ConfirmMerge,
		IReadOnlyDictionary<string, Guid> KeepByCulture,
		Guid UserId,
		DateTimeOffset Now,
		CancellationToken CancellationToken)
	{
		List<ArticleLanguageEntry> CurrentMembers = CurrentGroupId is null ? [CurrentEntry] : await GetGroupMembersAsync(CurrentGroupId.Value, CancellationToken);
		List<ArticleLanguageEntry> TargetMembers = TargetGroupId is null ? [Target] : await GetGroupMembersAsync(TargetGroupId.Value, CancellationToken);

		List<ArticleLanguageClash> Clashes = [];
		foreach (ArticleLanguageEntry CurrentMember in CurrentMembers)
		{
			ArticleLanguageEntry? OtherMember = TargetMembers.FirstOrDefault(Member => Member.Culture.Equals(CurrentMember.Culture, StringComparison.OrdinalIgnoreCase));
			if (OtherMember is null)
				continue;

			// The two articles being linked always stay: the editor has already said they belong together.
			Guid? ForcedKeep = CurrentMember.CanonicalArticleId == CurrentEntry.CanonicalArticleId ? CurrentEntry.CanonicalArticleId
				: OtherMember.CanonicalArticleId == Target.CanonicalArticleId ? Target.CanonicalArticleId
				: null;
			Clashes.Add(new ArticleLanguageClash(CurrentMember.Culture, CurrentMember, OtherMember, ForcedKeep));
		}

		List<ArticleLanguageEntry> Dropped = [];
		bool AllChosen = true;
		foreach (ArticleLanguageClash Clash in Clashes)
		{
			Guid? Keep = Clash.ForcedKeepArticleId
				?? (KeepByCulture.TryGetValue(Clash.Culture, out Guid Chosen) ? Chosen : null);
			if (Keep == Clash.CurrentGroupArticle.CanonicalArticleId)
				Dropped.Add(Clash.OtherGroupArticle);
			else if (Keep == Clash.OtherGroupArticle.CanonicalArticleId)
				Dropped.Add(Clash.CurrentGroupArticle);
			else
				AllChosen = false;
		}

		bool JoinsTwoGroups = CurrentGroupId is not null && TargetGroupId is not null;
		if (!ConfirmMerge || !AllChosen)
		{
			return new ArticleLanguageResult
			{
				Outcome = ArticleLanguageOutcome.NeedsMergeConfirmation,
				Message = ConfirmMerge
					? "Choose which article to keep for each language that both groups have."
					: JoinsTwoGroups
						? $"“{Target.Title}” is already linked to other languages. Linking will merge the two groups."
						: $"Linking “{Target.Title}” replaces an article that is already linked in its language.",
				JoinsTwoGroups = JoinsTwoGroups,
				MergeMembers = [.. CurrentMembers.Concat(TargetMembers).Where(Member => Member.CanonicalArticleId != Current.CanonicalArticleId)],
				Clashes = Clashes
			};
		}

		List<Guid> DroppedIds = [.. Dropped.Select(Entry => Entry.CanonicalArticleId)];
		if (CurrentGroupId is not null)
			await SoftDeleteLinksAsync(CurrentGroupId.Value, DroppedIds, UserId, Now, CancellationToken);
		if (TargetGroupId is not null)
			await SoftDeleteLinksAsync(TargetGroupId.Value, DroppedIds, UserId, Now, CancellationToken);

		if (CurrentGroupId is null)
		{
			AddLinks(Current.SiteId, TargetGroupId!.Value, [Current.CanonicalArticleId], UserId, Now);
		}
		else if (TargetGroupId is null)
		{
			AddLinks(Current.SiteId, CurrentGroupId.Value, [Target.CanonicalArticleId], UserId, Now);
		}
		else
		{
			// Move the absorbed group's remaining rows (including any whose article has no current revision) into the
			// surviving group as fresh rows, so the old rows record the move.
			List<Guid> Moving = await Context.ArticleCultureLinks
				.Where(Link => Link.ArticleCultureLinkGroupId == TargetGroupId)
				.Select(Link => Link.CanonicalArticleId)
				.ToListAsync(CancellationToken);
			await SoftDeleteLinksAsync(TargetGroupId.Value, Moving, UserId, Now, CancellationToken);
			AddLinks(Current.SiteId, CurrentGroupId.Value, Moving, UserId, Now);
		}

		await Context.SaveChangesAsync(CancellationToken);

		return Linked(Dropped.Count switch
		{
			0 => $"Linked to {DescribeLanguage(Target)}, joining {TargetMembers.Count} articles into this group.",
			1 => $"Linked to {DescribeLanguage(Target)}. “{Dropped[0].Title}” was unlinked.",
			_ => $"Linked to {DescribeLanguage(Target)}. The groups were merged and {Dropped.Count} duplicate-language articles were unlinked."
		});
	}

	/// <summary>
	/// Runs a language-group operation in one transaction under the context's retrying execution strategy, committing
	/// only when the operation reports a write.
	/// </summary>
	/// <param name="Operation">The operation; it stages and saves its own changes.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result is the operation's result. Refusals, no-ops and merge previews roll back, so a preview
	/// never leaves partial writes behind.
	/// </returns>
	/// <remarks>
	/// The change tracker is cleared before each attempt so a retry after a transient failure does not re-add rows
	/// staged by the failed attempt.
	/// </remarks>
	private async Task<ArticleLanguageResult> InTransactionAsync(
		Func<CancellationToken, Task<ArticleLanguageResult>> Operation,
		CancellationToken CancellationToken)
	{
		IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();
		return await Strategy.ExecuteAsync(async CT =>
		{
			Context.ChangeTracker.Clear();
			await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);
			ArticleLanguageResult Result = await Operation(CT);
			if (Result.Outcome is ArticleLanguageOutcome.Linked or ArticleLanguageOutcome.Changed or ArticleLanguageOutcome.Removed)
				await Transaction.CommitAsync(CT);
			return Result;
		}, CancellationToken);
	}

	/// <summary>
	/// Resolves and validates the article an editor asked to link.
	/// </summary>
	/// <param name="Current">The current page's revision.</param>
	/// <param name="TargetCulture">The requested culture.</param>
	/// <param name="TargetSlug">The requested slug, with or without the <c>file:</c> prefix.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>
	/// A task whose result holds the target, or a refusal when the culture is the current one, no current article has
	/// that slug in that culture, or the target is a different <see cref="ArticleType"/>.
	/// </returns>
	private async Task<(ArticleLanguageEntry? Target, ArticleLanguageResult? Refusal)> FindLinkTargetAsync(
		ArticleRevision Current,
		string TargetCulture,
		string TargetSlug,
		CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(TargetCulture) || string.IsNullOrWhiteSpace(TargetSlug))
			return (null, ArticleLanguageResult.Refused("Choose a language and enter the article's address."));

		string LanguageName = SiteCultureHelper.GetDisplayName(TargetCulture);
		if (TargetCulture.Equals(Current.Culture, StringComparison.OrdinalIgnoreCase))
			return (null, ArticleLanguageResult.Refused($"This article is already the {LanguageName} version; choose another language."));

		string LookupSlug = ArticleUrlHelper.NormalizeLookupSlug(TargetSlug.Trim());
		ArticleLanguageEntry? Target = await Context.ArticleRevisions
			.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.CultureFilter])
			.Where(Revision => Revision.Culture == TargetCulture && Revision.UrlSlug == LookupSlug && Revision.IsCurrent)
			.Select(Revision => new ArticleLanguageEntry(Revision.CanonicalArticleId, Revision.Culture, Revision.Title, Revision.UrlSlug, Revision.Type))
			.FirstOrDefaultAsync(CancellationToken);

		if (Target is null)
			return (null, ArticleLanguageResult.Refused($"There is no {LanguageName} article at “{LookupSlug}”."));

		if (Target.Type != Current.Type)
			return (null, ArticleLanguageResult.Refused($"“{Target.Title}” is {DescribeType(Target.Type)} page, but this is {DescribeType(Current.Type)} page. Only pages of the same kind can be linked."));

		return (Target, null);
	}

	/// <summary>
	/// Gets the active group an article belongs to.
	/// </summary>
	/// <param name="CanonicalArticleId">The article.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is the group identifier, or <see langword="null"/> when the article is not linked.</returns>
	private async Task<Guid?> GetGroupIdAsync(Guid CanonicalArticleId, CancellationToken CancellationToken)
	{
		return await Context.ArticleCultureLinks
			.Where(Link => Link.CanonicalArticleId == CanonicalArticleId)
			.Select(Link => (Guid?)Link.ArticleCultureLinkGroupId)
			.FirstOrDefaultAsync(CancellationToken);
	}

	/// <summary>
	/// Gets every member of a group that has a current revision, in any culture of the current site.
	/// </summary>
	/// <param name="GroupId">The group.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is the members, including the article the caller started from.</returns>
	private async Task<List<ArticleLanguageEntry>> GetGroupMembersAsync(Guid GroupId, CancellationToken CancellationToken)
	{
		return await Context.ArticleCultureLinks
			.Where(Link => Link.ArticleCultureLinkGroupId == GroupId)
			.Join(
				Context.ArticleRevisions.IgnoreQueryFilters([WikiWikiWorldDbContext.CultureFilter]).Where(Revision => Revision.IsCurrent),
				Link => Link.CanonicalArticleId,
				Revision => Revision.CanonicalArticleId,
				(Link, Revision) => new ArticleLanguageEntry(Revision.CanonicalArticleId, Revision.Culture, Revision.Title, Revision.UrlSlug, Revision.Type))
			.ToListAsync(CancellationToken);
	}

	/// <summary>
	/// Soft-deletes the active rows of the given articles in one group, recording who removed them.
	/// </summary>
	/// <param name="GroupId">The group the rows belong to.</param>
	/// <param name="CanonicalArticleIds">The articles whose rows to remove; may be empty.</param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="Now">The deletion timestamp.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task that completes once the rows are updated; this bypasses the change tracker.</returns>
	private async Task SoftDeleteLinksAsync(Guid GroupId, IReadOnlyCollection<Guid> CanonicalArticleIds, Guid UserId, DateTimeOffset Now, CancellationToken CancellationToken)
	{
		if (CanonicalArticleIds.Count == 0)
			return;

		await Context.ArticleCultureLinks
			.Where(Link => Link.ArticleCultureLinkGroupId == GroupId && CanonicalArticleIds.Contains(Link.CanonicalArticleId))
			.ExecuteUpdateAsync(Setters => Setters
				.SetProperty(Link => Link.DateDeleted, Now)
				.SetProperty(Link => Link.DeletedByUserId, UserId), CancellationToken);
	}

	/// <summary>
	/// Stages new active rows placing the given articles in a group; the caller saves them.
	/// </summary>
	/// <param name="SiteId">The current site.</param>
	/// <param name="GroupId">The group to join.</param>
	/// <param name="CanonicalArticleIds">The articles to add.</param>
	/// <param name="UserId">The acting user.</param>
	/// <param name="Now">The creation timestamp.</param>
	private void AddLinks(int SiteId, Guid GroupId, IEnumerable<Guid> CanonicalArticleIds, Guid UserId, DateTimeOffset Now)
	{
		Context.ArticleCultureLinks.AddRange(CanonicalArticleIds.Select(CanonicalArticleId => new ArticleCultureLink
		{
			SiteId = SiteId,
			CanonicalArticleId = CanonicalArticleId,
			ArticleCultureLinkGroupId = GroupId,
			CreatedByUserId = UserId,
			DateCreated = Now
		}));
	}

	/// <summary>
	/// Converts a revision to a group entry.
	/// </summary>
	/// <param name="Revision">The revision.</param>
	/// <returns>The entry describing the revision's article.</returns>
	private static ArticleLanguageEntry ToEntry(ArticleRevision Revision) =>
		new(Revision.CanonicalArticleId, Revision.Culture, Revision.Title, Revision.UrlSlug, Revision.Type);

	/// <summary>
	/// Creates a successful link result.
	/// </summary>
	/// <param name="Message">The confirmation shown to the editor.</param>
	/// <returns>A <see cref="ArticleLanguageOutcome.Linked"/> result.</returns>
	private static ArticleLanguageResult Linked(string Message) => new() { Outcome = ArticleLanguageOutcome.Linked, Message = Message };

	/// <summary>
	/// Describes an article for a confirmation message, naming its language and title.
	/// </summary>
	/// <param name="Entry">The article.</param>
	/// <returns>Text such as <c>the 日本語 article “GQ Japan”</c>.</returns>
	private static string DescribeLanguage(ArticleLanguageEntry Entry) =>
		$"the {SiteCultureHelper.GetDisplayName(Entry.Culture)} article “{Entry.Title}”";

	/// <summary>
	/// Gets a lowercase reader-facing name for an article type, with its indefinite article.
	/// </summary>
	/// <param name="Type">The article type.</param>
	/// <returns>The type's name in lowercase after "a" or "an", such as <c>a file</c> or <c>an article</c>.</returns>
	private static string DescribeType(ArticleType Type)
	{
		string Name = Type.ToString().ToLowerInvariant();
		return "aeiou".Contains(Name[0]) ? $"an {Name}" : $"a {Name}";
	}
}
