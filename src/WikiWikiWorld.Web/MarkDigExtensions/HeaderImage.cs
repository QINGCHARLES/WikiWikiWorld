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
/// A block element representing the header image of an article.
/// </summary>
/// <param name="Parser">The block parser.</param>
public sealed class HeaderImageBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets or initializes the URL slug for the header image.
	/// </summary>
	public required string UrlSlug { get; init; }

	// Stored here during Enrichment; retrieved by the Renderer (debug) 
	// or the PageModel (via Document.SetData)
	/// <summary>
	/// Gets or sets the resolved URL of the image after enrichment.
	/// </summary>
	public string? ResolvedUrl { get; set; }
}

/// <summary>
/// Parses the {{HeaderImage ...}} block syntax.
/// </summary>
public sealed class HeaderImageBlockParser : BlockParser
{
	private const string MarkerStart = "{{HeaderImage ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="HeaderImageBlockParser"/> class.
	/// </summary>
	public HeaderImageBlockParser()
	{
		OpeningCharacters = ['{'];
	}

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		int StartPosition = Slice.Start;

		if (!Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);

		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return BlockState.None;
		}

		string UrlSlug = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();

		HeaderImageBlock Block = new(this)
		{
			UrlSlug = UrlSlug,
			Column = Processor.Column,
			Span = new SourceSpan(Processor.LineIndex, Processor.LineIndex)
		};

		Processor.NewBlocks.Push(Block);
		Slice.Start = EndPos + MarkerEnd.Length;

		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Renders a diagnostic comment for the <see cref="HeaderImageBlock"/>.
/// </summary>
public sealed class HeaderImageBlockRenderer : HtmlObjectRenderer<HeaderImageBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, HeaderImageBlock Block)
	{
		// We output a hidden comment for debugging.
		// The actual image is rendered by the Razor Page layout using the Document Metadata.
		if (Renderer.EnableHtmlForBlock)
		{
			Renderer.Write(Block.ResolvedUrl is not null
				? $"<!-- Header Image Resolved: {Block.ResolvedUrl} -->"
				: $"<!-- Header Image Not Resolved: {Block.UrlSlug} -->");
		}
	}
}

/// <summary>
/// A Markdig extension that supports article header images.
/// </summary>
public sealed class HeaderImageExtension : IMarkdownExtension
{
	/// <summary>
	/// The key used to store the resolved header image URL in the document metadata.
	/// </summary>
	public const string DocumentKey = "HeaderImage";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<HeaderImageBlockParser>())
		{
			Pipeline.BlockParsers.Add(new HeaderImageBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is HeaderImageBlockRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new HeaderImageBlockRenderer());
		}
	}

	/// <summary>
	/// Scans the document for HeaderImage blocks and resolves their URLs using optimized database projections.
	/// </summary>
	public static async Task EnrichAsync(
		MarkdownDocument Document,
		WikiWikiWorldDbContext Context,
		int SiteId,
		string Culture,
		CancellationToken CancellationToken)
	{
		// 1. Find Blocks
		List<HeaderImageBlock> HeaderBlocks = [.. Document.Descendants<HeaderImageBlock>()];
		
		if (HeaderBlocks.Count == 0)
		{
			return;
		}

		// 2. Gather Slugs
		List<string> Slugs = [.. HeaderBlocks
			.Select(b =>
			{
				string S = b.UrlSlug;
				if (S.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
				{
					S = S["file:".Length..];
				}
				return SlugHelper.GenerateSlug(S);
			})
			.Distinct()];

		// 3. Batch Query Articles (Projection: Fetch ONLY IDs, not full text)
		// We use 'var' because the projection creates an anonymous type.
		var Articles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(x => x.SiteId == SiteId && x.Culture == Culture && Slugs.Contains(x.UrlSlug) && x.IsCurrent)
			.Select(x => new { x.UrlSlug, x.CanonicalFileId }) 
			.ToListAsync(CancellationToken);

		Dictionary<string, Guid?> ArticleLookup = Articles
			.ToDictionary(a => a.UrlSlug, a => a.CanonicalFileId, StringComparer.OrdinalIgnoreCase);

		// 4. Gather File IDs
		List<Guid> FileIds = [.. Articles
			.Where(a => a.CanonicalFileId.HasValue)
			.Select(a => a.CanonicalFileId!.Value)
			.Distinct()];

		// 5. Batch Query Files (Projection: Fetch ONLY Filenames)
		var Files = await Context.FileRevisions
			.AsNoTracking()
			.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
			.Select(f => new { f.CanonicalFileId, f.Filename })
			.ToListAsync(CancellationToken);

		Dictionary<Guid, string> FileLookup = Files.ToDictionary(f => f.CanonicalFileId, f => f.Filename);

		string? FirstFoundHeaderImage = null;

		// 6. Update Blocks & Document
		foreach (HeaderImageBlock Block in HeaderBlocks)
		{
			string Slug = Block.UrlSlug;
			if (Slug.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
			{
				Slug = Slug["file:".Length..];
			}
			Slug = SlugHelper.GenerateSlug(Slug);

			if (ArticleLookup.TryGetValue(Slug, out Guid? CanonicalFileId) &&
				CanonicalFileId.HasValue &&
				FileLookup.TryGetValue(CanonicalFileId.Value, out string? Filename))
			{
				string Url = $"/sitefiles/{SiteId}/images/{CanonicalFileId}{Path.GetExtension(Filename)}";

				// Update block for debug renderer
				Block.ResolvedUrl = Url;

				// Capture the first valid image for the page hero
				FirstFoundHeaderImage ??= Url;
			}
		}

		// Store result in Document Metadata for the ViewModel
		if (FirstFoundHeaderImage is not null)
		{
			Document.SetData(DocumentKey, FirstFoundHeaderImage);
		}
	}
}
