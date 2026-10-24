namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Identifies the profile page being shown, so the shared profile header can mark its tab as current.
/// </summary>
public enum ProfileTab
{
	/// <summary>
	/// The profile overview at <c>/@user/profile</c>.
	/// </summary>
	Overview,

	/// <summary>
	/// The edit history at <c>/@user/contributions</c>.
	/// </summary>
	Contributions,

	/// <summary>
	/// The owner's bookmark lists or publicly visible lists at <c>/@user/bookmarks</c>.
	/// </summary>
	Bookmarks,

	/// <summary>
	/// The owner-only messages inbox at <c>/@user/messages</c>.
	/// </summary>
	Messages,

	/// <summary>
	/// The owner-only preferences at <c>/@user/settings</c>.
	/// </summary>
	Settings
}

/// <summary>
/// Everything the shared profile header needs: identity, viewer relationship, and the headline statistics shown in
/// the masthead and tab counts. Loaded once per profile request by <see cref="ProfileService.GetHeaderAsync"/>.
/// </summary>
public sealed record class ProfileHeader
{
	/// <summary>
	/// Gets the profile owner's user identifier.
	/// </summary>
	public required Guid UserId { get; init; }

	/// <summary>
	/// Gets the profile owner's user name as stored, which may differ in case from the requested route value.
	/// </summary>
	public required string UserName { get; init; }

	/// <summary>
	/// Gets the application-relative avatar URL, or <see langword="null"/> when the user has no profile picture.
	/// </summary>
	public string? AvatarPath { get; init; }

	/// <summary>
	/// Gets the instant the account was created, falling back to the user's first revision for accounts registered
	/// before join dates were recorded; <see langword="null"/> when neither is known.
	/// </summary>
	public DateTimeOffset? DateJoined { get; init; }

	/// <summary>
	/// Gets the most recent edit or talk post on the current site and culture, or <see langword="null"/> when the user has none.
	/// </summary>
	public DateTimeOffset? LastActive { get; init; }

	/// <summary>
	/// Gets a value indicating whether the signed-in viewer owns this profile.
	/// </summary>
	public required bool IsOwner { get; init; }

	/// <summary>
	/// Gets a value indicating whether the viewer is signed in.
	/// </summary>
	public required bool IsViewerAuthenticated { get; init; }

	/// <summary>
	/// Gets a value indicating whether the user has written their profile article (the <c>@UserName</c> user page).
	/// </summary>
	public required bool HasProfileArticle { get; init; }

	/// <summary>
	/// Gets a value indicating whether the profile article has been written, as opposed to still holding the
	/// placeholder text of the user page created at registration.
	/// </summary>
	public required bool IsProfileArticleWritten { get; init; }

	/// <summary>
	/// Gets the profile article's <c>{{Subtitle}}</c> text, shown as the masthead epigraph, or <see langword="null"/>.
	/// </summary>
	public string? Tagline { get; init; }

	/// <summary>
	/// Gets the profile article's <c>{{ShortDescription}}</c> text, shown as the one-line bio, or <see langword="null"/>.
	/// </summary>
	public string? Summary { get; init; }

	/// <summary>
	/// Gets the identity role names held by the user, such as administrator roles; empty for ordinary contributors.
	/// </summary>
	public required IReadOnlyList<string> Roles { get; init; }

	/// <summary>
	/// Gets the contribution totals for the current site and culture.
	/// </summary>
	public required ProfileStats Stats { get; init; }

	/// <summary>
	/// Gets the number of visible current-article memberships in readable lists; null for visitors without public lists.
	/// </summary>
	public int? BookmarkCount { get; init; }

	/// <summary>Gets whether this profile has any public bookmark lists, including empty lists.</summary>
	public bool HasPublicBookmarkLists { get; init; }

	/// <summary>
	/// Gets the masthead volume: one plus the number of whole years since the account was created, so a first-year member is volume 1.
	/// </summary>
	public required int VolumeNumber { get; init; }

	/// <summary>
	/// Gets the editorial rank derived from the edit count, such as <c>Regular</c> or <c>Archivist</c>.
	/// </summary>
	public required string Rank { get; init; }

	/// <summary>
	/// Gets the profile URL path for this user, <c>/@UserName/profile</c>.
	/// </summary>
	public string ProfilePath => $"/@{UserName}/profile";

	/// <summary>
	/// Gets the URL path of the user's profile article, <c>/@UserName</c>.
	/// </summary>
	public string ArticlePath => $"/@{UserName}";
}

/// <summary>
/// Contribution totals for one user on the current site and culture. Deleted revisions and posts are excluded, and so
/// are revisions of user pages.
/// </summary>
public sealed record class ProfileStats
{
	/// <summary>
	/// Gets the number of article revisions the user has saved, excluding user pages.
	/// </summary>
	public required int EditCount { get; init; }

	/// <summary>
	/// Gets the number of distinct articles, other than user pages, the user has saved at least one revision of.
	/// </summary>
	public required int ArticlesEditedCount { get; init; }

	/// <summary>
	/// Gets the number of articles whose first surviving revision the user wrote, excluding their own user page.
	/// </summary>
	public required int ArticlesStartedCount { get; init; }

	/// <summary>
	/// Gets the number of talk-page posts the user has written.
	/// </summary>
	public required int TalkPostCount { get; init; }
}

/// <summary>
/// The data behind the profile overview tab, beyond the shared header.
/// </summary>
public sealed record class ProfileOverview
{
	/// <summary>
	/// Gets the last year of daily edit counts.
	/// </summary>
	public required ProfileActivityCalendar Calendar { get; init; }

	/// <summary>
	/// Gets the categories the user's edited articles fall into most often, busiest first.
	/// </summary>
	public required IReadOnlyList<ProfileFocusArea> FocusAreas { get; init; }

	/// <summary>
	/// Gets the number of distinct categories across all articles the user has edited.
	/// </summary>
	public required int CategoryCount { get; init; }

	/// <summary>
	/// Gets the most recent edits and talk posts, newest first.
	/// </summary>
	public required IReadOnlyList<ProfileActivityItem> RecentActivity { get; init; }

	/// <summary>
	/// Gets every badge in the catalog with the user's progress, earned badges first.
	/// </summary>
	public required IReadOnlyList<ProfileBadge> Badges { get; init; }

	/// <summary>
	/// Gets the current revision of the user's profile article, or <see langword="null"/> when they have not written one.
	/// </summary>
	public ArticleRevision? ProfileArticle { get; init; }
}

/// <summary>
/// A category the user frequently edits in, with the number of their edited articles that belong to it.
/// </summary>
/// <param name="CategorySlug">The immutable category slug, used for the <c>/category:</c> link.</param>
/// <param name="Label">A readable label derived from the slug.</param>
/// <param name="ArticleCount">How many distinct articles the user edited that are currently in this category.</param>
public sealed record class ProfileFocusArea(string CategorySlug, string Label, int ArticleCount);

/// <summary>
/// Distinguishes the kinds of entries in the profile activity feed.
/// </summary>
public enum ProfileActivityKind
{
	/// <summary>
	/// The first surviving revision of an article.
	/// </summary>
	Created,

	/// <summary>
	/// A later revision of an existing article.
	/// </summary>
	Edited,

	/// <summary>
	/// A post in an article talk subject.
	/// </summary>
	Discussed
}

/// <summary>
/// One entry in the profile activity feed or contributions list.
/// </summary>
/// <param name="Kind">Whether the entry is a creation, an edit, or a talk post.</param>
/// <param name="Title">The article title, or the talk subject for posts.</param>
/// <param name="Path">The absolute URL path the entry links to.</param>
/// <param name="Summary">The edit summary or the talk subject's article title, if any.</param>
/// <param name="ArticleType">The type of article the entry concerns.</param>
/// <param name="Date">When the revision or post was saved.</param>
public sealed record class ProfileActivityItem(
	ProfileActivityKind Kind,
	string Title,
	string Path,
	string? Summary,
	ArticleType ArticleType,
	DateTimeOffset Date);

/// <summary>
/// One page of a user's revisions, newest first.
/// </summary>
/// <param name="Items">The revisions on this page.</param>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="HasNextPage">Whether older revisions exist beyond this page.</param>
public sealed record class ProfileContributionPage(IReadOnlyList<ProfileActivityItem> Items, int PageNumber, bool HasNextPage);
