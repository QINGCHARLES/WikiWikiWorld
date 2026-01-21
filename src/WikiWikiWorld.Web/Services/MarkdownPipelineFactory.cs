using Markdig;
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
	private readonly Lazy<MarkdownPipeline> Pipeline = new(BuildPipeline);

	/// <inheritdoc/>
	public MarkdownPipeline GetPipeline()
	{
		return Pipeline.Value;
	}

	/// <summary>
	/// Builds and configures the Markdown pipeline with all custom extensions.
	/// </summary>
	/// <returns>A fully configured <see cref="MarkdownPipeline"/>.</returns>
	private static MarkdownPipeline BuildPipeline()
	{
		ShortDescriptionExtension ShortDescExt = new();
		ImageExtension ImageExt = new();
		HeaderImageExtension HeaderImageExt = new();
		DownloadsBoxExtension DownloadsBoxExt = new();
		PullQuoteExtension PullQuoteExt = new();
		
		CategoryExtension CategoryExt = new();
		FootnoteExtension FootnoteExt = new();
		CitationExtension CitationExt = new();

		PublicationIssueInfoboxExtension PublicationIssueInfoboxExt = new();
		CoverGridExtension CoverGridExt = new();

		MarkdownPipelineBuilder Builder = new MarkdownPipelineBuilder()
			.Use(ShortDescExt)
			.Use(ImageExt)
			.Use(HeaderImageExt)
			.Use(CategoryExt)
			.Use(FootnoteExt)
			.Use(CitationExt)
			.Use(PublicationIssueInfoboxExt)
			.Use(CoverGridExt)
			.Use(DownloadsBoxExt)
			.Use(PullQuoteExt)
			.UseAdvancedExtensions();

		return Builder.Build();
	}
}
