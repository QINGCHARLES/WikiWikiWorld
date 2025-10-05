namespace WikiWikiWorld.Web.Pages;

public sealed class IndexModel(
	IArticleRevisionRepository ArticleRevisionRepository,
	IFileRevisionRepository FileRevisionRepository,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	public List<ArticleWithCover> ArticlesWithCovers { get; private set; } = [];

	public async Task OnGetAsync()
	{
		// Fetch the latest articles with publication issue infoboxes (ordered by DateCreated DESC)
		IReadOnlyList<ArticleRevision> Articles = await ArticleRevisionRepository
			.GetLatestArticlesWithPublicationIssueInfoboxAsync(SiteId, Culture, 50);

		// Extract cover images for each article, preserving the date order
		foreach (ArticleRevision Article in Articles)
		{
			string? CoverImageSlug = ExtractCoverImageFromInfobox(Article.Text);
			if (string.IsNullOrEmpty(CoverImageSlug))
			{
				continue;
			}

			// Fetch the cover image article
			ArticleRevision? CoverArticle = await ArticleRevisionRepository
				.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, CoverImageSlug);

			if (CoverArticle?.CanonicalFileId is null)
			{
				continue;
			}

			// Fetch the file revision
			FileRevision? File = await FileRevisionRepository
				.GetCurrentByCanonicalFileIdAsync(CoverArticle.CanonicalFileId.Value);

			if (File is null)
			{
				continue;
			}

			string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";
			ArticlesWithCovers.Add(new ArticleWithCover(
				Article.Title,
				Article.UrlSlug,
				ImageUrl,
				Article.DateCreated
			));
		}
	}

	private static string? ExtractCoverImageFromInfobox(string Text)
	{
		// Find the start of the infobox
		int Start = Text.IndexOf("{{PublicationIssueInfobox", StringComparison.OrdinalIgnoreCase);
		if (Start == -1)
		{
			return null;
		}

		// Find the end of the infobox
		int End = Text.IndexOf("}}", Start, StringComparison.Ordinal);
		if (End == -1)
		{
			return null;
		}

		// Extract the infobox content
		string InfoboxContent = Text[Start..(End + 2)];

		// Find CoverImage property
		int CoverImagePos = InfoboxContent.IndexOf("CoverImage=", StringComparison.OrdinalIgnoreCase);
		if (CoverImagePos == -1)
		{
			return null;
		}

		// Extract from after "CoverImage=" to the next separator or end
		int ValueStart = CoverImagePos + "CoverImage=".Length;
		int ValueEnd = InfoboxContent.IndexOf("|#|", ValueStart, StringComparison.Ordinal);

		if (ValueEnd == -1)
		{
			// If no separator found, extract until the end marker
			ValueEnd = InfoboxContent.IndexOf("}}", ValueStart, StringComparison.Ordinal);
		}

		if (ValueEnd != -1)
		{
			string Value = InfoboxContent[ValueStart..ValueEnd].Trim();

			// Handle "file:" prefix
			if (Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			{
				Value = Value["file:".Length..];
			}

			return Value;
		}

		return null;
	}
}

public sealed record ArticleWithCover(string Title, string UrlSlug, string ImageUrl, DateTimeOffset DateCreated);
