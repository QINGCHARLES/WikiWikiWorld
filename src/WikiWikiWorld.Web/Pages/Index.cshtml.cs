namespace WikiWikiWorld.Web.Pages;

using WikiWikiWorld.Data;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Page model for the home page, displaying a grid of recent articles with covers or fallback header images.
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
	/// "Women's Interest" and "LGBTQ+" do not slugify cleanly. Titles are translation keys: the home page shows
	/// them in the request's UI language, while slugs stay the same on every culture.
	/// </remarks>
	// l10n: "News & Politics", "General Interest", "Ideas & Criticism", "Business & Finance", "Law & Justice", "Science & Nature", "Computing", "Technology", "Video Games", "Games & Puzzles", "History", "Military & Defence", "Art & Design", "Photography", "Architecture", "Music", "Film, TV & Radio", "Performing Arts", "Celebrity & Royalty", "Literature & Fiction", "Comics & Manga", "Humour & Satire", "True Crime", "Women's Interest", "Men's Interest", "LGBTQ+", "Fashion & Beauty", "Home & Garden", "Food & Drink", "Travel & Outdoors", "Health & Medicine", "Sport & Fitness", "Hunting, Shooting & Angling", "Transport & Vehicles", "Hobbies & Crafts", "Collecting & Antiques", "Pets & Animals", "Farming & Rural Life", "Children & Youth", "Family & Relationships", "Education & Careers", "Religion & Belief", "Paranormal & Esoteric", "Consumer & Shopping", "Trade & Professional", "Adult"
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
	/// Gets the list of articles with their resolved cover or header image information to display.
	/// </summary>
	public List<ArticleWithCover> ArticlesWithCovers { get; private set; } = [];

	/// <summary>
	/// Handles the GET request to populate the article grid, preferring each article's cover image and
	/// falling back to its first resolved header image when no cover is available.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	public async Task OnGetAsync(CancellationToken CancellationToken)
	{
		LatestArticlesWithCoverOrHeaderSpec LatestSpec = new(SiteId, Culture, 50);
		var Articles = await Context.ArticleRevisions
			.WithSpecification(LatestSpec)
			.Select(x => new { x.Title, x.UrlSlug, x.Text, x.DateCreated })
			.ToListAsync(CancellationToken);

		if (Articles.Count == 0)
		{
			return;
		}

		List<(string Title, string UrlSlug, DateTimeOffset DateCreated, string? CoverSlug, IReadOnlyList<HeaderImageReference> Headers)> Candidates =
			[with(capacity: Articles.Count)];
		HashSet<string> ImageSlugs = [with(StringComparer.OrdinalIgnoreCase)];

		foreach (var Article in Articles)
		{
			string? RawCoverSlug = ArticleCoverReference.Extract(Article.Text);
			string? NormalizedCoverSlug = !string.IsNullOrEmpty(RawCoverSlug)
				? SlugHelper.GenerateSlug(RawCoverSlug)
				: null;
			if (!string.IsNullOrEmpty(NormalizedCoverSlug))
			{
				ImageSlugs.Add(NormalizedCoverSlug);
			}

			IReadOnlyList<HeaderImageReference> Headers = ArticleCoverReference.ExtractHeaders(Article.Text);
			foreach (HeaderImageReference Header in Headers)
			{
				string NormalizedHeaderSlug = SlugHelper.GenerateSlug(Header.Slug);
				if (!string.IsNullOrEmpty(NormalizedHeaderSlug))
				{
					ImageSlugs.Add(NormalizedHeaderSlug);
				}
			}

			Candidates.Add((Article.Title, Article.UrlSlug, Article.DateCreated, NormalizedCoverSlug, Headers));
		}

		if (ImageSlugs.Count == 0)
		{
			return;
		}

		var ImageArticles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(x => x.SiteId == SiteId && x.Culture == Culture && ImageSlugs.Contains(x.UrlSlug) && x.IsCurrent && x.DateDeleted == null && x.CanonicalFileId.HasValue)
			.Select(x => new { x.UrlSlug, CanonicalFileId = x.CanonicalFileId!.Value })
			.ToListAsync(CancellationToken);

		List<Guid> FileIds = [.. ImageArticles
			.Select(c => c.CanonicalFileId)
			.Distinct()];

		if (FileIds.Count == 0)
		{
			return;
		}

		var Files = await Context.FileRevisions
			.AsNoTracking()
			.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true && f.DateDeleted == null)
			.Select(f => new { f.CanonicalFileId, f.Id, f.Filename })
			.ToListAsync(CancellationToken);

		Dictionary<string, Guid> ImageArticleLookup = ImageArticles.ToDictionary(
			a => a.UrlSlug,
			a => a.CanonicalFileId,
			StringComparer.OrdinalIgnoreCase);
		Dictionary<Guid, (int Id, string Filename)> FileLookup = Files.ToDictionary(
			f => f.CanonicalFileId,
			f => (f.Id, f.Filename));

		foreach ((string Title, string UrlSlug, DateTimeOffset DateCreated, string? CoverSlug, IReadOnlyList<HeaderImageReference> Headers) in Candidates)
		{
			if (TryResolveImageUrl(SiteId, CoverSlug, ImageArticleLookup, FileLookup, out string? CoverUrl))
			{
				ArticlesWithCovers.Add(new ArticleWithCover(
					Title,
					UrlSlug,
					CoverUrl,
					DateCreated));
				continue;
			}

			foreach (HeaderImageReference Header in Headers)
			{
				string NormalizedHeaderSlug = SlugHelper.GenerateSlug(Header.Slug);
				if (TryResolveImageUrl(SiteId, NormalizedHeaderSlug, ImageArticleLookup, FileLookup, out string? HeaderUrl))
				{
					ArticlesWithCovers.Add(new ArticleWithCover(
						Title,
						UrlSlug,
						HeaderUrl,
						DateCreated,
						IsHeaderImage: true,
						ImageFocus: Header.Focus));
					break;
				}
			}
		}
	}

	/// <summary>
	/// Resolves a normalized file-article slug to its public sitefiles URL when both the file article and its current file revision exist.
	/// </summary>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="NormalizedSlug">The normalized file-article slug, or <see langword="null"/>.</param>
	/// <param name="ImageArticleLookup">Lookup from file-article slug to canonical file ID.</param>
	/// <param name="FileLookup">Lookup from canonical file ID to current file revision ID and filename.</param>
	/// <param name="ImageUrl">Receives the resolved image URL when found.</param>
	/// <returns><see langword="true"/> when the image resolved; otherwise <see langword="false"/>.</returns>
	private static bool TryResolveImageUrl(
		int SiteId,
		string? NormalizedSlug,
		Dictionary<string, Guid> ImageArticleLookup,
		Dictionary<Guid, (int Id, string Filename)> FileLookup,
		[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? ImageUrl)
	{
		if (!string.IsNullOrEmpty(NormalizedSlug) &&
			ImageArticleLookup.TryGetValue(NormalizedSlug, out Guid CanonicalFileId) &&
			FileLookup.TryGetValue(CanonicalFileId, out (int Id, string Filename) File))
		{
			ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, CanonicalFileId, File.Id, File.Filename);
			return true;
		}

		ImageUrl = null;
		return false;
	}
}

/// <summary>
/// Represents an article summary with its resolved cover or fallback header image for display on the index page.
/// </summary>
/// <param name="Title">The title of the article.</param>
/// <param name="UrlSlug">The URL slug of the article.</param>
/// <param name="ImageUrl">The URL of the cover or header image.</param>
/// <param name="DateCreated">The creation date of the current article revision.</param>
/// <param name="IsHeaderImage"><see langword="true"/> when <paramref name="ImageUrl"/> came from a fallback <c>{{HeaderImage}}</c> rather than a cover.</param>
/// <param name="ImageFocus">Optional vertical focus (<c>"top"</c>, <c>"center"</c>, or <c>"bottom"</c>) for header images.</param>
public sealed record ArticleWithCover(
	string Title,
	string UrlSlug,
	string ImageUrl,
	DateTimeOffset DateCreated,
	bool IsHeaderImage = false,
	string? ImageFocus = null);

/// <summary>
/// A home-page category pill linking to <c>/category:{Slug}</c>.
/// </summary>
/// <param name="Title">The display label.</param>
/// <param name="Slug">The fixed URL slug, without the <c>category:</c> prefix.</param>
public sealed record HomeCategory(string Title, string Slug);
