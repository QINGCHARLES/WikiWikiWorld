using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A block element representing a grid of cover images for a set of articles.
/// </summary>
/// <param name="Parser">The block parser used to create this block.</param>
public sealed class CoverGridBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets the raw content of the block (the list of slugs).
	/// </summary>
	public StringBuilder RawContent { get; } = new();

	/// <summary>
	/// Gets the list of parsed URL slugs from the content.
	/// </summary>
	public List<string> UrlSlugs { get; } = [];

	/// <summary>
	/// Gets or sets the cached cover image data (URL, Title, Slug).
	/// </summary>
	public List<(string ImageUrl, string Title, string UrlSlug)> CachedCoverImages { get; set; } = [];

	/// <summary>
	/// Records the culture supplied during enrichment; article links use the current host's culture.
	/// </summary>
	public string Culture { get; set; } = "en";
}

/// <summary>
/// Parses the <c>{{CoverGrid}}</c> block marker.
/// </summary>
public sealed class CoverGridBlockParser : BlockParser
{
	private const string MarkerStart = "{{CoverGrid";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="CoverGridBlockParser"/> class.
	/// </summary>
	public CoverGridBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		if (Processor.IsCodeIndent || !Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		int Start = Slice.Start + MarkerStart.Length;
		if (Start <= Slice.End && !char.IsWhiteSpace(Slice.Text[Start]) &&
			!Slice.Text.AsSpan(Start).StartsWith(MarkerEnd, StringComparison.Ordinal))
		{
			return BlockState.None;
		}

		CoverGridBlock CoverBlock = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column
		};

		Slice.Start = Start;
		string Remaining = Slice.ToString();
		if (!string.IsNullOrWhiteSpace(Remaining))
		{
			int EndPos = Remaining.IndexOf(MarkerEnd, StringComparison.Ordinal);
			if (EndPos != -1)
			{
				CoverBlock.RawContent.AppendLine(Remaining[..EndPos]);
				Processor.NewBlocks.Push(CoverBlock);
				return BlockState.BreakDiscard;
			}
			CoverBlock.RawContent.AppendLine(Remaining);
		}

		Processor.NewBlocks.Push(CoverBlock);
		return BlockState.ContinueDiscard;
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
	{
		StringSlice Slice = Processor.Line;
		string Line = Slice.ToString();
		int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
		CoverGridBlock CoverBlock = CurrentBlock as CoverGridBlock ?? throw new InvalidOperationException("Block is not a CoverGridBlock.");

		if (EndPos != -1)
		{
			CoverBlock.RawContent.AppendLine(Line[..EndPos]);
			return BlockState.BreakDiscard;
		}
		CoverBlock.RawContent.AppendLine(Line);
		return BlockState.ContinueDiscard;
	}
}

/// <summary>
/// Renders a <see cref="CoverGridBlock"/> as a responsive grid of images.
/// </summary>
public sealed class CoverGridRenderer : HtmlObjectRenderer<CoverGridBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CoverGridBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		if (Block.CachedCoverImages.Count == 0)
		{
			return;
		}

		RenderCoverGrid(Renderer, Block.CachedCoverImages);
	}

	/// <summary>
	/// Renders linked cover figures and captions with root-relative paths, preserving the current culture subdomain.
	/// </summary>
	/// <param name="Renderer">The HTML renderer.</param>
	/// <param name="CoverImages">The list of cover images with URL, title, and slug.</param>
	private static void RenderCoverGrid(HtmlRenderer Renderer, List<(string ImageUrl, string Title, string UrlSlug)> CoverImages)
	{
		Renderer.Write("<div class=\"cover-grid\">");

		foreach ((string ImageUrl, string Title, string UrlSlug) in CoverImages)
		{
			Renderer.Write("<a href=\"/")
				.WriteEscapeUrl(UrlSlug)
				.Write("\"><figure>");
			Renderer.Write("<img src=\"")
				.WriteEscapeUrl(ImageUrl)
				.Write("\" alt=\"")
				.WriteEscape(Title)
				.Write("\" title=\"")
				.WriteEscape(Title)
				.Write("\" />");
			Renderer.Write("<figcaption>")
				.WriteEscape(Title)
				.Write("</figcaption></figure></a>");
		}

		Renderer.Write("</div>");
	}
}

/// <summary>
/// A Markdig extension that adds support for displaying a grid of article covers.
/// </summary>
public sealed class CoverGridExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<CoverGridBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new CoverGridBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(R => R is CoverGridRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new CoverGridRenderer());
		}
	}

	/// <summary>
	/// Asynchronously populates the cover image data for cover grid blocks in the document.
	/// </summary>
	/// <param name="Document">The markdown document to process.</param>
	/// <param name="Context">The database context (for fetching article and file info).</param>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="CancellationToken">A token to cancel the operation.</param>
	public static async Task EnrichAsync(MarkdownDocument Document, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken = default)
	{
		List<CoverGridBlock> GridBlocks = [.. Document.Descendants<CoverGridBlock>()];

		if (GridBlocks.Count == 0) return;

		List<string> AllUrlSlugs = [];

		foreach (CoverGridBlock Block in GridBlocks)
		{
			Block.Culture = Culture;
			string Raw = Block.RawContent.ToString();
			string[] Lines = Raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
			foreach (string Line in Lines)
			{
				string NormalizedSlug = SlugHelper.GenerateSlug(Line.Trim());
				if (!string.IsNullOrEmpty(NormalizedSlug))
				{
					Block.UrlSlugs.Add(NormalizedSlug);
					AllUrlSlugs.Add(NormalizedSlug);
				}
			}
		}

		if (AllUrlSlugs.Count == 0) return;

		List<string> DistinctUrlSlugs = [.. AllUrlSlugs.Distinct(StringComparer.OrdinalIgnoreCase)];

		var Articles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(x => x.SiteId == SiteId && x.Culture == Culture && DistinctUrlSlugs.Contains(x.UrlSlug) && x.IsCurrent && x.DateDeleted == null)
			.Select(x => new { x.UrlSlug, x.Title, x.Text })
			.ToListAsync(CancellationToken);

		Dictionary<string, (string Title, string Text)> ArticleLookup = Articles.ToDictionary(
			a => a.UrlSlug,
			a => (a.Title, a.Text),
			StringComparer.OrdinalIgnoreCase);

		List<string> CoverImageSlugs = [];
		Dictionary<string, string> ArticleToCoverSlug = new(StringComparer.OrdinalIgnoreCase);

		foreach (var Article in Articles)
		{
			string? RawCoverSlug = ArticleCoverReference.Extract(Article.Text);
			if (!string.IsNullOrEmpty(RawCoverSlug))
			{
				string NormalizedCoverSlug = SlugHelper.GenerateSlug(RawCoverSlug);
				if (!string.IsNullOrEmpty(NormalizedCoverSlug))
				{
					CoverImageSlugs.Add(NormalizedCoverSlug);
					ArticleToCoverSlug[Article.UrlSlug] = NormalizedCoverSlug;
				}
			}
		}

		Dictionary<string, Guid> CoverArticleLookup = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<Guid, (int Id, string Filename)> FileLookup = [];

		if (CoverImageSlugs.Count > 0)
		{
			List<string> DistinctCoverSlugs = [.. CoverImageSlugs.Distinct(StringComparer.OrdinalIgnoreCase)];

			var CoverArticles = await Context.ArticleRevisions
				.AsNoTracking()
				.Where(x => x.SiteId == SiteId && x.Culture == Culture && DistinctCoverSlugs.Contains(x.UrlSlug) && x.IsCurrent && x.DateDeleted == null && x.CanonicalFileId.HasValue)
				.Select(x => new { x.UrlSlug, CanonicalFileId = x.CanonicalFileId!.Value })
				.ToListAsync(CancellationToken);

			CoverArticleLookup = CoverArticles.ToDictionary(
				a => a.UrlSlug,
				a => a.CanonicalFileId,
				StringComparer.OrdinalIgnoreCase);

			List<Guid> FileIds = [.. CoverArticles
				.Select(a => a.CanonicalFileId)
				.Distinct()];

			var Files = await Context.FileRevisions
				.AsNoTracking()
				.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
				.Select(f => new { f.CanonicalFileId, f.Id, f.Filename })
				.ToListAsync(CancellationToken);

			FileLookup = Files.ToDictionary(
				f => f.CanonicalFileId,
				f => (f.Id, f.Filename));
		}

		foreach (CoverGridBlock Block in GridBlocks)
		{
			foreach (string UrlSlug in Block.UrlSlugs)
			{
				if (!ArticleLookup.TryGetValue(UrlSlug, out (string Title, string Text) Article))
				{
					Block.CachedCoverImages.Add(($"/sitefiles/{SiteId}/cover-missing.png", "Missing Article", UrlSlug));
					continue;
				}

				if (ArticleToCoverSlug.TryGetValue(UrlSlug, out string? CoverSlug) &&
					CoverArticleLookup.TryGetValue(CoverSlug, out Guid CanonicalFileId) &&
					FileLookup.TryGetValue(CanonicalFileId, out (int Id, string Filename) File))
				{
					string ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, CanonicalFileId, File.Id, File.Filename);
					Block.CachedCoverImages.Add((ImageUrl, Article.Title ?? "Title", UrlSlug));
				}
				else
				{
					Block.CachedCoverImages.Add(($"/sitefiles/{SiteId}/cover-missing.png", Article.Title ?? "Title", UrlSlug));
				}
			}
		}
	}
}
