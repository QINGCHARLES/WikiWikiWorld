using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>Lists indexed members of the category containing this directive.</summary>
public sealed class CategoryArticlesExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline) => Pipeline.BlockParsers.Add(new CategoryArticlesParser());
	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html) Html.ObjectRenderers.Add(new CategoryArticlesRenderer());
	}

	/// <summary>Loads one ordered page from the membership index, only when the directive is present on a category page.</summary>
	/// <param name="Document">Parsed article source.</param>
	/// <param name="Context">The request database context; the query explicitly applies site, culture, and visibility scope.</param>
	/// <param name="Revision">The category page whose slug identifies the category.</param>
	/// <param name="SiteId">Required site scope.</param>
	/// <param name="Culture">Required language scope.</param>
	/// <param name="Page">Requested one-based page; invalid ranges are clamped.</param>
	/// <param name="CancellationToken">Request cancellation.</param>
	/// <returns>A task completing when all matching blocks share the loaded page.</returns>
	public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context,
		ArticleRevision Revision, int SiteId, string Culture, int Page, CancellationToken CancellationToken)
	{
		List<CategoryArticlesBlock> Blocks = [.. Document.Descendants<CategoryArticlesBlock>()];
		if (Blocks.Count == 0 || Revision.Type != ArticleType.Category) return;
		string Slug = Revision.UrlSlug.StartsWith("category:", StringComparison.OrdinalIgnoreCase)
			? Revision.UrlSlug[9..] : Revision.UrlSlug;
		// SQLite reordered LINQ joins to scan the site's revisions first. CROSS JOIN fixes
		// the lookup order: category index, then revision primary key. Scope is explicit.
		// Named parameters avoid collisions with EF's generated paging parameter names.
		SqliteParameter SlugParameter = new("CategorySlug", Slug);
		SqliteParameter SiteParameter = new("CategorySiteId", SiteId);
		SqliteParameter CultureParameter = new("CategoryCulture", Culture);
		IQueryable<CategoryArticleLink> Query = Context.Database.SqlQuery<CategoryArticleLink>($"""
			SELECT r.Id, r.Title, r.UrlSlug AS Slug, r.Type
			FROM ArticleCategoryMemberships AS m
			CROSS JOIN ArticleRevisions AS r ON r.Id = m.ArticleRevisionId
			WHERE m.CategorySlug = {SlugParameter} AND r.SiteId = {SiteParameter} AND r.Culture = {CultureParameter}
			  AND r.IsCurrent = 1 AND r.DateDeleted IS NULL
			""");
		int Count = await Query.CountAsync(CancellationToken);
		int PageCount = Math.Max(1, (int)Math.Ceiling(Count / 100d));
		Page = Math.Clamp(Page, 1, PageCount);
		List<CategoryArticleLink> Items = await Query.OrderBy(r => r.Title).ThenBy(r => r.Id)
			.Skip((Page - 1) * 100).Take(100).ToListAsync(CancellationToken);
		foreach (CategoryArticlesBlock Block in Blocks)
		{
			Block.Items = Items;
			Block.Page = Page;
			Block.PageCount = PageCount;
			Block.Path = ArticleUrlHelper.BuildArticlePath(Revision);
		}
	}
}

/// <summary>A plain-text title and routing metadata, without article body content.</summary>
/// <param name="Id">Revision identifier used to break ordering ties.</param>
/// <param name="Title">Plain-text article title.</param>
/// <param name="Slug">Stored article slug.</param>
/// <param name="Type">Namespace used by canonical routing.</param>
public sealed record CategoryArticleLink(int Id, string Title, string Slug, ArticleType Type);

/// <summary>A standalone category listing marker and its request-specific results.</summary>
/// <param name="Parser">Parser creating this node.</param>
public sealed class CategoryArticlesBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>Gets or sets the bounded list of current members.</summary>
	public IReadOnlyList<CategoryArticleLink> Items { get; set; } = [];
	/// <summary>Gets or sets the canonical category path; null indicates unsupported placement.</summary>
	public string? Path { get; set; }
	/// <summary>Gets or sets the clamped one-based page number.</summary>
	public int Page { get; set; }
	/// <summary>Gets or sets the number of available pages, with a minimum of one.</summary>
	public int PageCount { get; set; }
}

/// <summary>Recognizes only the complete standalone CategoryArticles marker, preserving malformed input.</summary>
public sealed class CategoryArticlesParser : BlockParser
{
	/// <summary>Registers the opening brace as the marker trigger.</summary>
	public CategoryArticlesParser() => OpeningCharacters = ['{'];
	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (Processor.IsCodeIndent || Processor.Line.ToString().Trim() != "{{CategoryArticles}}") return BlockState.None;
		Processor.NewBlocks.Push(new CategoryArticlesBlock(this));
		return BlockState.BreakDiscard;
	}
}

/// <summary>Writes escaped article links and ordinary server-side pagination without JavaScript.</summary>
public sealed class CategoryArticlesRenderer : HtmlObjectRenderer<CategoryArticlesBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CategoryArticlesBlock Block)
	{
		if (Block.Path is null)
		{
			Renderer.WriteLine("<p>{{CategoryArticles}} is available on category articles.</p>");
			return;
		}
		Renderer.WriteLine("<div class=\"category-articles\"><h2>Articles in this category</h2>");
		if (Block.Items.Count == 0) Renderer.WriteLine("<p>No articles are currently listed in this category.</p>");
		else
		{
			Renderer.WriteLine("<ul>");
			foreach (CategoryArticleLink Item in Block.Items)
			{
				Renderer.Write("<li><a href=\"");
				Renderer.WriteEscapeUrl(ArticleUrlHelper.BuildArticlePath(Item.Slug, Item.Type));
				Renderer.Write("\">");
				Renderer.WriteEscape(Item.Title);
				Renderer.WriteLine("</a></li>");
			}
			Renderer.WriteLine("</ul>");
		}
		if (Block.PageCount > 1)
		{
			Renderer.WriteLine("<nav aria-label=\"Category articles\">");
			if (Block.Page > 1) WritePageLink(Renderer, Block.Path, Block.Page - 1, "Previous");
			Renderer.Write(FormattableString.Invariant($" <span>Page {Block.Page} of {Block.PageCount}</span> "));
			if (Block.Page < Block.PageCount) WritePageLink(Renderer, Block.Path, Block.Page + 1, "Next");
			Renderer.WriteLine("</nav>");
		}
		Renderer.WriteLine("</div>");
	}

	/// <summary>Writes a navigation link with an invariant page number and escaped URL.</summary>
	/// <param name="Renderer">HTML writer.</param>
	/// <param name="Path">Canonical category path.</param>
	/// <param name="Page">Destination page number.</param>
	/// <param name="Label">Visible navigation label.</param>
	private static void WritePageLink(HtmlRenderer Renderer, string Path, int Page, string Label)
	{
		Renderer.Write("<a href=\"");
		Renderer.WriteEscapeUrl(Path + "?CategoryPage=" + Page.ToString(CultureInfo.InvariantCulture));
		Renderer.Write("\">");
		Renderer.WriteEscape(Label);
		Renderer.Write("</a>");
	}
}
