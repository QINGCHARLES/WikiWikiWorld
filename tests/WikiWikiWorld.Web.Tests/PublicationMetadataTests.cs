using System.Text.Json;
using Markdig;
using Markdig.Syntax;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Pages.Article;
using WikiWikiWorld.Web.Services;
using WikiWikiWorld.Web.Helpers;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Protects the catalogue facts and header authoring syntax consumed by the article head.</summary>
public sealed class PublicationMetadataTests
{
	/// <summary>Checks that the three alt-text states and helper-encoded punctuation survive parsing.</summary>
	/// <param name="Source">One whole header directive.</param>
	/// <param name="ExpectedAlt">The authored text, empty marker or absence.</param>
	/// <param name="ExpectedFocus">The expected focus token on the same image.</param>
	[Theory]
	[InlineData("{{HeaderImage file:art |#| Focus=centre}}", null, "center")]
	[InlineData("{{HeaderImage art |#| AltText=}}", "", null)]
	[InlineData("{{HeaderImage art |#| Focus=bottom |#| AltText=Blue &#124;#&#124; green &#125;&#125; &#38; red}}", "Blue |#| green }} & red", "bottom")]
	public void HeaderDescriptionPreservesLegacyAndDecorativeMarkup(string Source, string? ExpectedAlt, string? ExpectedFocus)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new HeaderImageExtension()).Build();
		HeaderImageBlock Header = Assert.Single(Markdown.Parse(Source, Pipeline).OfType<HeaderImageBlock>());
		Assert.Equal(ExpectedAlt, Header.AltText);
		Assert.Equal(ExpectedFocus, Header.Focus);
	}

	/// <summary>Checks that legacy issue fields, a seasonal designation and an inline parent link keep their meaning.</summary>
	[Fact]
	public void LegacyIssueLinksToTheVerifiedParentWithoutGuessingADate()
	{
		PublicationFacts Facts = Assert.IsType<PublicationFacts>(Parse("""
			{{Eyebrow MagazineIssue}}
			{{PublicationIssueInfobox Publication=[*Example Times*](/example-times) |#| Issue=Vol. 3, No. 2–3 |#| CoverDate=Winter 1984–85 |#| PrintPublicationDate=Winter 1984–85 |#| CoverPrice=$5}}

			This readable description includes *emphasis* and [a link](/example). A second sentence follows.
			"""));
		Assert.Equal("/example-times", Facts.Single("Publication")?.Link);
		Assert.Equal("Example Times", Facts.Single("Publication")?.Value);
		Assert.Equal("This readable description includes emphasis and a link.", Facts.Lead);
		string Parent = "https://ja.example.test/example-times#periodical";
		using JsonDocument Data = JsonDocument.Parse(PublicationHeadBuilder.BuildJsonLd(CreateRevision(), Facts,
			new Uri("https://ja.example.test/issue"), "Example Times — 2–3", Facts.Lead, "Magazedia", Parent));
		JsonElement Issue = Data.RootElement.GetProperty("@graph")[1];
		Assert.Equal("2–3", Issue.GetProperty("issueNumber").GetString());
		Assert.Equal("3", Issue.GetProperty("isPartOf").GetProperty("volumeNumber").GetString());
		Assert.Equal(Parent, Issue.GetProperty("isPartOf").GetProperty("isPartOf").GetProperty("@id").GetString());
		Assert.False(Issue.TryGetProperty("datePublished", out _));
		Assert.False(Issue.TryGetProperty("offers", out _));
	}

	/// <summary>Verifies repeatable valid serial identifiers, language, and safe JSON embedding with authored text.</summary>
	[Fact]
	public void PeriodicalKeepsValidSerialNumbersAndLeavesPageLanguageIndependent()
	{
		PublicationFacts Facts = Assert.IsType<PublicationFacts>(Parse("""
			{{Infobox Publication ISSN=1085-9241 (print) |#| ISSN=1943-2623 (online) |#| ISSN=1084-662x |#| ISSN=2471-5393 |#| Language=English |#| Website=[example.test](https://example.test) |#| Publisher=Example (1995–2001)}}
			"""));
		const string Title = "東京 & \"Example\" </script>";
		string Json = PublicationHeadBuilder.BuildJsonLd(CreateRevision(), Facts, new Uri("https://ja.example.test/example"),
			Title, null, "Magazedia", null);
		Assert.DoesNotContain("</script", Json, StringComparison.OrdinalIgnoreCase);
		using JsonDocument Data = JsonDocument.Parse(Json);
		JsonElement Graph = Data.RootElement.GetProperty("@graph");
		Assert.Equal(Title, Graph[0].GetProperty("name").GetString());
		Assert.Equal("ja", Graph[0].GetProperty("inLanguage").GetString());
		Assert.Equal("en", Assert.Single(Graph[1].GetProperty("inLanguage").EnumerateArray()).GetString());
		Assert.Equal(["1085-9241", "1943-2623", "1084-662X"], Graph[1].GetProperty("issn").EnumerateArray().Select(Value => Value.GetString()));
		Assert.Equal("Example", Graph[1].GetProperty("publisher").GetProperty("name").GetString());
		Assert.Equal("https://example.test/", Graph[1].GetProperty("url").GetString());
	}

	/// <summary>Collects the complementary blocks used by publications that split their infobox.</summary>
	[Fact]
	public void ComplementaryBoxesDescribeOnePeriodical()
	{
		PublicationFacts Facts = Assert.IsType<PublicationFacts>(Parse("""
			{{Eyebrow Magazine}}
			{{Infobox Publication Publisher=Example}}

			An introductory passage separates the two infoboxes.

			{{Infobox Publication ISSN=1085-9241 (print) |#| ISSN=1943-2623 (online)}}
			"""));
		Assert.False(Facts.IsIssue);
		Assert.Equal("Example", Facts.Single("Publisher")?.Value);
		Assert.Equal(2, Facts.Fields.Count(Fact => Fact.Key == "ISSN"));
	}

	/// <summary>Protects the edition-specific RIS output and a revision permalink on a whole minute.</summary>
	[Fact]
	public void CitationsPreserveSeasonAndRevisionAtAWholeMinute()
	{
		PublicationFacts Facts = Assert.IsType<PublicationFacts>(Parse("""
			{{Infobox PublicationIssue Publication=[*Example*](/example) |#| Issue=Volume 3 - Issue 2 |#| CoverDate=Fall 2016}}
			"""));
		const string Url = "https://ja.example.test/issue";
		PublicationHead Head = new(Url, "Example No. 2", null, "Magazedia", new PublicationShareImage(Url, "Cover"), string.Empty);
		PublicationCitation IssueCitation = PublicationCitation.ForIssue(Head, Facts);
		Assert.Contains("JF  - Example\r\n", IssueCitation.Ris);
		Assert.Contains("VL  - 3\r\nIS  - 2\r\n", IssueCitation.Ris);
		Assert.Contains("Fall 2016", IssueCitation.Text);
		Assert.DoesNotContain("DA  - ", IssueCitation.Ris);

		ArticleRevision Revision = CreateRevision();
		string Timestamp = ArticleHistoryModel.FormatRevisionTimestamp(Revision.DateCreated);
		Assert.True(RevisionDateParser.TryParseRevisionDate(Timestamp, out DateTimeOffset Parsed));
		Assert.Equal(Revision.DateCreated, Parsed);
		PublicationCitation PageCitation = PublicationCitation.ForPage(Head, Revision, Url + "/revision/" + Timestamp);
		Assert.Contains("TY  - WEB\r\n", PageCitation.Ris);
		Assert.Contains("/revision/20261122120000\r\n", PageCitation.Ris);
	}

	/// <summary>Prevents examples, conflicting subject types and multiple main boxes from creating a false record.</summary>
	/// <param name="Source">The article markup to classify.</param>
	[Theory]
	[InlineData("```markdown\n{{Infobox Publication ISSN=1084-662X}}\n```")]
	[InlineData("{{Eyebrow Magazine}}\n{{Infobox PublicationIssue Issue=7}}")]
	[InlineData("{{Infobox Publication ISSN=1084-662X}}\n{{Infobox Publication ISSN=1085-9241}}")]
	public void AmbiguousSourceDoesNotCreateACatalogueRecord(string Source) => Assert.Null(Parse(Source));

	/// <summary>Runs the same infobox and eyebrow configuration as Magazedia.</summary>
	/// <param name="Source">The fixture article's markup.</param>
	/// <returns>Its extracted publication facts when present.</returns>
	private static PublicationFacts? Parse(string Source)
	{
		MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use(new InfoboxExtension()).Use(new EyebrowExtension()).Build();
		MarkdownDocument Document = Markdown.Parse(Source, Pipeline);
		InfoboxExtension.Enrich(Document, TestInfoboxSchemas.Catalog, 1, "ja");
		EyebrowExtension.Enrich(Document);
		return PublicationFacts.Extract(Document);
	}

	/// <summary>Provides an article revision whose date and language describe only the wiki entry.</summary>
	/// <returns>A fixed Japanese wiki revision.</returns>
	private static ArticleRevision CreateRevision() => new()
	{
		Culture = "ja", Title = "Example", UrlSlug = "example", Text = string.Empty, RevisionReason = string.Empty,
		DateCreated = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero)
	};
}
