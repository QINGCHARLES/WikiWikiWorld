using Markdig;
using Microsoft.Extensions.Options;
using System.Globalization;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies the controlled YouTube authoring contract and Markdown pipeline boundaries.
/// </summary>
public sealed class YouTubeExtensionTests
{
	private static readonly IMarkdownPipelineFactory PipelineFactory = new MarkdownPipelineFactory(
		Options.Create(new SiteConfiguration()));

	/// <summary>
	/// Verifies that supported references render the same local click-to-load placeholder.
	/// </summary>
	/// <param name="VideoReference">The accepted video reference.</param>
	/// <param name="ExpectedStartSeconds">The expected start position extracted from the reference.</param>
	[Theory]
	[InlineData("M7lc1UVf-VE", 0)]
	[InlineData("https://www.youtube.com/watch?v=M7lc1UVf-VE", 0)]
	[InlineData("https://m.youtube.com/watch?v=M7lc1UVf-VE&feature=shared", 0)]
	[InlineData("https://youtu.be/M7lc1UVf-VE?t=1m30s&si=ShareToken", 90)]
	[InlineData("https://youtube.com/shorts/M7lc1UVf-VE?t=45s", 45)]
	[InlineData("https://www.youtube.com/embed/M7lc1UVf-VE?start=20", 20)]
	[InlineData("https://www.youtube-nocookie.com/embed/M7lc1UVf-VE?start=30", 30)]
	public void SupportedVideoReferencesRenderLocalPlaceholder(
		string VideoReference,
		int ExpectedStartSeconds)
	{
		string Source = $"{{{{YouTube {VideoReference} |#| Title=Player demonstration}}}}";

		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("<figure class=\"youtube-embed\">", Html, StringComparison.Ordinal);
		Assert.Contains("data-youtube-video-id=\"M7lc1UVf-VE\"", Html, StringComparison.Ordinal);
		Assert.Contains("Player demonstration", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<iframe", Html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("youtube-nocookie.com", Html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<img", Html, StringComparison.OrdinalIgnoreCase);

		if (ExpectedStartSeconds > 0)
		{
			Assert.Contains(
				$"data-youtube-start-seconds=\"{ExpectedStartSeconds.ToString(CultureInfo.InvariantCulture)}\"",
				Html,
				StringComparison.Ordinal);
		}
		else
		{
			Assert.DoesNotContain("data-youtube-start-seconds", Html, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Verifies that an explicit start position overrides a timestamp in a pasted URL.
	/// </summary>
	[Fact]
	public void ExplicitStartSecondsOverridesUrlTimestamp()
	{
		const string Source = "{{YouTube https://youtu.be/M7lc1UVf-VE?t=90s |#| Title=Override |#| StartSeconds=12}}";

		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("data-youtube-start-seconds=\"12\"", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("data-youtube-start-seconds=\"90\"", Html, StringComparison.Ordinal);
	}

	/// <summary>
	/// Verifies that author-controlled title and caption text is HTML encoded.
	/// </summary>
	[Fact]
	public void TitleAndCaptionAreHtmlEncoded()
	{
		const string Source = "{{YouTube M7lc1UVf-VE |#| Title=A <B> & \"C\" |#| Caption=<em>Plain & safe</em>}}";

		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("A &lt;B&gt; &amp; &quot;C&quot;", Html, StringComparison.Ordinal);
		Assert.Contains("&lt;em&gt;Plain &amp; safe&lt;/em&gt;", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<em>Plain", Html, StringComparison.Ordinal);
	}

	/// <summary>
	/// Verifies that invalid or ambiguous directives cannot produce controlled embed markup.
	/// </summary>
	/// <param name="Source">The invalid directive source.</param>
	[Theory]
	[InlineData("{{YouTube bad-id |#| Title=Invalid identifier}}")]
	[InlineData("{{YouTube M7lc1UVf-VE}}")]
	[InlineData("{{YouTube M7lc1UVf-VE |#| Title=Invalid start |#| StartSeconds=-1}}")]
	[InlineData("{{YouTube M7lc1UVf-VE |#| Title=Unknown property |#| Autoplay=true}}")]
	[InlineData("{{YouTube https://www.youtube.com.evil.example/watch?v=M7lc1UVf-VE |#| Title=Host prefix}}")]
	[InlineData("{{YouTube https://www.youtube.com/watch?v=M7lc1UVf-VE&list=PL123 |#| Title=Playlist}}")]
	[InlineData("{{YouTube https://youtu.be/M7lc1UVf-VE?tracking=1 |#| Title=Unknown query}}")]
	[InlineData("{{YouTube https://youtu.be/M7lc1UVf-VE?t=20s&start=20 |#| Title=Ambiguous time}}")]
	[InlineData("{{YouTube https://youtu.be/M7lc1UVf-VE?t=999999999999999999999999999999h |#| Title=Overflowing time}}")]
	public void InvalidDirectivesRemainVisibleText(string Source)
	{
		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("{{YouTube", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("youtube-embed", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<iframe", Html, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that raw HTML inside Timeline descriptions cannot bypass the article pipeline's HTML restriction.
	/// </summary>
	[Fact]
	public void TimelineDescriptionsDisableRawHtml()
	{
		const string Source = "{{Timeline Title=Unsafe |#| Description=<script>alert('x')</script>}}";

		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("&lt;script&gt;", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<script", Html, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that raw article HTML is encoded rather than executed.
	/// </summary>
	[Fact]
	public void FullArticlePipelineDisablesRawHtml()
	{
		const string Source = "<script>alert('x')</script>\n\n<iframe src=\"https://example.com\"></iframe>";

		string Html = RenderArticleMarkdown(Source);

		Assert.Contains("&lt;script&gt;", Html, StringComparison.Ordinal);
		Assert.Contains("&lt;iframe", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<script", Html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<iframe", Html, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that Markdig's built-in image-style media syntax cannot render an iframe.
	/// </summary>
	[Fact]
	public void BuiltInMediaLinkSyntaxCannotRenderIframe()
	{
		const string Source = "![Video](https://www.youtube.com/watch?v=M7lc1UVf-VE)";

		string Html = RenderArticleMarkdown(Source);

		Assert.DoesNotContain("<iframe", Html, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Verifies that talk posts do not activate article directives or raw HTML.
	/// </summary>
	[Fact]
	public void TalkPipelineLeavesYouTubeDirectiveAsText()
	{
		const string Source = "{{YouTube M7lc1UVf-VE |#| Title=Talk video}}\n<script>alert('x')</script>";

		string Html = PipelineFactory.RenderTalkMarkdown(Source);

		Assert.Contains("{{YouTube", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("youtube-embed", Html, StringComparison.Ordinal);
		Assert.Contains("&lt;script&gt;", Html, StringComparison.Ordinal);
		Assert.DoesNotContain("<script", Html, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Renders Markdown through the application's configured full article pipeline.
	/// </summary>
	/// <param name="Source">The Markdown source.</param>
	/// <returns>The rendered HTML.</returns>
	private static string RenderArticleMarkdown(string Source)
	{
		return Markdown.ToHtml(Source, PipelineFactory.GetPipeline());
	}
}
