using Markdig;
using Markdig.Syntax;
using WikiWikiWorld.Web.MarkdigExtensions;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies eyebrow metadata rejects removed types and preserves the first valid authored label.</summary>
public sealed class EyebrowTests
{
	/// <summary>Exercises parsing and enrichment together, including removed values before valid directives.</summary>
	/// <param name="Source">Authored Markdown containing eyebrow directives.</param>
	/// <param name="ExpectedLabel">The label exposed to the article view, or null when no valid type exists.</param>
	[Theory]
	[InlineData("{{Eyebrow MAGAZINEFRANCHISE}}", null)]
	[InlineData("{{Eyebrow UNKNOWN}}", null)]
	[InlineData("{{Eyebrow MAGAZINEFRANCHISE}}\n\n{{Eyebrow FRANCHISE}}", "Franchise")]
	[InlineData("{{Eyebrow zineissue}}\n\n{{Eyebrow MAGAZINE}}", "Zine Issue")]
	[InlineData("{{Eyebrow JournalIssue}}", "Journal Issue")]
	[InlineData("{{Eyebrow newsletter}}", "Newsletter")]
	[InlineData("{{Eyebrow NEWSLETTERISSUE}}", "Newsletter Issue")]
	public void MetadataUsesFirstSupportedType(string Source, string? ExpectedLabel)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new EyebrowExtension()).Build();
		MarkdownDocument Document = Markdown.Parse(Source, Pipeline);

		EyebrowExtension.Enrich(Document);

		Assert.Equal(ExpectedLabel, Document.GetData(EyebrowExtension.DocumentKey) as string);
		Assert.DoesNotContain("{{Eyebrow", Document.ToHtml(Pipeline), StringComparison.Ordinal);
	}
}
