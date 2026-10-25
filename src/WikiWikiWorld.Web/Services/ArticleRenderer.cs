using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Renders article markdown with all wiki directive enrichment and extracted page metadata.
/// </summary>
public interface IArticleRenderer
{
	/// <summary>
	/// Renders an article revision to HTML and extracts metadata from its wiki directives.
	/// </summary>
	/// <param name="Revision">The article revision to render.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="EditUrl">The article edit URL used by stub banners.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <param name="CategoryPage">One-based page of indexed category members.</param>
	/// <returns>The rendered article content and extracted metadata.</returns>
	Task<RenderedArticle> RenderAsync(
		ArticleRevision Revision,
		int SiteId,
		string Culture,
		string EditUrl,
		CancellationToken CancellationToken, int CategoryPage = 1);
}

/// <summary>
/// Contains rendered article HTML and metadata extracted during markdown enrichment.
/// </summary>
public sealed record class RenderedArticle
{
	/// <summary>
	/// Gets the rendered article HTML.
	/// </summary>
	public required string Html { get; init; }

	/// <summary>
	/// Gets the categories extracted from the article markdown.
	/// </summary>
	public required IReadOnlyList<Category> Categories { get; init; }

	/// <summary>
	/// Gets the resolved header image URL, if present.
	/// </summary>
	public string? HeaderImage { get; init; }

	/// <summary>
	/// Gets the header image's vertical focus ("top", "center" or "bottom"), or <see langword="null"/> for the default top framing.
	/// </summary>
	public string? HeaderImageFocus { get; init; }

	/// <summary>
	/// Gets the resolved meta description, if present.
	/// </summary>
	public string? MetaDescription { get; init; }

	/// <summary>
	/// Gets the resolved eyebrow label, if present.
	/// </summary>
	public string? EyebrowLabel { get; init; }

	/// <summary>
	/// Gets the resolved subtitle text, if present.
	/// </summary>
	public string? SubtitleText { get; init; }

	/// <summary>
	/// Gets the file source text, if present.
	/// </summary>
	public string? FileSourceText { get; init; }

	/// <summary>
	/// Gets the file source URL, if present.
	/// </summary>
	public string? FileSourceUrl { get; init; }

	/// <summary>
	/// Gets a value indicating whether the file source URL is an approved external domain.
	/// </summary>
	public bool IsFileSourceApprovedDomain { get; init; }
}

/// <summary>
/// Default implementation of <see cref="IArticleRenderer"/> for article markdown.
/// </summary>
public sealed class ArticleRenderer(
	WikiWikiWorldDbContext Context,
	IMarkdownPipelineFactory MarkdownPipelineFactory,
	IOptions<SiteConfiguration> SiteConfiguration) : IArticleRenderer
{
	/// <inheritdoc/>
	public async Task<RenderedArticle> RenderAsync(
		ArticleRevision Revision,
		int SiteId,
		string Culture,
		string EditUrl,
		CancellationToken CancellationToken, int CategoryPage = 1)
	{
		MarkdownPipeline Pipeline = MarkdownPipelineFactory.GetPipeline();
		MarkdownDocument Document = Markdown.Parse(Revision.Text, Pipeline);

		ShortDescriptionExtension.Enrich(Document);
		EyebrowExtension.Enrich(Document);
		SubtitleExtension.Enrich(Document);
		SourceExtension.Enrich(Document);
		await CategoryArticlesExtension.EnrichAsync(Document, Context, Revision, SiteId, Culture, CategoryPage, CancellationToken);
		await ImageExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
		await HeaderImageExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
		await CoverGridExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
		await CoverCarouselExtension.EnrichAsync(Document, Context, SiteId, Culture, CancellationToken);
		await DownloadsBoxExtension.EnrichAsync(Document, Context, SiteId, CancellationToken);
		SiteInfo? CurrentSite = SiteConfiguration.Value.Sites.FirstOrDefault(Site => Site.SiteId == SiteId);
		string SiteName = CurrentSite?.SiteName ?? string.Empty;
		StubExtension.Enrich(Document, SiteName, EditUrl);

		IReadOnlyList<Category> Categories = CategoryExtension.GetCategories(Document);

		FootnoteExtension.ReprocessFootnotes(Document, Pipeline);
		CitationExtension.ReprocessCitations(Document);

		string? FileSourceUrl = Document.GetData(SourceExtension.UrlKey) as string;

		return new RenderedArticle
		{
			Html = CleanupAndStyleArticle(RenderHtml(Document, Pipeline)),
			Categories = Categories,
			HeaderImage = Document.GetData(HeaderImageExtension.DocumentKey) as string,
			HeaderImageFocus = Document.GetData(HeaderImageExtension.FocusDocumentKey) as string,
			MetaDescription = Document.GetData(ShortDescriptionExtension.DocumentKey) as string,
			EyebrowLabel = Document.GetData(EyebrowExtension.DocumentKey) as string,
			SubtitleText = Document.GetData(SubtitleExtension.DocumentKey) as string,
			FileSourceText = Document.GetData(SourceExtension.TextKey) as string,
			FileSourceUrl = FileSourceUrl,
			IsFileSourceApprovedDomain = FileSourceUrl is not null
				&& LinkDomainHelper.IsApprovedDomain(FileSourceUrl, SiteConfiguration.Value.ApprovedLinkDomains)
		};
	}

	/// <summary>
	/// Renders a parsed markdown document to HTML.
	/// </summary>
	/// <param name="Document">The parsed markdown document.</param>
	/// <param name="Pipeline">The markdown pipeline used to parse the document.</param>
	/// <returns>The rendered HTML.</returns>
	private static string RenderHtml(MarkdownDocument Document, MarkdownPipeline Pipeline)
	{
		StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		Writer.Flush();

		return Writer.ToString();
	}

	/// <summary>
	/// Cleans up and styles the article HTML content.
	/// </summary>
	/// <param name="Html">The rendered HTML content.</param>
	/// <returns>The cleaned and styled HTML.</returns>
	private static string CleanupAndStyleArticle(string Html)
	{
		Html = Regex.Replace(
			Html,
			@"<p>\s*</p>",
			string.Empty,
			RegexOptions.IgnoreCase);

		int LastParagraphClose = Html.LastIndexOf("</p>", StringComparison.OrdinalIgnoreCase);
		if (LastParagraphClose < 0)
		{
			return Html;
		}

		int SearchStart = LastParagraphClose - 1;
		while (SearchStart >= 0)
		{
			char c = Html[SearchStart];
			if (c == '.')
			{
				return string.Concat(
					Html.AsSpan(0, SearchStart),
					"<span class=\"article-end-mark\">.</span>",
					Html.AsSpan(SearchStart + 1));
			}

			if (char.IsLetterOrDigit(c) || c == '"' || c == '\'' || c == ')' || c == ']')
			{
				break;
			}

			SearchStart--;
		}

		return Html;
	}
}
