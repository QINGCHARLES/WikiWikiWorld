using System.Security.Claims;
using Markdig;
using Markdig.Syntax;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Loads the data behind the profile pages. All article and talk queries go through the context's site and culture
/// query filters, so totals describe the user's work on the site being viewed. Contribution figures leave out user
/// pages (<see cref="ArticleType.User"/>): every account gets one created automatically, and editing a user page is not
/// encyclopedia work, so a new member starts from zero.
/// </summary>
/// <param name="Context">The database context for the current request.</param>
/// <param name="UserManager">Resolves users, roles, and the signed-in viewer.</param>
/// <param name="PipelineFactory">Supplies the article Markdown pipeline used to read the profile article's tagline directives.</param>
public sealed class ProfileService(WikiWikiWorldDbContext Context, UserManager<User> UserManager, IMarkdownPipelineFactory PipelineFactory)
{
	/// <summary>
	/// The number of entries in the overview's activity feed.
	/// </summary>
	private const int RecentActivityCount = 6;

	/// <summary>
	/// The number of focus-area categories listed on the overview.
	/// </summary>
	private const int FocusAreaCount = 8;

	/// <summary>
	/// Loads the shared header for a profile, including the viewer's relationship to it.
	/// </summary>
	/// <param name="UserName">The requested user name; matching is case-insensitive.</param>
	/// <param name="Viewer">The principal making the request.</param>
	/// <param name="SiteId">The current site, used for the avatar path.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The header, or <see langword="null"/> when no active user has that name.</returns>
	public async Task<ProfileHeader?> GetHeaderAsync(string UserName, ClaimsPrincipal Viewer, int SiteId, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Viewer);
		if (string.IsNullOrWhiteSpace(UserName))
		{
			return null;
		}

		User? TargetUser = await UserManager.FindByNameAsync(UserName);
		if (TargetUser is null)
		{
			return null;
		}

		bool IsViewerAuthenticated = Viewer.Identity?.IsAuthenticated ?? false;
		bool IsOwner = IsViewerAuthenticated
			&& Guid.TryParse(UserManager.GetUserId(Viewer), out Guid ViewerId)
			&& ViewerId == TargetUser.Id;
		string StoredUserName = TargetUser.UserName ?? UserName;
		string ProfileArticleSlug = $"@{StoredUserName}";

		ProfileStats Stats = await GetStatsAsync(TargetUser.Id, CancellationToken);
		DateTimeOffset? LastActive = await GetLastActiveAsync(TargetUser.Id, CancellationToken);

		// Accounts registered before the join date was recorded hold the default value; their first revision is the
		// best available stand-in.
		DateTimeOffset? DateJoined = TargetUser.DateCreated > DateTimeOffset.UnixEpoch
			? TargetUser.DateCreated
			: await Context.ArticleRevisions
				.Where(r => r.CreatedByUserId == TargetUser.Id)
				.OrderBy(r => r.Id)
				.Select(r => (DateTimeOffset?)r.DateCreated)
				.FirstOrDefaultAsync(CancellationToken);
		string? ProfileArticleText = await Context.ArticleRevisions
			.Where(r => r.UrlSlug == ProfileArticleSlug && r.IsCurrent)
			.OrderByDescending(r => r.Id)
			.Select(r => r.Text)
			.FirstOrDefaultAsync(CancellationToken);
		bool IsProfileArticleWritten = ProfileArticleText is not null
			&& !string.Equals(ProfileArticleText.Trim(), ArticleRevisionService.GetDefaultUserArticleText(StoredUserName), StringComparison.Ordinal);
		(string? Tagline, string? Summary) = ReadProfileDirectives(IsProfileArticleWritten ? ProfileArticleText : null);
		IList<string> Roles = await UserManager.GetRolesAsync(TargetUser);
		int? BookmarkCount = IsOwner
			? await Context.Bookmarks.CountAsync(b => b.CreatedByUserId == TargetUser.Id, CancellationToken)
			: null;

		return new ProfileHeader
		{
			UserId = TargetUser.Id,
			UserName = StoredUserName,
			AvatarPath = TargetUser.ProfilePicGuid.HasValue
				? $"/sitefiles/{SiteId.ToString(CultureInfo.InvariantCulture)}/profilepics/{TargetUser.ProfilePicGuid.Value}.png"
				: null,
			DateJoined = DateJoined,
			LastActive = LastActive,
			IsOwner = IsOwner,
			IsViewerAuthenticated = IsViewerAuthenticated,
			HasProfileArticle = ProfileArticleText is not null,
			IsProfileArticleWritten = IsProfileArticleWritten,
			Tagline = Tagline,
			Summary = Summary,
			Roles = [.. Roles.Order(StringComparer.OrdinalIgnoreCase)],
			Stats = Stats,
			BookmarkCount = BookmarkCount,
			VolumeNumber = DateJoined is { } Joined ? GetWholeYearsBetween(Joined, DateTimeOffset.UtcNow) + 1 : 1,
			Rank = ProfileBadgeCatalog.GetRank(Stats.EditCount)
		};
	}

	/// <summary>
	/// Loads the overview tab: the activity calendar, focus areas, recent activity, badges, and the profile article.
	/// </summary>
	/// <param name="Header">The header already loaded for this request.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The overview data.</returns>
	public async Task<ProfileOverview> GetOverviewAsync(ProfileHeader Header, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Header);

		ProfileActivityCalendar Calendar = await GetCalendarAsync(Header.UserId, CancellationToken);
		IReadOnlyList<ProfileFocusArea> AllFocusAreas = await GetFocusAreasAsync(Header.UserId, CancellationToken);
		IReadOnlyList<ProfileActivityItem> RecentActivity = await GetRecentActivityAsync(Header.UserId, CancellationToken);

		string ProfileArticleSlug = $"@{Header.UserName}";
		ArticleRevision? ProfileArticle = Header.IsProfileArticleWritten
			? await Context.ArticleRevisions
				.AsNoTracking()
				.Where(r => r.UrlSlug == ProfileArticleSlug && r.IsCurrent)
				.OrderByDescending(r => r.Id)
				.FirstOrDefaultAsync(CancellationToken)
			: null;

		int MembershipDays = Header.DateJoined is { } Joined
			? (int)Math.Max(0, (DateTimeOffset.UtcNow - Joined).TotalDays)
			: 0;

		return new ProfileOverview
		{
			Calendar = Calendar,
			FocusAreas = [.. AllFocusAreas.Take(FocusAreaCount)],
			CategoryCount = AllFocusAreas.Count,
			RecentActivity = RecentActivity,
			Badges = ProfileBadgeCatalog.Evaluate(Header.Stats, MembershipDays, Calendar.LongestStreakDays, AllFocusAreas.Count),
			ProfileArticle = ProfileArticle
		};
	}

	/// <summary>
	/// Loads the last year of the user's edits as a calendar.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The calendar ending today (UTC).</returns>
	public async Task<ProfileActivityCalendar> GetCalendarAsync(Guid UserId, CancellationToken CancellationToken)
	{
		DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
		DateTimeOffset WindowStart = new(ProfileActivityCalendar.GetFirstDay(Today).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

		// Timestamps are stored as normalized UTC text, so this comparison is a lexicographic range scan.
		List<DateTimeOffset> Timestamps = await GetContributionRevisions(UserId)
			.Where(r => r.DateCreated >= WindowStart)
			.Select(r => r.DateCreated)
			.ToListAsync(CancellationToken);

		return ProfileActivityCalendar.Build(Timestamps, Today);
	}

	/// <summary>
	/// Loads one page of the user's revisions, newest first, marking which ones created their article.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="PageNumber">The one-based page number; values below 1 are treated as 1.</param>
	/// <param name="PageSize">The number of revisions per page.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The requested page, linking each entry to its revision snapshot, and whether another page follows.</returns>
	public async Task<ProfileContributionPage> GetContributionsAsync(Guid UserId, int PageNumber, int PageSize, CancellationToken CancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PageSize);
		PageNumber = Math.Max(1, PageNumber);

		IReadOnlyList<ProfileActivityItem> Items = await GetRevisionItemsAsync(UserId, (PageNumber - 1) * PageSize, PageSize + 1, LinkToRevision: true, CancellationToken);
		return new ProfileContributionPage([.. Items.Take(PageSize)], PageNumber, Items.Count > PageSize);
	}

	/// <summary>
	/// Loads a slice of the user's revisions, newest first, marking which ones created their article.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="Skip">The number of newer revisions to skip.</param>
	/// <param name="Take">The maximum number of revisions to return.</param>
	/// <param name="LinkToRevision"><see langword="true"/> to link each entry to its revision snapshot; <see langword="false"/> to link the live article.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The revisions as activity entries.</returns>
	private async Task<IReadOnlyList<ProfileActivityItem>> GetRevisionItemsAsync(Guid UserId, int Skip, int Take, bool LinkToRevision, CancellationToken CancellationToken)
	{
		IQueryable<int> FirstRevisionIds = GetFirstRevisionIdsQuery(UserId);
		var Rows = await GetContributionRevisions(UserId)
			.OrderByDescending(r => r.Id)
			.Skip(Skip)
			.Take(Take)
			.Select(r => new
			{
				r.Title,
				r.UrlSlug,
				r.Type,
				r.RevisionReason,
				r.DateCreated,
				IsCreation = FirstRevisionIds.Contains(r.Id)
			})
			.ToListAsync(CancellationToken);

		return [.. Rows.Select(r => new ProfileActivityItem(
			r.IsCreation ? ProfileActivityKind.Created : ProfileActivityKind.Edited,
			r.Title,
			LinkToRevision
				? ArticleUrlHelper.BuildRelativePath(r.UrlSlug, r.Type, $"revision/{r.DateCreated.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}")
				: ArticleUrlHelper.BuildArticlePath(r.UrlSlug, r.Type),
			string.IsNullOrWhiteSpace(r.RevisionReason) ? null : r.RevisionReason,
			r.Type,
			r.DateCreated))];
	}

	/// <summary>
	/// Counts the user's revisions, edited articles, started articles, and talk posts.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The contribution totals.</returns>
	private async Task<ProfileStats> GetStatsAsync(Guid UserId, CancellationToken CancellationToken)
	{
		IQueryable<ArticleRevision> UserRevisions = GetContributionRevisions(UserId);
		IQueryable<int> FirstRevisionIds = GetFirstRevisionIdsQuery(UserId);

		int EditCount = await UserRevisions.CountAsync(CancellationToken);
		int ArticlesEditedCount = await UserRevisions.Select(r => r.CanonicalArticleId).Distinct().CountAsync(CancellationToken);
		int ArticlesStartedCount = await UserRevisions.CountAsync(r => FirstRevisionIds.Contains(r.Id), CancellationToken);

		// Posts carry no site of their own; joining the subject applies its site filter.
		int TalkPostCount = await Context.ArticleTalkSubjectPosts
			.Where(p => p.CreatedByUserId == UserId)
			.Join(Context.ArticleTalkSubjects, p => p.ArticleTalkSubjectId, s => s.Id, (p, s) => p.Id)
			.CountAsync(CancellationToken);

		return new ProfileStats
		{
			EditCount = EditCount,
			ArticlesEditedCount = ArticlesEditedCount,
			ArticlesStartedCount = ArticlesStartedCount,
			TalkPostCount = TalkPostCount
		};
	}

	/// <summary>
	/// Builds the query for the user's revisions that count as contributions: everything except user pages.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <returns>A composable query of the user's contribution revisions.</returns>
	private IQueryable<ArticleRevision> GetContributionRevisions(Guid UserId)
		=> Context.ArticleRevisions.Where(r => r.CreatedByUserId == UserId && r.Type != ArticleType.User);

	/// <summary>
	/// Builds a query for the identifier of the first surviving revision of every article the user has edited. It is
	/// composed as an uncorrelated <c>IN</c> subquery, which SQLite evaluates once per statement; ArticleRevisions has
	/// no index on CanonicalArticleId, so a correlated lookup would rescan the table for every row.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <returns>A composable query of first-revision identifiers.</returns>
	private IQueryable<int> GetFirstRevisionIdsQuery(Guid UserId)
	{
		IQueryable<Guid> EditedArticleIds = GetContributionRevisions(UserId).Select(r => r.CanonicalArticleId);

		return Context.ArticleRevisions
			.Where(r => EditedArticleIds.Contains(r.CanonicalArticleId))
			.GroupBy(r => r.CanonicalArticleId)
			.Select(g => g.Min(r => r.Id));
	}

	/// <summary>
	/// Finds the most recent revision or talk post time. Ordering by the autoincrement key follows insertion order and
	/// uses the CreatedByUserId indexes, avoiding a sort on the text timestamp.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The latest activity time, or <see langword="null"/> when the user has none.</returns>
	private async Task<DateTimeOffset?> GetLastActiveAsync(Guid UserId, CancellationToken CancellationToken)
	{
		DateTimeOffset? LastEdit = await Context.ArticleRevisions
			.Where(r => r.CreatedByUserId == UserId)
			.OrderByDescending(r => r.Id)
			.Select(r => (DateTimeOffset?)r.DateCreated)
			.FirstOrDefaultAsync(CancellationToken);

		DateTimeOffset? LastPost = await Context.ArticleTalkSubjectPosts
			.Where(p => p.CreatedByUserId == UserId)
			.Join(Context.ArticleTalkSubjects, p => p.ArticleTalkSubjectId, s => s.Id, (p, s) => p)
			.OrderByDescending(p => p.Id)
			.Select(p => (DateTimeOffset?)p.DateCreated)
			.FirstOrDefaultAsync(CancellationToken);

		return (LastEdit, LastPost) switch
		{
			({ } Edit, { } Post) => Edit > Post ? Edit : Post,
			({ } Edit, null) => Edit,
			(null, { } Post) => Post,
			_ => null
		};
	}

	/// <summary>
	/// Groups the categories of the current revisions of every article the user has edited, busiest first.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>Every category with its article count, ordered by count and then slug.</returns>
	private async Task<IReadOnlyList<ProfileFocusArea>> GetFocusAreasAsync(Guid UserId, CancellationToken CancellationToken)
	{
		IQueryable<Guid> EditedArticleIds = GetContributionRevisions(UserId).Select(r => r.CanonicalArticleId);

		var Groups = await Context.ArticleRevisions
			.Where(r => r.IsCurrent && r.Type != ArticleType.User && EditedArticleIds.Contains(r.CanonicalArticleId))
			.SelectMany(r => r.CategoryMemberships)
			.GroupBy(m => m.CategorySlug)
			.Select(g => new { Slug = g.Key, Count = g.Count() })
			.OrderByDescending(g => g.Count)
			.ThenBy(g => g.Slug)
			.ToListAsync(CancellationToken);

		return [.. Groups.Select(g => new ProfileFocusArea(g.Slug, ToCategoryLabel(g.Slug), g.Count))];
	}

	/// <summary>
	/// Merges the newest revisions and talk posts into one feed.
	/// </summary>
	/// <param name="UserId">The profile owner.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>Up to <see cref="RecentActivityCount"/> entries, newest first.</returns>
	private async Task<IReadOnlyList<ProfileActivityItem>> GetRecentActivityAsync(Guid UserId, CancellationToken CancellationToken)
	{
		IReadOnlyList<ProfileActivityItem> Edits = await GetRevisionItemsAsync(UserId, 0, RecentActivityCount, LinkToRevision: false, CancellationToken);

		var Posts = await Context.ArticleTalkSubjectPosts
			.Where(p => p.CreatedByUserId == UserId)
			.OrderByDescending(p => p.Id)
			.Join(Context.ArticleTalkSubjects, p => p.ArticleTalkSubjectId, s => s.Id, (p, s) => new { p.DateCreated, s.Subject, SubjectSlug = s.UrlSlug, s.CanonicalArticleId })
			.Join(Context.ArticleRevisions.Where(r => r.IsCurrent), x => x.CanonicalArticleId, r => r.CanonicalArticleId,
				(x, r) => new { x.DateCreated, x.Subject, x.SubjectSlug, r.Title, r.UrlSlug, r.Type })
			.Take(RecentActivityCount)
			.ToListAsync(CancellationToken);

		IEnumerable<ProfileActivityItem> PostItems = Posts.Select(p => new ProfileActivityItem(
			ProfileActivityKind.Discussed,
			p.Subject,
			ArticleUrlHelper.BuildRelativePath(p.UrlSlug, p.Type, $"talk/{p.SubjectSlug}"),
			p.Title,
			p.Type,
			p.DateCreated));

		return [.. Edits.Concat(PostItems).OrderByDescending(i => i.Date).Take(RecentActivityCount)];
	}

	/// <summary>
	/// Reads the <c>{{Subtitle}}</c> and <c>{{ShortDescription}}</c> directives from the profile article, so the header
	/// can show the owner's own tagline and summary on every tab. Parsing without enrichment or rendering is cheap.
	/// </summary>
	/// <param name="Text">The profile article Markdown, or <see langword="null"/> when there is no article.</param>
	/// <returns>The first subtitle and short description, each <see langword="null"/> when absent or blank.</returns>
	private (string? Tagline, string? Summary) ReadProfileDirectives(string? Text)
	{
		if (string.IsNullOrWhiteSpace(Text))
		{
			return (null, null);
		}

		MarkdownDocument Document = Markdown.Parse(Text, PipelineFactory.GetPipeline());
		string? Tagline = Document.Descendants<SubtitleBlock>().FirstOrDefault()?.Content;
		string? Summary = Document.Descendants<ShortDescriptionBlock>().FirstOrDefault()?.Content;
		return (string.IsNullOrWhiteSpace(Tagline) ? null : Tagline.Trim(), string.IsNullOrWhiteSpace(Summary) ? null : Summary.Trim());
	}

	/// <summary>
	/// Turns a category slug into a readable label: hyphens become spaces and the first letter is capitalized.
	/// </summary>
	/// <param name="Slug">The stored category slug.</param>
	/// <returns>The label, or the slug unchanged when it is empty.</returns>
	public static string ToCategoryLabel(string Slug)
	{
		if (string.IsNullOrWhiteSpace(Slug))
		{
			return Slug;
		}

		string Spaced = Slug.Replace('-', ' ');
		return string.Concat(Spaced[..1].ToUpper(CultureInfo.InvariantCulture), Spaced[1..]);
	}

	/// <summary>
	/// Counts complete years between two instants, as an age would be counted.
	/// </summary>
	/// <param name="Start">The earlier instant.</param>
	/// <param name="End">The later instant.</param>
	/// <returns>The number of anniversaries of <paramref name="Start"/> reached by <paramref name="End"/>, never negative.</returns>
	public static int GetWholeYearsBetween(DateTimeOffset Start, DateTimeOffset End)
	{
		DateTime StartUtc = Start.UtcDateTime;
		DateTime EndUtc = End.UtcDateTime;
		int Years = EndUtc.Year - StartUtc.Year;
		if (EndUtc.Month < StartUtc.Month || (EndUtc.Month == StartUtc.Month && EndUtc.Day < StartUtc.Day))
		{
			Years--;
		}

		return Math.Max(0, Years);
	}
}
