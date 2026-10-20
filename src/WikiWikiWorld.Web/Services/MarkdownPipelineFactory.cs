using Markdig;
using Markdig.Extensions.MediaLinks;
using Markdig.Renderers;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Factory service for creating and caching Markdown pipelines.
/// </summary>
public interface IMarkdownPipelineFactory
{
	/// <summary>
	/// Gets the cached full article Markdown pipeline, including every custom site extension.
	/// </summary>
	/// <returns>A configured <see cref="MarkdownPipeline"/>.</returns>
	MarkdownPipeline GetPipeline();

	/// <summary>
	/// Gets the cached minimal Markdown pipeline used for short user-generated content such as talk posts.
	/// </summary>
	/// <returns>A configured <see cref="MarkdownPipeline"/> limited to safe inline formatting, lists, links and quotes.</returns>
	MarkdownPipeline GetTalkPipeline();

	/// <summary>
	/// Renders a fragment of Markdown using the talk pipeline and returns the resulting HTML.
	/// </summary>
	/// <param name="Source">The Markdown source. <see langword="null"/> or whitespace renders to <see cref="string.Empty"/>.</param>
	/// <returns>The rendered HTML string. Callers are expected to wrap it with <c>Html.Raw</c>.</returns>
	string RenderTalkMarkdown(string? Source);
}

/// <summary>
/// Implementation of <see cref="IMarkdownPipelineFactory"/> that caches a single pipeline instance per pipeline kind.
/// </summary>
public sealed class MarkdownPipelineFactory : IMarkdownPipelineFactory
{
	private readonly Lazy<MarkdownPipeline> Pipeline;
	private readonly Lazy<MarkdownPipeline> TalkPipeline;

	/// <summary>
	/// Initializes a new instance of the <see cref="MarkdownPipelineFactory"/> class.
	/// </summary>
	/// <param name="SiteConfiguration">The site configuration options.</param>
	public MarkdownPipelineFactory(IOptions<SiteConfiguration> SiteConfiguration)
	{
		this.Pipeline = new Lazy<MarkdownPipeline>(() => BuildPipeline(SiteConfiguration.Value.ApprovedLinkDomains));
		this.TalkPipeline = new Lazy<MarkdownPipeline>(BuildTalkPipeline);
	}

	/// <inheritdoc/>
	public MarkdownPipeline GetPipeline()
	{
		return Pipeline.Value;
	}

	/// <inheritdoc/>
	public MarkdownPipeline GetTalkPipeline()
	{
		return TalkPipeline.Value;
	}

	/// <inheritdoc/>
	public string RenderTalkMarkdown(string? Source)
	{
		if (string.IsNullOrWhiteSpace(Source))
		{
			return string.Empty;
		}

		MarkdownPipeline TalkPipelineInstance = TalkPipeline.Value;
		StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		TalkPipelineInstance.Setup(Renderer);
		Renderer.Render(Markdown.Parse(Source, TalkPipelineInstance));
		Writer.Flush();
		return Writer.ToString();
	}

	/// <summary>
	/// Builds and configures the Markdown pipeline with all custom extensions.
	/// </summary>
	/// <param name="ApprovedLinkDomains">The list of domains that do not require nofollow on external links.</param>
	/// <returns>A fully configured <see cref="MarkdownPipeline"/>.</returns>
	private static MarkdownPipeline BuildPipeline(IReadOnlyList<string> ApprovedLinkDomains)
	{
		ShortDescriptionExtension ShortDescExt = new();
		ImageExtension ImageExt = new();
		HeaderImageExtension HeaderImageExt = new();
		EyebrowExtension EyebrowExt = new();
		SubtitleExtension SubtitleExt = new();
		DownloadsBoxExtension DownloadsBoxExt = new();
		PullQuoteExtension PullQuoteExt = new();

		CategoryExtension CategoryExt = new();
		FootnoteExtension FootnoteExt = new();
		CitationExtension CitationExt = new(ApprovedLinkDomains);

		PublicationIssueInfoboxExtension PublicationIssueInfoboxExt = new();
		CoverGridExtension CoverGridExt = new();
		CoverCarouselExtension CoverCarouselExt = new();
		TimelineExtension TimelineExt = new();
		YouTubeExtension YouTubeExt = new();
		StubExtension StubExt = new();
		SourceExtension SourceExt = new();

		ApprovedNoFollowLinksExtension LinksExt = new(ApprovedLinkDomains);

		MarkdownPipelineBuilder Builder = new MarkdownPipelineBuilder()
			.Use(ShortDescExt)
			.Use(ImageExt)
			.Use(HeaderImageExt)
			.Use(EyebrowExt)
			.Use(SubtitleExt)
			.Use(CategoryExt)
			.Use(FootnoteExt)
			.Use(CitationExt)
			.Use(PublicationIssueInfoboxExt)
			.Use(CoverGridExt)
			.Use(CoverCarouselExt)
			.Use(TimelineExt)
			.Use(new IssueCreditsExtension(ApprovedLinkDomains))
			.Use(YouTubeExt)
			.Use(DownloadsBoxExt)
			.Use(PullQuoteExt)
			.Use(StubExt)
			.Use(SourceExt)
			.Use(LinksExt)
			.UseAdvancedExtensions()
			.DisableHtml();

		Builder.Extensions.TryRemove<MediaLinkExtension>();

		return Builder.Build();
	}

	/// <summary>
	/// Builds the minimal, Reddit-style Markdown pipeline used to render talk posts.
	/// </summary>
	/// <remarks>
	/// Deliberately omits every WikiWikiWorld custom extension. Raw HTML is disabled so user-supplied
	/// content cannot inject markup; soft line breaks become hard breaks for chat-style flow.
	/// </remarks>
	/// <returns>A configured <see cref="MarkdownPipeline"/>.</returns>
	private static MarkdownPipeline BuildTalkPipeline()
	{
		return new MarkdownPipelineBuilder()
			.UseAutoLinks()
			.UseEmphasisExtras()
			.UseListExtras()
			.UsePipeTables()
			.UseSoftlineBreakAsHardlineBreak()
			.DisableHtml()
			.Build();
	}
}
