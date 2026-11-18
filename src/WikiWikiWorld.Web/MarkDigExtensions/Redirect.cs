using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.EntityFrameworkCore;
using System.Net;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Represents a <c>{{Redirect target}}</c> block directive in Markdown.
/// </summary>
public sealed class RedirectBlock : LeafBlock
{
	/// <summary>
	/// Gets or sets the normalized destination URL slug.
	/// </summary>
	public string TargetSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional anchor fragment without the leading <c>#</c>.
	/// </summary>
	public string? Fragment { get; set; }

	/// <summary>
	/// Gets or sets the raw target text authored inside the directive.
	/// </summary>
	public string RawTarget { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether the authored target passed syntax validation.
	/// </summary>
	public bool IsValid { get; set; }

	/// <summary>
	/// Gets or sets the resolved destination URL path including any fragment, populated during enrichment.
	/// </summary>
	public string? ResolvedPath { get; set; }

	/// <summary>
	/// Gets or sets the resolved destination article title, or <see langword="null"/> when no matching target was found.
	/// </summary>
	public string? ResolvedTitle { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the resolved target exists as a current revision.
	/// </summary>
	public bool TargetExists { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the resolved target is itself a redirect page.
	/// </summary>
	public bool TargetIsRedirect { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="RedirectBlock"/> class.
	/// </summary>
	/// <param name="Parser">The block parser that created this block.</param>
	public RedirectBlock(BlockParser Parser)
		: base(Parser)
	{
	}
}

/// <summary>
/// Parses <c>{{Redirect ...}}</c> block directives on their own line.
/// </summary>
public sealed class RedirectBlockParser : BlockParser
{
	private const string Prefix = "{{Redirect ";
	private const string Suffix = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="RedirectBlockParser"/> class.
	/// </summary>
	public RedirectBlockParser()
	{
		OpeningCharacters = ['{'];
	}

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (Processor.IsCodeIndent)
		{
			return BlockState.None;
		}

		StringSlice Slice = Processor.Line;
		string Text = Slice.Text;
		int Start = Slice.Start;
		int RemainingLength = Slice.End - Start + 1;

		if (RemainingLength < Prefix.Length + Suffix.Length)
		{
			return BlockState.None;
		}

		ReadOnlySpan<char> Remaining = Text.AsSpan(Start, RemainingLength);
		if (!Remaining.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
		{
			return BlockState.None;
		}

		int CloseIndex = Remaining[Prefix.Length..].IndexOf(Suffix, StringComparison.Ordinal);
		if (CloseIndex < 0)
		{
			return BlockState.None;
		}

		ReadOnlySpan<char> Trailing = Remaining[(Prefix.Length + CloseIndex + Suffix.Length)..].Trim();
		if (!Trailing.IsEmpty)
		{
			return BlockState.None;
		}

		string RawTarget = Remaining.Slice(Prefix.Length, CloseIndex).ToString().Trim();
		bool IsValid = RedirectExtension.TryParseTarget(RawTarget, out string TargetSlug, out string? Fragment);

		RedirectBlock Block = new(this)
		{
			RawTarget = RawTarget,
			TargetSlug = TargetSlug,
			Fragment = Fragment,
			IsValid = IsValid
		};

		Processor.NewBlocks.Push(Block);
		Processor.Line.Start = Processor.Line.End + 1;

		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Renders <see cref="RedirectBlock"/> directives as a status notice when viewed directly.
/// </summary>
public sealed class RedirectRenderer : HtmlObjectRenderer<RedirectBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, RedirectBlock Block)
	{
		if (!Block.IsValid || string.IsNullOrWhiteSpace(Block.TargetSlug))
		{
			Renderer.WriteLine("<div class=\"revision-notice redirect-notice\">");
			Renderer.WriteLine("<strong>Redirect page:</strong> Invalid redirect target directive.");
			Renderer.WriteLine("</div>");
			return;
		}

		string DefaultPath = ArticleUrlHelper.BuildArticlePath(Block.TargetSlug, ArticleType.Article);
		if (!string.IsNullOrEmpty(Block.Fragment))
		{
			DefaultPath = $"{DefaultPath}#{Block.Fragment}";
		}

		string Href = WebUtility.HtmlEncode(Block.ResolvedPath ?? DefaultPath);
		string Label = WebUtility.HtmlEncode(!string.IsNullOrWhiteSpace(Block.ResolvedTitle)
			? Block.ResolvedTitle
			: Block.TargetSlug);

		Renderer.WriteLine("<div class=\"revision-notice redirect-notice\">");
		Renderer.Write($"<strong>Redirect page:</strong> This page redirects to <a href=\"{Href}\">{Label}</a>");
		if (Block.TargetIsRedirect)
		{
			Renderer.Write(" (target is also a redirect and cannot be chained)");
		}
		else if (!Block.TargetExists)
		{
			Renderer.Write(" (target page does not exist yet)");
		}
		Renderer.WriteLine(".");
		Renderer.WriteLine("</div>");
	}
}

/// <summary>
/// Markdig extension and target-parsing helpers for <c>{{Redirect ...}}</c> directives.
/// </summary>
public sealed class RedirectExtension : IMarkdownExtension
{
	/// <summary>
	/// Document data key storing the parsed destination URL slug.
	/// </summary>
	public const string TargetSlugDocumentKey = "RedirectTargetSlug";

	/// <summary>
	/// Document data key storing the optional destination anchor fragment.
	/// </summary>
	public const string FragmentDocumentKey = "RedirectFragment";

	private static readonly Lazy<MarkdownPipeline> RedirectOnlyPipeline = new(() =>
		new MarkdownPipelineBuilder()
			.Use(new RedirectExtension())
			.DisableHtml()
			.Build());

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<RedirectBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new RedirectBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer && !HtmlRenderer.ObjectRenderers.Contains<RedirectRenderer>())
		{
			HtmlRenderer.ObjectRenderers.Insert(0, new RedirectRenderer());
		}
	}

	/// <summary>
	/// Extracts and validates the first valid <c>{{Redirect ...}}</c> target outside code blocks from article markdown.
	/// </summary>
	/// <param name="MarkdownText">The article markdown source.</param>
	/// <param name="TargetSlug">When successful, receives the normalized destination URL slug.</param>
	/// <param name="Fragment">When successful, receives the optional anchor fragment without <c>#</c>.</param>
	/// <returns><see langword="true"/> when a valid redirect directive is found; otherwise <see langword="false"/>.</returns>
	public static bool TryExtractTarget(string? MarkdownText, out string TargetSlug, out string? Fragment)
	{
		TargetSlug = string.Empty;
		Fragment = null;

		if (string.IsNullOrWhiteSpace(MarkdownText))
		{
			return false;
		}

		MarkdownDocument Document = Markdown.Parse(MarkdownText, RedirectOnlyPipeline.Value);
		RedirectBlock? Block = Document.Descendants<RedirectBlock>().FirstOrDefault(B => B.IsValid);
		if (Block is null)
		{
			return false;
		}

		TargetSlug = Block.TargetSlug;
		Fragment = Block.Fragment;
		return true;
	}

	/// <summary>
	/// Parses and validates a raw redirect target string such as <c>1994</c>, <c>/1994#history</c>, or <c>file:cover</c>.
	/// </summary>
	/// <param name="RawTarget">The raw target value inside <c>{{Redirect ...}}</c>.</param>
	/// <param name="TargetSlug">When successful, receives the normalized lookup slug.</param>
	/// <param name="Fragment">When successful, receives the optional sanitized anchor fragment without <c>#</c>.</param>
	/// <returns><see langword="true"/> when the target is a safe, non-empty relative article target; otherwise <see langword="false"/>.</returns>
	public static bool TryParseTarget(string? RawTarget, out string TargetSlug, out string? Fragment)
	{
		TargetSlug = string.Empty;
		Fragment = null;

		if (string.IsNullOrWhiteSpace(RawTarget))
		{
			return false;
		}

		string Trimmed = RawTarget.Trim();
		string SlugPart = Trimmed;
		int HashIndex = Trimmed.IndexOf('#');
		if (HashIndex >= 0)
		{
			SlugPart = Trimmed[..HashIndex].Trim();
			string RawFragment = Trimmed[(HashIndex + 1)..].Trim();
			if (!string.IsNullOrEmpty(RawFragment))
			{
				if (!IsValidFragment(RawFragment))
				{
					return false;
				}
				Fragment = RawFragment;
			}
		}

		SlugPart = SlugPart.TrimStart('/');
		string NormalizedSlug = ArticleUrlHelper.NormalizeLookupSlug(SlugPart);
		if (!IsValidTargetSlug(NormalizedSlug))
		{
			return false;
		}

		TargetSlug = NormalizedSlug;
		return true;
	}

	/// <summary>
	/// Enriches <see cref="RedirectBlock"/> nodes in <paramref name="Document"/> with resolved target metadata
	/// and records the parsed target in document data.
	/// </summary>
	/// <param name="Document">The parsed Markdown document.</param>
	/// <param name="Context">The database context used to check destination existence and canonical path.</param>
	/// <param name="SiteId">The current site ID.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task representing the asynchronous enrichment operation.</returns>
	public static async Task EnrichAsync(
		MarkdownDocument Document,
		WikiWikiWorldDbContext Context,
		int SiteId,
		string Culture,
		CancellationToken CancellationToken)
	{
		List<RedirectBlock> Blocks = [.. Document.Descendants<RedirectBlock>()];
		if (Blocks.Count == 0)
		{
			return;
		}

		RedirectBlock? FirstValid = Blocks.FirstOrDefault(B => B.IsValid);
		if (FirstValid is not null)
		{
			Document.SetData(TargetSlugDocumentKey, FirstValid.TargetSlug);
			if (!string.IsNullOrEmpty(FirstValid.Fragment))
			{
				Document.SetData(FragmentDocumentKey, FirstValid.Fragment);
			}
		}

		HashSet<string> DistinctTargets =
		[
			with(StringComparer.OrdinalIgnoreCase),
			.. Blocks.Where(B => B.IsValid).Select(B => B.TargetSlug)
		];

		if (DistinctTargets.Count == 0)
		{
			return;
		}

		Dictionary<string, (string UrlSlug, ArticleType Type, string Title)> ResolvedTargets = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(R => R.SiteId == SiteId && R.Culture == Culture && R.IsCurrent && DistinctTargets.Contains(R.UrlSlug))
			.Select(R => new { R.UrlSlug, R.Type, R.Title })
			.ToDictionaryAsync(
				R => R.UrlSlug,
				R => (R.UrlSlug, R.Type, R.Title),
				StringComparer.OrdinalIgnoreCase,
				CancellationToken);

		foreach (RedirectBlock Block in Blocks)
		{
			if (!Block.IsValid)
			{
				continue;
			}

			if (ResolvedTargets.TryGetValue(Block.TargetSlug, out (string UrlSlug, ArticleType Type, string Title) Target))
			{
				string Path = ArticleUrlHelper.BuildArticlePath(Target.UrlSlug, Target.Type);
				if (!string.IsNullOrEmpty(Block.Fragment))
				{
					Path = $"{Path}#{Block.Fragment}";
				}

				Block.TargetExists = true;
				Block.TargetIsRedirect = Target.Type == ArticleType.Redirect;
				Block.ResolvedTitle = Target.Title;
				Block.ResolvedPath = Path;
			}
			else
			{
				string Path = ArticleUrlHelper.BuildArticlePath(Block.TargetSlug, ArticleType.Article);
				if (!string.IsNullOrEmpty(Block.Fragment))
				{
					Path = $"{Path}#{Block.Fragment}";
				}

				Block.TargetExists = false;
				Block.TargetIsRedirect = false;
				Block.ResolvedPath = Path;
			}
		}
	}

	/// <summary>
	/// Validates that a normalized target slug represents a safe local wiki slug rather than a URL, path traversal, or user-unsafe value.
	/// </summary>
	/// <param name="Slug">The normalized target slug.</param>
	/// <returns><see langword="true"/> when the slug is valid for a wiki redirect; otherwise <see langword="false"/>.</returns>
	private static bool IsValidTargetSlug(string Slug)
	{
		if (string.IsNullOrWhiteSpace(Slug)
			|| Slug.StartsWith('@')
			|| Slug.Contains("..", StringComparison.Ordinal)
			|| Slug.Contains("://", StringComparison.Ordinal)
			|| ArticleUrlHelper.IsRestrictedSlug(Slug))
		{
			return false;
		}

		if (Slug.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
			|| Slug.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
			|| Slug.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		foreach (char Ch in Slug)
		{
			if (char.IsWhiteSpace(Ch)
				|| char.IsControl(Ch)
				|| Ch is '/' or '\\' or '?' or '&' or '<' or '>' or '"' or '\'' or '`')
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Validates that an optional URL fragment contains only safe anchor characters.
	/// </summary>
	/// <param name="Fragment">The raw fragment text after <c>#</c>.</param>
	/// <returns><see langword="true"/> when the fragment is safe to append to a local URL; otherwise <see langword="false"/>.</returns>
	private static bool IsValidFragment(string Fragment)
	{
		foreach (char Ch in Fragment)
		{
			if (!(char.IsLetterOrDigit(Ch) || Ch is '-' or '_' or '.'))
			{
				return false;
			}
		}

		return true;
	}
}
