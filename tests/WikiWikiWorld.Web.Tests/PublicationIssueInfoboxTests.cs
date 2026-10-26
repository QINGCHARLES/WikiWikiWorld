using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies infobox values through the production Markdown pipeline and shared note processing.</summary>
public sealed class PublicationIssueInfoboxTests
{
	/// <summary>Checks inline formatting without introducing paragraphs or parsing property names as Markdown.</summary>
	[Fact]
	public void FormatsValuesAndEscapesLabels()
	{
		string Html = Render("{{PublicationIssueInfobox CoverDate=*January* **1993** |#| Editor=[Jane](/jane) |#| **Custom**=<script>alert(1)</script> |#| Formats=***Print*** and `digital`}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal("January", Document.QuerySelector("dd em")!.TextContent);
		Assert.Equal("1993", Document.QuerySelector("dd strong")!.TextContent);
		Assert.Equal("/jane", Document.QuerySelector("dd a")!.GetAttribute("href"));
		Assert.Equal("Print", Document.QuerySelector("dd em strong, dd strong em")!.TextContent);
		Assert.Equal("digital", Document.QuerySelector("dd code")!.TextContent);
		Assert.Contains("<dt>**Custom**</dt>", Html);
		Assert.Null(Document.QuerySelector("script"));
		Assert.Null(Document.QuerySelector("dd p"));
		Assert.DoesNotContain("{{PublicationIssueInfobox", Html);
	}

	/// <summary>Ensures nested notes share numbering, formatted bodies, and functioning backlinks with prose.</summary>
	[Fact]
	public void SharesCustomFootnotesWithSurroundingProse()
	{
		string Html = Render("""
			Before.{{Footnote First note.}}

			{{PublicationIssueInfobox
			CoverDate=1993{{Footnote Sold in **1992**.}} |#|
			Editor=Jane{{Footnote See *Archive*.}}
			}}

			After.{{Footnote Last note.}}

			{{Footnotes}}
			""");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal(["1", "2", "3", "4"], Document.QuerySelectorAll("sup").Select(x => x.TextContent).ToArray());
		Assert.Equal(["2", "3"], Document.QuerySelectorAll(".infobox sup").Select(x => x.TextContent).ToArray());
		Assert.Equal(4, Document.QuerySelectorAll(".footnote-list > li").Length);
		Assert.Contains("<strong>1992</strong>", Html);
		Assert.Contains("<em>Archive</em>", Html);
		foreach (IElement Link in Document.QuerySelectorAll("sup a, .footnote-backref"))
		{
			Assert.NotNull(Document.GetElementById(Link.GetAttribute("href")![1..]));
		}
	}

	/// <summary>Uses normal document reference definitions for standard labeled footnotes and article links.</summary>
	[Fact]
	public void ResolvesStandardFootnotesAndReferenceLinks()
	{
		string Html = Render("""
			{{PublicationIssueInfobox Editor=[Jane][editor][^archive]}}

			[editor]: /jane
			[^archive]: From the *archive*.
			""");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal("/jane", Document.QuerySelector(".infobox a")!.GetAttribute("href"));
		IElement Reference = Document.QuerySelector(".infobox a[href^='#fn']")!;
		Assert.NotNull(Reference);
		Assert.Contains("archive", Document.GetElementById(Reference.GetAttribute("href")![1..])!.TextContent);
		Assert.Contains("<em>archive</em>", Html);
	}

	/// <summary>Preserves citation property separators and de-duplicates references across the article.</summary>
	[Fact]
	public void SharesCitationsAndPreservesNestedProperties()
	{
		string Html = Render("""
			Before.{{Citation title=Archive |#| year=1993}}

			{{PublicationIssueInfobox CoverDate=1993{{Citation title=Archive |#| year=1993}} |#| Editor=Jane}}

			{{Citations}}
			""");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal(2, Document.QuerySelectorAll("dt").Length);
		Assert.Equal("Editor", Document.QuerySelectorAll("dt")[1].TextContent);
		Assert.Contains("Archive", Document.QuerySelector(".citations")!.TextContent);
		Assert.Single(Document.QuerySelectorAll(".citations li"));
		Assert.Equal(2, Document.QuerySelectorAll("a[href='#cit:1']").Length);
	}

	/// <summary>Ignores outer-looking delimiters inside code spans and escaped punctuation.</summary>
	/// <param name="Value">Inline Markdown containing literal directive punctuation.</param>
	/// <param name="Expected">Its visible plain-text result.</param>
	[Theory]
	[InlineData("`}} |#| {{`", "}} |#| {{")]
	[InlineData("``a ` }} |#| b``", "a ` }} |#| b")]
	[InlineData("\\}} \\|#| \\{\\{literal\\}\\}", "}} |#| {{literal}}")]
	[InlineData("Unmatched `tick", "Unmatched `tick")]
	public void PreservesLiteralDelimiters(string Value, string Expected)
	{
		string Html = Render("{{PublicationIssueInfobox Fact=" + Value + " |#| Editor=Jane}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal(2, Document.QuerySelectorAll("dd").Length);
		Assert.Equal(Expected, Document.QuerySelectorAll("dd")[0].TextContent);
		Assert.Equal("Jane", Document.QuerySelectorAll("dd")[1].TextContent);
	}

	/// <summary>Allows literal closing delimiters and citations inside a custom note body.</summary>
	[Fact]
	public void SupportsCodeAndCitationsInsideInfoboxNotes()
	{
		string Html = Render("""
			{{PublicationIssueInfobox Editor=Jane{{Footnote Literal `}} |#|` and {{Citation title=Archive |#| year=1993}}.}} |#| CoverDate=1993}}

			{{Footnotes}}

			{{Citations}}
			""");
		Assert.Contains("<code>}} |#|</code>", Html);
		Assert.Contains("Archive", Html);
		Assert.Contains("<dt>Cover Date</dt><dd>1993</dd>", Html);
		Assert.DoesNotContain("{{Citation", Html);
		Assert.DoesNotContain("fnref:?", Html);
	}

	/// <summary>Retains grouping, repeated values, empty fields, and ignored legacy image metadata.</summary>
	[Fact]
	public void PreservesExistingFactBehavior()
	{
		string Html = Render("{{PublicationIssueInfobox Editor=*Alice* |#| CoverDate=1993 |#| editor=**Bob** |#| CoverImage=file:ignored{{Footnote Ignored.}} |#| Empty=}}");
		Assert.Contains("<dt>Editor</dt><dd><em>Alice</em></dd><dt>Editor</dt><dd><strong>Bob</strong></dd><dt>Cover Date</dt>", Html);
		Assert.Contains("<dt>Empty</dt><dd></dd>", Html);
		Assert.DoesNotContain("ignored", Html);
		Assert.DoesNotContain("fnref:", Html);
	}

	/// <summary>Applies the production external-link policy to inline links in infobox values.</summary>
	[Fact]
	public void AppliesArticleLinkPolicy()
	{
		string Html = Render("{{PublicationIssueInfobox Source=[Archive](https://example.org/archive)}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal("nofollow noopener", Document.QuerySelector("dd a")!.GetAttribute("rel"));
	}

	/// <summary>Shows incomplete input as escaped source instead of emitting partially parsed facts.</summary>
	[Fact]
	public void KeepsMalformedSourceVisible()
	{
		string Html = Render("{{PublicationIssueInfobox Editor=<b>Jane</b>{{Footnote Unclosed}}\nFollowing text.");
		Assert.Contains("infobox-error", Html);
		Assert.Contains("{{PublicationIssueInfobox", Html);
		Assert.Contains("Following text.", Html);
		Assert.DoesNotContain("<b>", Html);
		Assert.DoesNotContain("<dd>", Html);
	}

	/// <summary>Finds legacy covers after nested notes and ignores cover-looking properties inside citations.</summary>
	[Fact]
	public void FindsLegacyCoverAfterNestedValues()
	{
		Assert.Equal("front", ArticleCoverReference.Extract("{{PublicationIssueInfobox Editor=Jane{{Footnote See {{Citation title=Archive |#| CoverImage=wrong}}.}} |#| CoverImage=file:front}}"));
		Assert.Equal("front", ArticleCoverReference.Extract("{{PublicationIssueInfobox Editor=Jane{{Footnote Literal |~| CoverImage=wrong.}} |#| CoverImage=file:front}}"));
		Assert.Equal("front", ArticleCoverReference.Extract("{{PublicationIssueInfobox Fact=`}} |#| CoverImage=wrong` |#| CoverImage=file:front}}"));
	}

	/// <summary>Recognizes complete directive names while preserving indented and fenced code examples.</summary>
	/// <param name="Source">A valid directive or an example that must remain literal.</param>
	/// <param name="ExpectedCount">The number of infoboxes recognized in the document.</param>
	[Theory]
	[InlineData("{{PublicationIssueInfobox Editor=Jane}}", 1)]
	[InlineData("{{PublicationIssueInfobox\r\nEditor=Jane\r\n}}", 1)]
	[InlineData("{{PublicationIssueInfoboxExtra Editor=Jane}}", 0)]
	[InlineData("    {{PublicationIssueInfobox Editor=Jane}}", 0)]
	[InlineData("```\n{{PublicationIssueInfobox Editor=Jane}}\n```", 0)]
	[InlineData("`{{PublicationIssueInfobox Editor=Jane}}`", 0)]
	[InlineData("> {{PublicationIssueInfobox\n> Editor=Jane\n> }}", 1)]
	[InlineData("- {{PublicationIssueInfobox\n  Editor=Jane\n  }}", 1)]
	public void RecognizesDirectiveBoundaries(string Source, int ExpectedCount)
	{
		string Html = Render(Source);
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Equal(ExpectedCount, Document.QuerySelectorAll(".infobox").Length);
		if (ExpectedCount > 0) Assert.Equal("Jane", Document.QuerySelector("dd")!.TextContent);
	}

	/// <summary>Keeps heading and list punctuation inline instead of constructing nested document blocks.</summary>
	[Fact]
	public void KeepsBlockSyntaxAsInlineText()
	{
		string Html = Render("{{PublicationIssueInfobox Fact=# Heading\n- List item}}");
		using IDocument Document = new HtmlParser().ParseDocument(Html);
		Assert.Null(Document.QuerySelector(".infobox h1, .infobox ul, .infobox p"));
		Assert.Contains("# Heading", Document.QuerySelector("dd")!.TextContent);
		Assert.Contains("- List item", Document.QuerySelector("dd")!.TextContent);
	}

	/// <summary>Runs the same parsing and shared custom-note passes used by the article renderer.</summary>
	/// <param name="Source">The article Markdown to render.</param>
	/// <returns>HTML produced by the production pipeline before page-level layout processing.</returns>
	private static string Render(string Source)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())).GetPipeline();
		MarkdownDocument Document = Markdown.Parse(Source, Pipeline);
		FootnoteExtension.ReprocessFootnotes(Document, Pipeline);
		CitationExtension.ReprocessCitations(Document);
		using StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		Pipeline.Setup(Renderer);
		Renderer.Render(Document);
		return Writer.ToString();
	}
}
