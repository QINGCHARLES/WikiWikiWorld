using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies the profile calendar, badge, and formatting rules, and the profile queries against actual SQLite.
/// </summary>
public sealed class ProfileTests : IDisposable
{
	private static readonly MarkdownPipelineFactory PipelineFactory = new(Options.Create(new SiteConfiguration()));

	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");

	/// <summary>
	/// Creates a disposable database with the application's EF schema.
	/// </summary>
	public ProfileTests()
	{
		Connection.Open();
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Database.EnsureCreated();
	}

	/// <inheritdoc/>
	public void Dispose() => Connection.Dispose();

	/// <summary>
	/// The window starts on the Monday 52 weeks before the current week, whatever weekday today is.
	/// </summary>
	/// <param name="Today">An ISO date inside the week starting Monday 19 October 2026.</param>
	[Theory]
	[InlineData("2026-10-19")]
	[InlineData("2026-10-23")]
	[InlineData("2026-10-25")]
	public void CalendarStartsOnMondayFiftyTwoWeeksBack(string Today)
	{
		DateOnly FirstDay = ProfileActivityCalendar.GetFirstDay(DateOnly.Parse(Today, CultureInfo.InvariantCulture));

		Assert.Equal(new DateOnly(2025, 10, 20), FirstDay);
		Assert.Equal(DayOfWeek.Monday, FirstDay.DayOfWeek);
	}

	/// <summary>
	/// Edits are bucketed by UTC day, out-of-window edits are ignored, and streaks and month labels follow the days.
	/// </summary>
	[Fact]
	public void CalendarBucketsDaysAndComputesStreaks()
	{
		DateOnly Today = new(2026, 10, 23);
		DateTimeOffset[] Timestamps =
		[
			// 23:30 at UTC-5 is the next UTC day: 2026-10-23.
			new(2026, 10, 22, 23, 30, 0, TimeSpan.FromHours(-5)),
			new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero),
			new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero),
			new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero),
			new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero),
			new(2026, 3, 3, 9, 0, 0, TimeSpan.Zero),
			new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero),
			new(2026, 3, 5, 9, 0, 0, TimeSpan.Zero),
			new(2024, 1, 1, 9, 0, 0, TimeSpan.Zero),
			new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero)
		];

		ProfileActivityCalendar Calendar = ProfileActivityCalendar.Build(Timestamps, Today);

		Assert.Equal(ProfileActivityCalendar.WeekCount, Calendar.Weeks.Count);
		Assert.All(Calendar.Weeks, w => Assert.Equal(7, w.Days.Count));
		Assert.Equal(8, Calendar.TotalCount);
		Assert.Equal(7, Calendar.ActiveDayCount);
		Assert.Equal(4, Calendar.LongestStreakDays);
		Assert.Equal(3, Calendar.CurrentStreakDays);

		ProfileActivityDay[] AllDays = [.. Calendar.Weeks.SelectMany(w => w.Days)];
		Assert.Equal(2, AllDays.Single(d => d.Date == new DateOnly(2026, 10, 21)).Count);
		Assert.Equal(2, AllDays.Single(d => d.Date == new DateOnly(2026, 10, 21)).Level);
		Assert.Equal(1, AllDays.Single(d => d.Date == Today).Count);
		Assert.True(AllDays.Single(d => d.Date == new DateOnly(2026, 10, 24)).IsFuture);
		Assert.False(AllDays.Single(d => d.Date == Today).IsFuture);
		Assert.Contains(Calendar.Weeks, w => w.MonthStart == new DateOnly(2026, 3, 1));
	}

	/// <summary>
	/// The current streak survives a quiet today by counting from yesterday, and stops at the first gap.
	/// </summary>
	[Fact]
	public void CurrentStreakCountsFromYesterdayWhenTodayIsQuiet()
	{
		DateOnly Today = new(2026, 10, 23);
		DateTimeOffset[] Timestamps =
		[
			new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero),
			new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero),
			new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero)
		];

		ProfileActivityCalendar Calendar = ProfileActivityCalendar.Build(Timestamps, Today);

		Assert.Equal(2, Calendar.CurrentStreakDays);
		Assert.Equal(2, Calendar.LongestStreakDays);
	}

	/// <summary>
	/// Intensity levels use fixed thresholds.
	/// </summary>
	/// <param name="Count">The edit count for a day.</param>
	/// <param name="Expected">The expected level.</param>
	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(3, 2)]
	[InlineData(4, 3)]
	[InlineData(7, 3)]
	[InlineData(8, 4)]
	[InlineData(500, 4)]
	public void CalendarLevelsUseFixedThresholds(int Count, int Expected) =>
		Assert.Equal(Expected, ProfileActivityCalendar.GetLevel(Count));

	/// <summary>
	/// Earned badges come first in catalog order, and progress is clamped to 0–100%.
	/// </summary>
	[Fact]
	public void BadgesListEarnedFirstWithClampedProgress()
	{
		ProfileStats Stats = new() { EditCount = 60, ArticlesEditedCount = 9, ArticlesStartedCount = 1, TalkPostCount = 4 };

		IReadOnlyList<ProfileBadge> Badges = ProfileBadgeCatalog.Evaluate(Stats, MembershipDays: 400, LongestStreakDays: 3, CategoryCount: 12);

		Assert.Equal(11, Badges.Count);
		Assert.Equal(["debut", "regular", "byline", "polymath", "anniversary"], Badges.TakeWhile(b => b.IsEarned).Select(b => b.Key));
		Assert.All(Badges.SkipWhile(b => b.IsEarned), b => Assert.False(b.IsEarned));
		Assert.Equal(12, Badges.Single(b => b.Key == "archivist").ProgressPercent);
		Assert.Equal(100, Badges.Single(b => b.Key == "polymath").ProgressPercent);
		Assert.Equal(40, Badges.Single(b => b.Key == "columnist").ProgressPercent);
	}

	/// <summary>
	/// Ranks begin at their edit thresholds.
	/// </summary>
	/// <param name="EditCount">The user's edit count.</param>
	/// <param name="Expected">The expected rank.</param>
	[Theory]
	[InlineData(0, "Reader")]
	[InlineData(1, "Contributor")]
	[InlineData(49, "Contributor")]
	[InlineData(50, "Regular")]
	[InlineData(500, "Archivist")]
	[InlineData(2_500, "Masthead")]
	public void RankFollowsEditThresholds(int EditCount, string Expected) =>
		Assert.Equal(Expected, ProfileBadgeCatalog.GetRank(EditCount));

	/// <summary>
	/// Roman numerals use subtractive pairs, and values outside 1–3999 are rejected.
	/// </summary>
	[Fact]
	public void RomanNumeralsFormatAndRejectOutOfRange()
	{
		Assert.Equal("I", ProfileFormatting.ToRomanNumeral(1));
		Assert.Equal("IV", ProfileFormatting.ToRomanNumeral(4));
		Assert.Equal("VIII", ProfileFormatting.ToRomanNumeral(8));
		Assert.Equal("XIV", ProfileFormatting.ToRomanNumeral(14));
		Assert.Equal("MCMXCIV", ProfileFormatting.ToRomanNumeral(1994));
		Assert.Equal("MMMCMXCIX", ProfileFormatting.ToRomanNumeral(3999));
		Assert.Throws<ArgumentOutOfRangeException>(() => ProfileFormatting.ToRomanNumeral(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => ProfileFormatting.ToRomanNumeral(4000));
	}

	/// <summary>
	/// Relative times drop the "Updated" prefix and treat future instants as now.
	/// </summary>
	[Fact]
	public void RelativeTimeDropsPrefixAndClampsFuture()
	{
		DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

		Assert.Equal("two hours ago", ProfileFormatting.ToRelativeTime(Now.AddHours(-2), Now));
		Assert.Equal("less than a minute ago", ProfileFormatting.ToRelativeTime(Now.AddMinutes(5), Now).ToLowerInvariant());
	}

	/// <summary>
	/// Whole years count anniversaries reached, so the masthead volume turns over on the join date.
	/// </summary>
	[Fact]
	public void WholeYearsCountAnniversaries()
	{
		DateTimeOffset Joined = new(2019, 3, 14, 10, 0, 0, TimeSpan.Zero);

		Assert.Equal(6, ProfileService.GetWholeYearsBetween(Joined, new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero)));
		Assert.Equal(7, ProfileService.GetWholeYearsBetween(Joined, new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero)));
		Assert.Equal(0, ProfileService.GetWholeYearsBetween(Joined, Joined.AddDays(-1)));
	}

	/// <summary>
	/// Totals are scoped to the current site, exclude user pages, and credit a start only for an article's first revision.
	/// </summary>
	[Fact]
	public async Task HeaderTotalsAreSiteScopedAndExcludeUserPages()
	{
		ProfileSeed Seed = await SeedAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(SiteId: 1);
		ProfileService Service = CreateService(Context);

		ProfileHeader? Header = await Service.GetHeaderAsync("sarah", CreatePrincipal(Seed.Sarah), 1, CancellationToken.None);

		Assert.NotNull(Header);
		Assert.Equal("Sarah", Header.UserName);
		Assert.True(Header.IsOwner);
		Assert.Equal(4, Header.Stats.EditCount);
		Assert.Equal(2, Header.Stats.ArticlesEditedCount);
		Assert.Equal(1, Header.Stats.ArticlesStartedCount);
		Assert.Equal(1, Header.Stats.TalkPostCount);
		Assert.Equal(1, Header.BookmarkCount);
		Assert.Equal("Contributor", Header.Rank);
		Assert.True(Header.HasProfileArticle);
		Assert.False(Header.IsProfileArticleWritten);
		Assert.Null(Header.Tagline);
		Assert.Equal(Seed.SarahJoined, Header.DateJoined);
		Assert.Equal(Seed.LatestPost, Header.LastActive);
	}

	/// <summary>
	/// A visitor is not the owner and does not see the private bookmark count; written articles expose their directives;
	/// accounts with the default join date fall back to their first revision.
	/// </summary>
	[Fact]
	public async Task HeaderForVisitorReadsDirectivesAndFallsBackJoinDate()
	{
		ProfileSeed Seed = await SeedAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(SiteId: 1);
		ProfileService Service = CreateService(Context);

		ProfileHeader? Header = await Service.GetHeaderAsync("Mags", CreatePrincipal(Seed.Sarah), 1, CancellationToken.None);

		Assert.NotNull(Header);
		Assert.False(Header.IsOwner);
		Assert.Null(Header.BookmarkCount);
		Assert.True(Header.IsProfileArticleWritten);
		Assert.Equal("Style, citation, repeat.", Header.Tagline);
		Assert.Equal("Collector of 1990s fashion titles.", Header.Summary);
		Assert.Equal(Seed.MagsFirstRevision, Header.DateJoined);
		Assert.Null(await Service.GetHeaderAsync("nobody", new ClaimsPrincipal(new ClaimsIdentity()), 1, CancellationToken.None));
	}

	/// <summary>
	/// Contributions are ordered by timestamp even when identifiers are not, mark creations, and page correctly.
	/// </summary>
	[Fact]
	public async Task ContributionsOrderByTimestampAndPage()
	{
		ProfileSeed Seed = await SeedAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(SiteId: 1);
		ProfileService Service = CreateService(Context);

		ProfileContributionPage First = await Service.GetContributionsAsync(Seed.Sarah, 1, 3, CancellationToken.None);
		ProfileContributionPage Second = await Service.GetContributionsAsync(Seed.Sarah, 2, 3, CancellationToken.None);

		Assert.True(First.HasNextPage);
		Assert.False(Second.HasNextPage);
		ProfileActivityItem[] All = [.. First.Items, .. Second.Items];
		Assert.Equal(4, All.Length);
		Assert.Equal(All.OrderByDescending(i => i.Date).Select(i => i.Date), All.Select(i => i.Date));
		Assert.Equal(["Beta", "Alpha", "Beta", "Alpha"], All.Select(i => i.Title));
		Assert.Equal(ProfileActivityKind.Created, All[^1].Kind);
		Assert.All(All[..^1], i => Assert.Equal(ProfileActivityKind.Edited, i.Kind));
		Assert.StartsWith("/alpha/revision/", All[^1].Path, StringComparison.Ordinal);
	}

	/// <summary>
	/// The overview's focus areas come from current revisions, and its feed merges edits with talk posts.
	/// </summary>
	[Fact]
	public async Task OverviewGroupsCurrentCategoriesAndMergesActivity()
	{
		ProfileSeed Seed = await SeedAsync();
		await using WikiWikiWorldDbContext Context = CreateContext(SiteId: 1);
		ProfileService Service = CreateService(Context);
		ProfileHeader Header = (await Service.GetHeaderAsync("Sarah", CreatePrincipal(Seed.Sarah), 1, CancellationToken.None))!;

		ProfileOverview Overview = await Service.GetOverviewAsync(Header, CancellationToken.None);

		Assert.Equal(["fashion-magazines", "photography"], Overview.FocusAreas.Select(f => f.CategorySlug));
		Assert.Equal([2, 1], Overview.FocusAreas.Select(f => f.ArticleCount));
		Assert.Equal("Fashion magazines", Overview.FocusAreas[0].Label);
		Assert.Equal(2, Overview.CategoryCount);
		Assert.Equal(ProfileActivityKind.Discussed, Overview.RecentActivity[0].Kind);
		Assert.Equal("/beta/talk/sources", Overview.RecentActivity[0].Path);
		Assert.Equal("/beta", Overview.RecentActivity[1].Path);
		Assert.Null(Overview.ProfileArticle);
		Assert.Contains(Overview.Badges, b => b.Key == "byline" && b.IsEarned);
	}

	/// <summary>
	/// The IDs and instants created by <see cref="SeedAsync"/>.
	/// </summary>
	/// <param name="Sarah">The main user's identifier.</param>
	/// <param name="SarahJoined">The main user's recorded join date.</param>
	/// <param name="LatestPost">The main user's most recent site-1 talk post.</param>
	/// <param name="MagsFirstRevision">The first revision time of the user with a default join date.</param>
	private sealed record class ProfileSeed(Guid Sarah, DateTimeOffset SarahJoined, DateTimeOffset LatestPost, DateTimeOffset MagsFirstRevision);

	/// <summary>
	/// Seeds two users, two articles with interleaved edits whose identifiers run against time, user pages, talk posts
	/// on two sites, and a bookmark.
	/// </summary>
	/// <returns>The identifiers and instants the tests assert against.</returns>
	private async Task<ProfileSeed> SeedAsync()
	{
		DateTimeOffset Now = DateTimeOffset.UtcNow;
		Guid Sarah = Guid.NewGuid();
		Guid Mags = Guid.NewGuid();
		DateTimeOffset SarahJoined = Now.AddYears(-2);
		DateTimeOffset MagsFirstRevision = Now.AddDays(-30);
		Guid Alpha = Guid.NewGuid();
		Guid Beta = Guid.NewGuid();
		Guid SarahPage = Guid.NewGuid();
		Guid MagsPage = Guid.NewGuid();

		await using WikiWikiWorldDbContext Context = CreateContext();
		Context.Users.AddRange(
			new User { Id = Sarah, UserName = "Sarah", NormalizedUserName = "SARAH", DateCreated = SarahJoined },
			new User { Id = Mags, UserName = "Mags", NormalizedUserName = "MAGS", DateCreated = default });

		// Inserted out of date order so identifier order and time order disagree.
		Context.ArticleRevisions.AddRange(
			Revision(Beta, "Beta", Sarah, Now.AddDays(-1), "{{Category Fashion magazines}}"),
			Revision(Alpha, "Alpha", Sarah, Now.AddDays(-10), "{{Category Fashion magazines}}\n{{Category Photography}}", IsCurrent: false),
			Revision(Alpha, "Alpha", Mags, Now.AddDays(-5), "{{Category Fashion magazines}}\n{{Category Photography}}", IsCurrent: false),
			Revision(Alpha, "Alpha", Sarah, Now.AddDays(-2), "{{Category Fashion magazines}}\n{{Category Photography}}"),
			Revision(Beta, "Beta", Mags, MagsFirstRevision, "{{Category Fashion magazines}}", IsCurrent: false),
			Revision(Beta, "Beta", Sarah, Now.AddDays(-3), "{{Category Fashion magazines}}", IsCurrent: false),
			Revision(SarahPage, "@Sarah", Sarah, Now.AddDays(-40), ArticleRevisionService.GetDefaultUserArticleText("Sarah"), ArticleType.User),
			Revision(MagsPage, "@Mags", Mags, Now.AddDays(-20), "{{Subtitle Style, citation, repeat.}}\n{{ShortDescription Collector of 1990s fashion titles.}}\n\nHello.", ArticleType.User),
			Revision(Guid.NewGuid(), "Elsewhere", Sarah, Now.AddDays(-1), "Other site", SiteId: 2));
		await Context.SaveChangesAsync();

		// Mags's first revision in date order is Beta's first; Sarah's Alpha revision at -10 days starts Alpha.
		ArticleTalkSubject SiteOneSubject = new()
		{
			SiteId = 1, CanonicalArticleId = Beta, Subject = "Sources", UrlSlug = "sources", CreatedByUserId = Mags, DateCreated = Now.AddDays(-4)
		};
		ArticleTalkSubject SiteTwoSubject = new()
		{
			SiteId = 2, CanonicalArticleId = Guid.NewGuid(), Subject = "Other", UrlSlug = "other", CreatedByUserId = Sarah, DateCreated = Now.AddDays(-4)
		};
		Context.ArticleTalkSubjects.AddRange(SiteOneSubject, SiteTwoSubject);
		await Context.SaveChangesAsync();

		DateTimeOffset LatestPost = Now.AddHours(-3);
		Context.ArticleTalkSubjectPosts.AddRange(
			new ArticleTalkSubjectPost { ArticleTalkSubjectId = SiteOneSubject.Id, Text = "Checked.", CreatedByUserId = Sarah, DateCreated = LatestPost },
			new ArticleTalkSubjectPost { ArticleTalkSubjectId = SiteTwoSubject.Id, Text = "Elsewhere.", CreatedByUserId = Sarah, DateCreated = Now.AddHours(-1) });

		BookmarkList List = new() { SiteId = 1, Name = "Bookmarks", IsDefault = true, CreatedByUserId = Sarah, DateCreated = Now };
		Context.BookmarkLists.Add(List);
		await Context.SaveChangesAsync();
		Context.Bookmarks.Add(new Bookmark { BookmarkListId = List.Id, SiteId = 1, CanonicalArticleId = Alpha, CreatedByUserId = Sarah, DateCreated = Now });
		await Context.SaveChangesAsync();

		return new ProfileSeed(Sarah, SarahJoined, LatestPost, MagsFirstRevision);
	}

	/// <summary>
	/// Creates a site-1 English revision whose slug is the lower-cased title, or the exact title for user pages, which
	/// keep the user name's case as the application stores them.
	/// </summary>
	/// <param name="CanonicalArticleId">The article identity.</param>
	/// <param name="Title">The article title, from which the slug is derived.</param>
	/// <param name="UserId">The author.</param>
	/// <param name="DateCreated">When the revision was saved.</param>
	/// <param name="Text">The Markdown source.</param>
	/// <param name="Type">The article namespace.</param>
	/// <param name="IsCurrent">Whether this is the article's current revision.</param>
	/// <param name="SiteId">The owning site.</param>
	/// <returns>An unsaved revision.</returns>
	private static ArticleRevision Revision(Guid CanonicalArticleId, string Title, Guid UserId, DateTimeOffset DateCreated, string Text,
		ArticleType Type = ArticleType.Article, bool IsCurrent = true, int SiteId = 1) => new()
	{
		CanonicalArticleId = CanonicalArticleId,
		SiteId = SiteId,
		Culture = "en",
		Title = Title,
		UrlSlug = Type == ArticleType.User ? Title : Title.ToLowerInvariant(),
		IsCurrent = IsCurrent,
		Type = Type,
		Text = Text,
		RevisionReason = "Test",
		CreatedByUserId = UserId,
		DateCreated = DateCreated
	};

	/// <summary>
	/// Creates a context on the shared connection, scoped to one site and culture when a site is given.
	/// </summary>
	/// <param name="SiteId">The site whose query filters to apply, or <see langword="null"/> for unfiltered seeding.</param>
	/// <returns>A new context with category indexing enabled.</returns>
	private WikiWikiWorldDbContext CreateContext(int? SiteId = null)
	{
		DbContextOptions<WikiWikiWorldDbContext> Options = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.AddInterceptors(new CategoryMembershipSaveInterceptor(PipelineFactory))
			.Options;
		return SiteId is { } Site ? new(Options, new FixedSiteContext(Site)) : new(Options);
	}

	/// <summary>
	/// Creates the service with a real Identity user manager over the test context.
	/// </summary>
	/// <param name="Context">The site-scoped context.</param>
	/// <returns>The profile service under test.</returns>
	private static ProfileService CreateService(WikiWikiWorldDbContext Context)
	{
		UserStore<User, Role, WikiWikiWorldDbContext, Guid> Store = new(Context);
		UserManager<User> UserManager = new(Store, Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), new PasswordHasher<User>(),
			[], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
		return new ProfileService(Context, UserManager, PipelineFactory);
	}

	/// <summary>
	/// Creates a signed-in principal carrying the user identifier claim Identity reads.
	/// </summary>
	/// <param name="UserId">The signed-in user.</param>
	/// <returns>An authenticated principal.</returns>
	private static ClaimsPrincipal CreatePrincipal(Guid UserId) =>
		new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"));

	/// <summary>
	/// A site context fixed to one site and English, so the context applies its site and culture filters.
	/// </summary>
	/// <param name="SiteId">The site to scope queries to.</param>
	private sealed class FixedSiteContext(int SiteId) : ISiteContextService
	{
		/// <inheritdoc/>
		public int? GetCurrentSiteId() => SiteId;

		/// <inheritdoc/>
		public string? GetCurrentCulture() => "en";
	}
}
