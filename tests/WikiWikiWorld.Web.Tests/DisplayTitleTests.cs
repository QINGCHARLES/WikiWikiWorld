using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Web.Helpers;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies automatic title breaks preserve authored text and HTML encoding.</summary>
public sealed class DisplayTitleTests
{
	/// <summary>Checks visible slash breaks without introducing authored HTML or changing plain-text titles.</summary>
	/// <param name="DisplayTitle">The optional authored display title.</param>
	/// <param name="FallbackTitle">The canonical title used when the display title is absent.</param>
	/// <param name="ExpectedHtml">The safe heading fragment including optional and explicit breaks.</param>
	/// <param name="ExpectedPlainText">The unchanged title for metadata and inline references.</param>
	[Theory]
	[InlineData("SPRING/SUMMER 2014", "Canonical", "SPRING/<wbr>SUMMER 2014", "SPRING/SUMMER 2014")]
	[InlineData(null, "SPRING/SUMMER", "SPRING/<wbr>SUMMER", "SPRING/SUMMER")]
	[InlineData("USA\r\nSPRING/SUMMER", "Canonical", "USA<br />SPRING/<wbr>SUMMER", "USA SPRING/SUMMER")]
	[InlineData("<script>alert(1)</script>/A&B", "Canonical", "&lt;script&gt;alert(1)&lt;/<wbr>script&gt;/<wbr>A&amp;B", "<script>alert(1)</script>/A&B")]
	[InlineData("Before/[[SPRING/SUMMER|#| ]]After/2014", "Canonical", "Before/<wbr>SPRING/<wbr>SUMMERAfter/<wbr>2014", "Before/SPRING/SUMMERAfter/2014")]
	public async Task RenderingPreservesTextAndEncoding(string? DisplayTitle, string FallbackTitle, string ExpectedHtml, string ExpectedPlainText)
	{
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().Options);
		string Html = await DisplayTitleHelper.RenderHtmlAsync(DisplayTitle, FallbackTitle, Context, 1, "en", CancellationToken.None);

		Assert.Equal(ExpectedHtml, Html);
		Assert.Equal(ExpectedPlainText, DisplayTitleHelper.ToPlainText(DisplayTitle, FallbackTitle));
		HtmlParser Parser = new();
		Assert.Null(Parser.ParseDocument(Html).QuerySelector("script"));
	}
}
