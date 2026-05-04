using Markdig;
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
	/// Gets a cached Markdown pipeline.
	/// </summary>
	/// <returns>A configured <see cref="MarkdownPipeline"/>.</returns>
	MarkdownPipeline GetPipeline();
}

/// <summary>
/// Implementation of <see cref="IMarkdownPipelineFactory"/> that caches a single pipeline instance.
/// </summary>
public sealed class MarkdownPipelineFactory : IMarkdownPipelineFactory
{
	private readonly Lazy<MarkdownPipeline> Pipeline;

	/// <summary>
	/// Initializes a new instance of the <see cref="MarkdownPipelineFactory"/> class.
	/// </summary>
	/// <param name="SiteConfiguration">The site configuration options.</param>
	public MarkdownPipelineFactory(IOptions<SiteConfiguration> SiteConfiguration)
	{
		this.Pipeline = new Lazy<MarkdownPipeline>(() => BuildPipeline(SiteConfiguration.Value.ApprovedLinkDomains));
	}

	/// <inheritdoc/>
	public MarkdownPipeline GetPipeline()
	{
		return Pipeline.Value;
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
		DownloadsBoxExtension DownloadsBoxExt = new();
		PullQuoteExtension PullQuoteExt = new();

		CategoryExtension CategoryExt = new();
		FootnoteExtension FootnoteExt = new();
		CitationExtension CitationExt = new(ApprovedLinkDomains);

		PublicationIssueInfoboxExtension PublicationIssueInfoboxExt = new();
		CoverGridExtension CoverGridExt = new();
		StubExtension StubExt = new();
		SourceExtension SourceExt = new();
		
		ApprovedNoFollowLinksExtension LinksExt = new(ApprovedLinkDomains);

		MarkdownPipelineBuilder Builder = new MarkdownPipelineBuilder()
			.Use(ShortDescExt)
			.Use(ImageExt)
			.Use(HeaderImageExt)
			.Use(EyebrowExt)
			.Use(CategoryExt)
			.Use(FootnoteExt)
			.Use(CitationExt)
			.Use(PublicationIssueInfoboxExt)
			.Use(CoverGridExt)
			.Use(DownloadsBoxExt)
			.Use(PullQuoteExt)
			.Use(StubExt)
			.Use(SourceExt)
			.Use(LinksExt)
			.UseAdvancedExtensions();

		return Builder.Build();
	}
}
