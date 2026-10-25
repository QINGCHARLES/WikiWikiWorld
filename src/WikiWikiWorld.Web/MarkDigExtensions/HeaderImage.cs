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

	/// <summary>
	/// Gets or initializes the normalized vertical focus ("top", "center" or "bottom") from the optional
	/// <c>Focus=</c> property, or <see langword="null"/> when absent or unrecognised (the hero then stays top-aligned).
	/// </summary>
	public string? Focus { get; init; }

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

		// The first segment is the file slug; any further "|#|" segments are Key=Value properties.
		string[] Segments = Slice.Text[Slice.Start..EndPos].Split("|#|", StringSplitOptions.TrimEntries);
		string? Focus = null;
		foreach (string Segment in Segments.Skip(1))
		{
			int EqualsIndex = Segment.IndexOf('=');
			if (EqualsIndex > 0 && Segment[..EqualsIndex].Trim().Equals("Focus", StringComparison.OrdinalIgnoreCase))
			{
				Focus = NormalizeFocus(Segment[(EqualsIndex + 1)..]);
			}
		}

		HeaderImageBlock Block = new(this)
		{
			UrlSlug = Segments[0],
			Focus = Focus,
			Column = Processor.Column,
			Span = new SourceSpan(Processor.LineIndex, Processor.LineIndex)
		};

		Processor.NewBlocks.Push(Block);
		Slice.Start = EndPos + MarkerEnd.Length;

		return BlockState.BreakDiscard;
	}

	/// <summary>
	/// Maps an author's focus value to the token the view and stylesheet understand, accepting the British "centre".
	/// </summary>
	/// <param name="Value">The raw <c>Focus=</c> value.</param>
	/// <returns>"top", "center" or "bottom"; <see langword="null"/> for anything else, so the default framing applies.</returns>
	private static string? NormalizeFocus(string Value) => Value.Trim().ToLowerInvariant() switch
	{
		"top" => "top",
		"center" or "centre" => "center",
		"bottom" => "bottom",
		_ => null
	};
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

	/// <summary>
	/// The key used to store the chosen header image's vertical focus in the document metadata, when one was given.
	/// </summary>
	public const string FocusDocumentKey = "HeaderImageFocus";

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

		// 5. Batch Query Files (Projection)
		var Files = await Context.FileRevisions
			.AsNoTracking()
			.Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
			.Select(f => new { f.Id, f.CanonicalFileId, f.Filename, f.ImageWidthPixels, f.ImageHeightPixels })
			.ToListAsync(CancellationToken);

		Dictionary<Guid, (int Id, string Filename)> FileLookup = Files.ToDictionary(f => f.CanonicalFileId, f => (f.Id, f.Filename));

		string? FirstFoundHeaderImage = null;
		string? FirstFoundHeaderFocus = null;

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
				FileLookup.TryGetValue(CanonicalFileId.Value, out (int Id, string Filename) FileInfo))
			{
				string Url = ImageUrlHelper.BuildImageUrl(SiteId, CanonicalFileId.Value, FileInfo.Id, FileInfo.Filename);

				// Update block for debug renderer
				Block.ResolvedUrl = Url;

				// Capture the first valid image for the page hero, with that same block's focus
				if (FirstFoundHeaderImage is null)
				{
					FirstFoundHeaderImage = Url;
					FirstFoundHeaderFocus = Block.Focus;
				}
			}
		}

		// Store result in Document Metadata for the ViewModel
		if (FirstFoundHeaderImage is not null)
		{
			Document.SetData(DocumentKey, FirstFoundHeaderImage);
			if (FirstFoundHeaderFocus is not null)
			{
				Document.SetData(FocusDocumentKey, FirstFoundHeaderFocus);
			}
		}
	}
}
