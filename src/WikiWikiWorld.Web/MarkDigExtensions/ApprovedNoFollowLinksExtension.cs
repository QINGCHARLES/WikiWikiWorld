using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Collections.Generic;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A Markdig extension that applies rel="noopener" to all external links,
/// and adds "nofollow" unless the domain is in the approved list.
/// </summary>
public sealed class ApprovedNoFollowLinksExtension : IMarkdownExtension
{
	private readonly IReadOnlyList<string> ApprovedDomains;

	/// <summary>
	/// Initializes a new instance of the <see cref="ApprovedNoFollowLinksExtension"/> class.
	/// </summary>
	/// <param name="ApprovedDomains">The collection of approved domains.</param>
	public ApprovedNoFollowLinksExtension(IReadOnlyList<string> ApprovedDomains)
	{
		this.ApprovedDomains = ApprovedDomains ?? [];
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		Pipeline.DocumentProcessed += DocumentProcessed;
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		// No custom renderer needed, modifies AST attributes.
	}

	private void DocumentProcessed(MarkdownDocument Document)
	{
		foreach (LinkInline Link in Document.Descendants<LinkInline>())
		{
			// Process only standard and auto links (not images).
			if (Link.IsImage)
			{
				continue;
			}

			// External link check
			if (!string.IsNullOrWhiteSpace(Link.Url) &&
				(Link.Url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) ||
				 Link.Url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) ||
				 Link.Url.StartsWith("//", System.StringComparison.OrdinalIgnoreCase)))
			{
				bool IsApproved = LinkDomainHelper.IsApprovedDomain(Link.Url, this.ApprovedDomains);
				
				// Always use noopener for external links
				string RelValue = IsApproved ? "noopener" : "nofollow noopener";

				Link.GetAttributes().AddPropertyIfNotExist("rel", RelValue);
			}
		}
	}
}
