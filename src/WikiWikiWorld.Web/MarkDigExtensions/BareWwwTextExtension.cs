using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Keeps bare <c>www.</c> addresses as plain text instead of the links Markdig's autolink extension makes of them.
/// </summary>
/// <remarks>
/// Articles name old websites exactly as they were printed, and many of those domains are dead or now belong to
/// someone else, so a bare address must not become a live link. Only the autolinks that bare <c>www.</c> text
/// produces are unwrapped: explicit <c>[text](url)</c> links (which authors use to point at an archive copy),
/// <c>&lt;url&gt;</c> autolinks and bare <c>http://</c>/<c>https://</c> URLs keep linking.
/// </remarks>
public sealed class BareWwwTextExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		Pipeline.DocumentProcessed -= UnlinkBareWwwAddresses;
		Pipeline.DocumentProcessed += UnlinkBareWwwAddresses;
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		// The document is rewritten after parsing; no renderer is needed.
	}

	/// <summary>
	/// Replaces each autolink made from bare <c>www.</c> text with the literal text it was made from.
	/// </summary>
	/// <param name="Document">The parsed document, changed in place.</param>
	private static void UnlinkBareWwwAddresses(MarkdownDocument Document)
	{
		// Collected first: replacing inlines while enumerating the tree would skip or revisit nodes.
		List<LinkInline> Links = [.. Document.Descendants<LinkInline>().Where(IsBareWwwAutoLink)];
		foreach (LinkInline Link in Links)
		{
			LiteralInline Text = (LiteralInline)Link.FirstChild!;
			// copyChildren: false, or the link's literal child is kept beside the replacement and the text doubles.
			Link.ReplaceBy(new LiteralInline(Text.Content.ToString())
			{
				Span = Link.Span,
				Line = Link.Line,
				Column = Link.Column,
			}, copyChildren: false);
		}
	}

	/// <summary>
	/// Tells whether a link is one Markdig's autolink extension made from bare <c>www.</c> text.
	/// </summary>
	/// <param name="Link">The link to inspect.</param>
	/// <returns><see langword="true"/> for an autolink whose only content is literal text starting with <c>www.</c>.</returns>
	private static bool IsBareWwwAutoLink(LinkInline Link) =>
		Link is { IsAutoLink: true, IsImage: false, FirstChild: LiteralInline Text }
		&& Text.NextSibling is null
		&& Text.Content.AsSpan().StartsWith("www.", StringComparison.OrdinalIgnoreCase);
}
