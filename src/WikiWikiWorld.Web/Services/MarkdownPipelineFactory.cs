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
	/// Gets the cached minimal Markdown pipeline used for short user content, including discussions, messages and notes.
	/// </summary>
	/// <returns>A configured <see cref="MarkdownPipeline"/> limited to safe inline formatting, lists, links and quotes.</returns>
	MarkdownPipeline GetUserContentPipeline();

	/// <summary>
	/// Renders short user content as HTML with basic Markdown formatting and the shared URL safety policy.
	/// </summary>
	/// <param name="Source">The Markdown source. <see langword="null"/> or whitespace renders to <see cref="string.Empty"/>.</param>
	/// <returns>The rendered HTML string. Callers are expected to wrap it with <c>Html.Raw</c>.</returns>
	string RenderUserContentMarkdown(string? Source);
}

/// <summary>
/// Implementation of <see cref="IMarkdownPipelineFactory"/> that caches a single pipeline instance per pipeline kind.
/// </summary>
public sealed class MarkdownPipelineFactory : IMarkdownPipelineFactory
{
	private readonly Lazy<MarkdownPipeline> Pipeline;
	private readonly Lazy<MarkdownPipeline> UserContentPipeline;

	/// <summary>
	/// Initializes a new instance of the <see cref="MarkdownPipelineFactory"/> class.
	/// </summary>
	/// <param name="SiteConfiguration">The site configuration options.</param>
	public MarkdownPipelineFactory(IOptions<SiteConfiguration> SiteConfiguration)
	{
		this.Pipeline = new Lazy<MarkdownPipeline>(() => BuildPipeline(SiteConfiguration.Value.ApprovedLinkDomains));
		this.UserContentPipeline = new Lazy<MarkdownPipeline>(BuildUserContentPipeline);
	}

	/// <inheritdoc/>
	public MarkdownPipeline GetPipeline()
	{
		return Pipeline.Value;
	}

	/// <inheritdoc/>
	public MarkdownPipeline GetUserContentPipeline()
	{
		return UserContentPipeline.Value;
	}

	/// <inheritdoc/>
	public string RenderUserContentMarkdown(string? Source)
	{
		if (string.IsNullOrWhiteSpace(Source))
		{
			return string.Empty;
		}

		MarkdownPipeline UserContentPipelineInstance = UserContentPipeline.Value;
		StringWriter Writer = new();
		HtmlRenderer Renderer = new(Writer);
		UserContentPipelineInstance.Setup(Renderer);
		Renderer.Render(Markdown.Parse(Source, UserContentPipelineInstance));
		Writer.Flush();
		return Writer.ToString();
	}

	/// <summary>
	/// Builds the article pipeline with authored-attribute/URL restrictions before the trusted external-link policy.
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
			.Use(new CategoryArticlesExtension())
			.Use(FootnoteExt)
			.Use(CitationExt)
			.Use(PublicationIssueInfoboxExt)
			.Use(CoverGridExt)
			.Use(CoverCarouselExt)
			.Use(TimelineExt)
			.Use(new IssueCreditsExtension(ApprovedLinkDomains))
			.Use(new IssueContentsExtension())
			.Use(YouTubeExt)
			.Use(DownloadsBoxExt)
			.Use(PullQuoteExt)
			.Use(StubExt)
			.Use(SourceExt)
			.Use(new MarkdownSafetyExtension())
			.Use(LinksExt)
			.UseAdvancedExtensions()
			.DisableHtml();

		Builder.Extensions.TryRemove<MediaLinkExtension>();

		return Builder.Build();
	}

	/// <summary>
	/// Builds the minimal Markdown pipeline shared by discussions, messages and notes.
	/// </summary>
	/// <remarks>
	/// Deliberately omits wiki directives and generic attributes. Raw HTML is disabled so user-supplied
	/// content cannot inject markup; the shared safety policy rejects executable URL schemes.
	/// Soft line breaks become hard breaks to preserve authored line breaks.
	/// </remarks>
	/// <returns>A configured <see cref="MarkdownPipeline"/>.</returns>
	private static MarkdownPipeline BuildUserContentPipeline()
	{
		return new MarkdownPipelineBuilder()
			.UseAutoLinks()
			.UseEmphasisExtras()
			.UseListExtras()
			.UsePipeTables()
			.UseSoftlineBreakAsHardlineBreak()
			.Use(new MarkdownSafetyExtension())
			.DisableHtml()
			.Build();
	}
}
