using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

// Block-level element for the DownloadsBox
public class DownloadsBoxBlock(BlockParser Parser) : ContainerBlock(Parser)
{
	public required String Hash { get; init; }
}

// Block parser for DownloadsBoxBlock
public class DownloadsBoxBlockParser : BlockParser
{
	private const String MarkerStart = "{{DownloadsBox ";
	private const String MarkerEnd = "}}";

	public DownloadsBoxBlockParser()
	{
		// Trigger when a line starts with '{'
		OpeningCharacters = ['{'];
	}

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		Markdig.Helpers.StringSlice Slice = Processor.Line;
		Slice.TrimStart(); // Trim leading whitespace
		Int32 StartPosition = Slice.Start;

		if (!Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		Slice.Start += MarkerStart.Length;
		Int32 EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return BlockState.None;
		}

		String HashContent = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
		if (String.IsNullOrWhiteSpace(HashContent))
		{
			Slice.Start = StartPosition;
			return BlockState.None;
		}

		DownloadsBoxBlock Block = new(this)
		{
			Hash = HashContent,
			Column = Processor.Column,
			Line = Processor.LineIndex,
			Span = new SourceSpan(Processor.Start, Processor.Line.End)
		};

		Processor.NewBlocks.Push(Block);
		Slice.Start = EndPos + MarkerEnd.Length;
		return BlockState.BreakDiscard;
	}
}

// Block renderer for DownloadsBoxBlock
public class DownloadsBoxBlockRenderer : HtmlObjectRenderer<DownloadsBoxBlock>
{
	private readonly Int32 SiteId;
	private readonly IDownloadUrlsRepository DownloadUrlsRepository;
	private readonly IUserRepository UserRepository;

	public DownloadsBoxBlockRenderer(Int32 SiteId, IDownloadUrlsRepository DownloadUrlsRepository, IUserRepository UserRepository)
	{
		this.SiteId = SiteId;
		this.DownloadUrlsRepository = DownloadUrlsRepository;
		this.UserRepository = UserRepository;
	}

	protected override void Write(HtmlRenderer Renderer, DownloadsBoxBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		// Start the downloads box
		Renderer.Write("<aside class=\"downloads-box\">");

		try
		{
			DownloadUrl? Download = DownloadUrlsRepository
				.GetByHashAsync(SiteId, Block.Hash)
				.GetAwaiter().GetResult();

			if (Download is not null)
			{
				RenderDownload(Renderer, Download);
			}
			else
			{
				Renderer.Write("<p class=\"no-downloads\">Download file not available.</p>");
			}
		}
		catch (Exception)
		{
			Renderer.Write("<p class=\"no-downloads\">Download file not available.</p>");
		}

		Renderer.Write("</aside>");
	}

	private void RenderDownload(HtmlRenderer Renderer, DownloadUrl Download)
	{
		String QualityText = Download.Quality.HasValue ? $"Quality: {Download.Quality}" : "";
		String FileSizeText = FormatFileSize(Download.FileSizeBytes);

		// Get username for the "Provided by" line
		String ProviderText = "";
		try
		{
			User? UploadUser = UserRepository
				.GetByIdAsync(Download.CreatedByUserId)
				.GetAwaiter().GetResult();

			if (UploadUser is not null)
			{
				ProviderText = $"Provided by <a href=\"/@{HtmlEscape(UploadUser.UserName)}\">@{HtmlEscape(UploadUser.UserName)}</a>";
			}
		}
		catch
		{
			// If we can't get the user info, we'll just skip the provider line
		}

		// Render file info
		Renderer.Write("<div class=\"file-info\">");
		Renderer.Write("<div class=\"file-icon\"><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1.5\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/><path d=\"M14 3v4a1 1 0 0 0 1 1h4\" /><path d=\"M5 12v-7a2 2 0 0 1 2 -2h7l5 5v4\" /><path d=\"M5 18h1.5a1.5 1.5 0 0 0 0 -3h-1.5v6\" /><path d=\"M17 18h2\" /><path d=\"M20 15h-3v6\" /><path d=\"M11 15v6h1a2 2 0 0 0 2 -2v-2a2 2 0 0 0 -2 -2h-1z\" /></svg></div>");
		Renderer.Write("<div class=\"text-content\">");
		Renderer.Write($"<p class=\"filename\">{HtmlEscape(Download.Filename)}</p>");
		Renderer.Write($"<p class=\"filesize\">{FileSizeText}</p>");
		if (!String.IsNullOrEmpty(QualityText))
		{
			Renderer.Write($"<p class=\"quality\">{QualityText}/5</p>");
		}
		if (!String.IsNullOrEmpty(ProviderText))
		{
			Renderer.Write($"<p class=\"provider\">{ProviderText}</p>");
		}
		Renderer.Write("</div>");
		Renderer.Write("</div>");

		// Render download button
		Renderer.Write($"<a class=\"button\" data-hash=\"{Download.HashSha256}\" href=\"{Download.DownloadUrls}\" target=\"_blank\">");
		Renderer.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" class=\"icon icon-tabler icons-tabler-outline icon-tabler-download\">");
		Renderer.Write("<path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/>");
		Renderer.Write("<path d=\"M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2 -2v-2\" />");
		Renderer.Write("<path d=\"M7 11l5 5l5 -5\" />");
		Renderer.Write("<path d=\"M12 4l0 12\" />");
		Renderer.Write("</svg>Download</a>");
	}

	private static String FormatFileSize(Int64 Bytes)
	{
		String[] Suffixes = { "B", "KB", "MB", "GB", "TB" };
		Int32 Order = 0;
		Double Size = Bytes;

		while (Size >= 1024 && Order < Suffixes.Length - 1)
		{
			Order++;
			Size /= 1024;
		}

		String FormattedSize = Size < 10
			? Size.ToString("0.##", CultureInfo.InvariantCulture)
			: Size.ToString("0", CultureInfo.InvariantCulture);

		return $"{FormattedSize} {Suffixes[Order]}";
	}

	private static String HtmlEscape(String Text) => Text
		.Replace("&", "&amp;")
		.Replace("<", "&lt;")
		.Replace(">", "&gt;")
		.Replace("\"", "&quot;")
		.Replace("'", "&#39;");
}

// Extension to register the DownloadsBox block parser and renderer
public class DownloadsBoxExtension : IMarkdownExtension
{
	private readonly Int32 SiteId;
	private readonly IDownloadUrlsRepository DownloadUrlsRepository;
	private readonly IUserRepository UserRepository;

	public DownloadsBoxExtension(Int32 SiteId, IDownloadUrlsRepository DownloadUrlsRepository, IUserRepository UserRepository)
	{
		this.SiteId = SiteId;
		this.DownloadUrlsRepository = DownloadUrlsRepository;
		this.UserRepository = UserRepository;
	}

	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<DownloadsBoxBlockParser>())
		{
			Pipeline.BlockParsers.Add(new DownloadsBoxBlockParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(r => r is DownloadsBoxBlockRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new DownloadsBoxBlockRenderer(SiteId, DownloadUrlsRepository, UserRepository));
		}
	}
}