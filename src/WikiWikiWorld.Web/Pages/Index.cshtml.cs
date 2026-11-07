namespace WikiWikiWorld.Web.Pages;

using WikiWikiWorld.Data;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Page model for the home page, displaying a grid of recent articles with covers.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class IndexModel(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Curated top-level magazine categories shown as pills on the home page, in display order.
	/// </summary>
	/// <remarks>
	/// Slugs are fixed rather than derived from titles, because titles such as "News &amp; Politics",
	/// "Women's Interest" and "LGBTQ+" do not slugify cleanly. Titles are English on every culture for now.
	/// </remarks>
	public static IReadOnlyList<HomeCategory> Categories { get; } =
	[
		new("News & Politics", "news-politics"),
		new("General Interest", "general-interest"),
		new("Ideas & Criticism", "ideas-criticism"),
		new("Business & Finance", "business-finance"),
		new("Law & Justice", "law-justice"),
		new("Science & Nature", "science-nature"),
		new("Computing", "computing"),
		new("Technology", "technology"),
		new("Video Games", "video-games"),
		new("Games & Puzzles", "games-puzzles"),
		new("History", "history"),
		new("Military & Defence", "military-defence"),
		new("Art & Design", "art-design"),
		new("Photography", "photography"),
		new("Architecture", "architecture"),
		new("Music", "music"),
		new("Film, TV & Radio", "film-tv-radio"),
		new("Performing Arts", "performing-arts"),
		new("Celebrity & Royalty", "celebrity-royalty"),
		new("Literature & Fiction", "literature-fiction"),
		new("Comics & Manga", "comics-manga"),
		new("Humour & Satire", "humour-satire"),
		new("True Crime", "true-crime"),
		new("Women's Interest", "womens-interest"),
		new("Men's Interest", "mens-interest"),
		new("LGBTQ+", "lgbtq"),
		new("Fashion & Beauty", "fashion-beauty"),
		new("Home & Garden", "home-garden"),
		new("Food & Drink", "food-drink"),
		new("Travel & Outdoors", "travel-outdoors"),
		new("Health & Medicine", "health-medicine"),
		new("Sport & Fitness", "sport-fitness"),
		new("Hunting, Shooting & Angling", "hunting-shooting-angling"),
		new("Transport & Vehicles", "transport-vehicles"),
		new("Hobbies & Crafts", "hobbies-crafts"),
		new("Collecting & Antiques", "collecting-antiques"),
		new("Pets & Animals", "pets-animals"),
		new("Farming & Rural Life", "farming-rural-life"),
		new("Children & Youth", "children-youth"),
		new("Family & Relationships", "family-relationships"),
		new("Education & Careers", "education-careers"),
		new("Religion & Belief", "religion-belief"),
		new("Paranormal & Esoteric", "paranormal-esoteric"),
		new("Consumer & Shopping", "consumer-shopping"),
		new("Trade & Professional", "trade-professional"),
		new("Adult", "adult"),
	];

	/// <summary>
	/// Gets the list of articles with their cover image information to display.
	/// </summary>
	public List<ArticleWithCover> ArticlesWithCovers { get; private set; } = [];

	/// <summary>
	/// Handles the GET request to populate the article grid.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	public async Task OnGetAsync(CancellationToken CancellationToken)
	{
		LatestArticlesWithCoverCarouselSpec LatestSpec = new(50);
		var Articles = await Context.ArticleRevisions
			.WithSpecification(LatestSpec)
			.Where(x => x.SiteId == SiteId && x.Culture == Culture)
			.Select(x => new { x.Title, x.UrlSlug, x.Text, x.DateCreated })
			.ToListAsync(CancellationToken);

		List<string> CoverSlugs = [.. Articles
			.Select(a => ArticleCoverReference.Extract(a.Text))
			.OfType<string>()
			.Select(SlugHelper.GenerateSlug)
			.Distinct(StringComparer.OrdinalIgnoreCase)];

		if (CoverSlugs.Count == 0)
		{
			return;
		}

		var CoverArticles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(x => x.SiteId == SiteId && x.Culture == Culture && CoverSlugs.Contains(x.UrlSlug) && x.IsCurrent && x.DateDeleted == null && x.CanonicalFileId.HasValue)
			.Select(x => new { x.UrlSlug, CanonicalFileId = x.CanonicalFileId!.Value })
			.ToListAsync(CancellationToken);

		List<Guid> FileIds = [.. CoverArticles
			.Select(c => c.CanonicalFileId)
			.Distinct()];

		var Files = await Context.FileRevisions
			.AsNoTracking()
			.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
			.Select(f => new { f.CanonicalFileId, f.Id, f.Filename })
			.ToListAsync(CancellationToken);

		Dictionary<string, Guid> CoverArticleLookup = CoverArticles.ToDictionary(a => a.UrlSlug, a => a.CanonicalFileId, StringComparer.OrdinalIgnoreCase);
		Dictionary<Guid, (int Id, string Filename)> FileLookup = Files.ToDictionary(f => f.CanonicalFileId, f => (f.Id, f.Filename));

		foreach (var Article in Articles)
		{
			string? CoverImageSlug = ArticleCoverReference.Extract(Article.Text);
			if (string.IsNullOrEmpty(CoverImageSlug))
			{
				continue;
			}
			CoverImageSlug = SlugHelper.GenerateSlug(CoverImageSlug);

			if (!CoverArticleLookup.TryGetValue(CoverImageSlug, out Guid CanonicalFileId))
			{
				continue;
			}

			if (!FileLookup.TryGetValue(CanonicalFileId, out (int Id, string Filename) File))
			{
				continue;
			}

			string ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, CanonicalFileId, File.Id, File.Filename);
			ArticlesWithCovers.Add(new ArticleWithCover(
				Article.Title,
				Article.UrlSlug,
				ImageUrl,
				Article.DateCreated
			));
		}
	}
}

/// <summary>
/// Represents an article summary with its cover image for display on the index page.
/// </summary>
/// <param name="Title">The title of the article.</param>
/// <param name="UrlSlug">The URL slug of the article.</param>
/// <param name="ImageUrl">The URL of the cover image.</param>
/// <param name="DateCreated">The creation date of the article.</param>
public sealed record ArticleWithCover(string Title, string UrlSlug, string ImageUrl, DateTimeOffset DateCreated);

/// <summary>
/// A home-page category pill linking to <c>/category:{Slug}</c>.
/// </summary>
/// <param name="Title">The display label.</param>
/// <param name="Slug">The fixed URL slug, without the <c>category:</c> prefix.</param>
public sealed record HomeCategory(string Title, string Slug);
