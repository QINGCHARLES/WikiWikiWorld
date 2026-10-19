using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Contains the validated author-supplied values for a YouTube embed directive.
/// </summary>
/// <param name="VideoId">The validated 11-character YouTube video identifier.</param>
/// <param name="Title">The accessible video title supplied by the author.</param>
/// <param name="StartSeconds">The non-negative playback start position in seconds.</param>
/// <param name="Caption">The optional plain-text figure caption.</param>
internal sealed record class YouTubeDirective(
	string VideoId,
	string Title,
	int StartSeconds,
	string? Caption);

/// <summary>
/// Represents a validated <c>{{YouTube ...}}</c> article block.
/// </summary>
/// <param name="Parser">The block parser that created the block.</param>
public sealed class YouTubeBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets the validated 11-character YouTube video identifier.
	/// </summary>
	public required string VideoId { get; init; }

	/// <summary>
	/// Gets the accessible video title supplied by the author.
	/// </summary>
	public required string Title { get; init; }

	/// <summary>
	/// Gets the non-negative playback start position in seconds.
	/// </summary>
	public int StartSeconds { get; init; }

	/// <summary>
	/// Gets the optional plain-text figure caption.
	/// </summary>
	public string? Caption { get; init; }
}

/// <summary>
/// Parses standalone <c>{{YouTube ...}}</c> article blocks.
/// </summary>
public sealed class YouTubeBlockParser : BlockParser
{
	private const string MarkerStart = "{{YouTube ";
	private const string MarkerEnd = "}}";
	private const string PropertySeparator = "|#|";

	/// <summary>
	/// Initializes a new instance of the <see cref="YouTubeBlockParser"/> class.
	/// </summary>
	public YouTubeBlockParser()
	{
		OpeningCharacters = ['{'];
	}

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		ArgumentNullException.ThrowIfNull(Processor);

		StringSlice Line = Processor.Line;
		string Source = Line.ToString().Trim();
		if (!TryParseDirective(Source, out YouTubeDirective? Directive))
		{
			return BlockState.None;
		}

		YouTubeBlock Block = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column,
			VideoId = Directive.VideoId,
			Title = Directive.Title,
			StartSeconds = Directive.StartSeconds,
			Caption = Directive.Caption
		};

		Processor.NewBlocks.Push(Block);
		return BlockState.BreakDiscard;
	}

	/// <summary>
	/// Parses and validates a complete YouTube directive source line.
	/// </summary>
	/// <param name="Source">The complete directive source line.</param>
	/// <param name="Directive">The validated directive when parsing succeeds.</param>
	/// <returns><see langword="true"/> when the directive is valid; otherwise, <see langword="false"/>.</returns>
	internal static bool TryParseDirective(
		string Source,
		[NotNullWhen(true)] out YouTubeDirective? Directive)
	{
		Directive = null;
		if (!Source.StartsWith(MarkerStart, StringComparison.Ordinal) ||
			!Source.EndsWith(MarkerEnd, StringComparison.Ordinal))
		{
			return false;
		}

		string Payload = Source[MarkerStart.Length..^MarkerEnd.Length].Trim();
		string[] Parts = Payload.Split(PropertySeparator, StringSplitOptions.None);
		if (Parts.Length < 2)
		{
			return false;
		}

		string VideoReference = Parts[0].Trim();
		string? Title = null;
		string? Caption = null;
		int? ExplicitStartSeconds = null;
		HashSet<string> SeenPropertyNames = new(StringComparer.OrdinalIgnoreCase);

		for (int i = 1; i < Parts.Length; i++)
		{
			string Property = Parts[i].Trim();
			int EqualsPosition = Property.IndexOf('=');
			if (EqualsPosition <= 0)
			{
				return false;
			}

			string PropertyName = Property[..EqualsPosition].Trim();
			string PropertyValue = Property[(EqualsPosition + 1)..].Trim();
			if (!SeenPropertyNames.Add(PropertyName))
			{
				return false;
			}

			if (PropertyName.Equals("Title", StringComparison.OrdinalIgnoreCase))
			{
				if (string.IsNullOrWhiteSpace(PropertyValue))
				{
					return false;
				}

				Title = PropertyValue;
			}
			else if (PropertyName.Equals("StartSeconds", StringComparison.OrdinalIgnoreCase))
			{
				if (!int.TryParse(
					PropertyValue,
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out int ParsedStartSeconds) ||
					ParsedStartSeconds < 0)
				{
					return false;
				}

				ExplicitStartSeconds = ParsedStartSeconds;
			}
			else if (PropertyName.Equals("Caption", StringComparison.OrdinalIgnoreCase))
			{
				Caption = string.IsNullOrWhiteSpace(PropertyValue) ? null : PropertyValue;
			}
			else
			{
				return false;
			}
		}

		if (Title is null ||
			!TryParseVideoReference(VideoReference, out string? VideoId, out int UrlStartSeconds))
		{
			return false;
		}

		Directive = new YouTubeDirective(
			VideoId,
			Title,
			ExplicitStartSeconds ?? UrlStartSeconds,
			Caption);
		return true;
	}

	/// <summary>
	/// Parses a bare video identifier or an allowlisted YouTube URL.
	/// </summary>
	/// <param name="VideoReference">The author-supplied video reference.</param>
	/// <param name="VideoId">The validated video identifier.</param>
	/// <param name="StartSeconds">The playback start position extracted from the URL.</param>
	/// <returns><see langword="true"/> when the reference is valid; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseVideoReference(
		string VideoReference,
		[NotNullWhen(true)] out string? VideoId,
		out int StartSeconds)
	{
		VideoId = null;
		StartSeconds = 0;

		if (IsValidVideoId(VideoReference))
		{
			VideoId = VideoReference;
			return true;
		}

		if (!Uri.TryCreate(VideoReference, UriKind.Absolute, out Uri? VideoUri) ||
			VideoUri.Scheme is not ("http" or "https") ||
			!VideoUri.IsDefaultPort ||
			VideoUri.UserInfo.Length > 0 ||
			VideoUri.Fragment.Length > 0)
		{
			return false;
		}

		Dictionary<string, StringValues> QueryParameters = QueryHelpers.ParseQuery(VideoUri.Query);
		string[] PathSegments = VideoUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		string Host = VideoUri.IdnHost;

		if (Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
		{
			if (PathSegments.Length != 1 ||
				!AreQueryParametersAllowed(QueryParameters, "t", "start", "si", "feature"))
			{
				return false;
			}

			VideoId = PathSegments[0];
		}
		else if (Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
			Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
			Host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase))
		{
			if (PathSegments.Length == 1 &&
				PathSegments[0].Equals("watch", StringComparison.OrdinalIgnoreCase) &&
				AreQueryParametersAllowed(QueryParameters, "v", "t", "start", "si", "feature") &&
				TryGetSingleQueryValue(QueryParameters, "v", out string? WatchVideoId))
			{
				VideoId = WatchVideoId;
			}
			else if (PathSegments.Length == 2 &&
				(PathSegments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
				 PathSegments[0].Equals("embed", StringComparison.OrdinalIgnoreCase)) &&
				AreQueryParametersAllowed(QueryParameters, "t", "start", "si", "feature"))
			{
				VideoId = PathSegments[1];
			}
			else
			{
				return false;
			}
		}
		else if (Host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
			Host.Equals("www.youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
		{
			if (PathSegments.Length != 2 ||
				!PathSegments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
				!AreQueryParametersAllowed(QueryParameters, "t", "start"))
			{
				return false;
			}

			VideoId = PathSegments[1];
		}
		else
		{
			return false;
		}

		return IsValidVideoId(VideoId) && TryGetStartSeconds(QueryParameters, out StartSeconds);
	}

	/// <summary>
	/// Determines whether every URL query parameter is explicitly allowlisted and occurs once.
	/// </summary>
	/// <param name="QueryParameters">The parsed query parameters.</param>
	/// <param name="AllowedParameterNames">The permitted parameter names.</param>
	/// <returns><see langword="true"/> when every parameter is allowed; otherwise, <see langword="false"/>.</returns>
	private static bool AreQueryParametersAllowed(
		IReadOnlyDictionary<string, StringValues> QueryParameters,
		params ReadOnlySpan<string> AllowedParameterNames)
	{
		foreach (KeyValuePair<string, StringValues> QueryParameter in QueryParameters)
		{
			bool IsAllowed = false;
			foreach (string AllowedParameterName in AllowedParameterNames)
			{
				if (QueryParameter.Key.Equals(AllowedParameterName, StringComparison.Ordinal))
				{
					IsAllowed = true;
					break;
				}
			}

			if (!IsAllowed || QueryParameter.Value.Count != 1)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Gets a single query-string value.
	/// </summary>
	/// <param name="QueryParameters">The parsed query parameters.</param>
	/// <param name="ParameterName">The parameter name to retrieve.</param>
	/// <param name="Value">The single non-empty value.</param>
	/// <returns><see langword="true"/> when the value exists exactly once; otherwise, <see langword="false"/>.</returns>
	private static bool TryGetSingleQueryValue(
		IReadOnlyDictionary<string, StringValues> QueryParameters,
		string ParameterName,
		[NotNullWhen(true)] out string? Value)
	{
		Value = null;
		if (!QueryParameters.TryGetValue(ParameterName, out StringValues Values) || Values.Count != 1)
		{
			return false;
		}

		string? Candidate = Values[0];
		if (string.IsNullOrWhiteSpace(Candidate))
		{
			return false;
		}

		Value = Candidate;
		return true;
	}

	/// <summary>
	/// Extracts an optional playback start position from an allowlisted YouTube URL.
	/// </summary>
	/// <param name="QueryParameters">The parsed query parameters.</param>
	/// <param name="StartSeconds">The extracted non-negative start position.</param>
	/// <returns><see langword="true"/> when the timestamp is absent or valid; otherwise, <see langword="false"/>.</returns>
	private static bool TryGetStartSeconds(
		IReadOnlyDictionary<string, StringValues> QueryParameters,
		out int StartSeconds)
	{
		StartSeconds = 0;
		bool HasStart = TryGetSingleQueryValue(QueryParameters, "start", out string? StartValue);
		bool HasTime = TryGetSingleQueryValue(QueryParameters, "t", out string? TimeValue);

		if (HasStart && HasTime)
		{
			return false;
		}

		if (HasStart)
		{
			return int.TryParse(
				StartValue,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out StartSeconds);
		}

		if (!HasTime)
		{
			return true;
		}

		return TryParseTimeToSeconds(TimeValue!, out StartSeconds);
	}

	/// <summary>
	/// Parses a YouTube timestamp expressed as seconds or as an hours/minutes/seconds sequence.
	/// </summary>
	/// <param name="Value">The timestamp text.</param>
	/// <param name="TotalSeconds">The parsed total number of seconds.</param>
	/// <returns><see langword="true"/> when the timestamp is valid; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseTimeToSeconds(string Value, out int TotalSeconds)
	{
		TotalSeconds = 0;
		if (int.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out int Seconds))
		{
			TotalSeconds = Seconds;
			return true;
		}

		long AccumulatedSeconds = 0;
		long CurrentNumber = 0;
		bool HasCurrentDigits = false;
		int PreviousUnitRank = 4;

		foreach (char Character in Value)
		{
			if (Character is >= '0' and <= '9')
			{
				HasCurrentDigits = true;
				int Digit = Character - '0';
				if (CurrentNumber > (long.MaxValue - Digit) / 10)
				{
					return false;
				}

				CurrentNumber = (CurrentNumber * 10) + Digit;
				continue;
			}

			int UnitRank;
			int UnitSeconds;
			if (Character == 'h')
			{
				UnitRank = 3;
				UnitSeconds = 3_600;
			}
			else if (Character == 'm')
			{
				UnitRank = 2;
				UnitSeconds = 60;
			}
			else if (Character == 's')
			{
				UnitRank = 1;
				UnitSeconds = 1;
			}
			else
			{
				return false;
			}

			if (!HasCurrentDigits || UnitRank >= PreviousUnitRank)
			{
				return false;
			}

			if (CurrentNumber > (int.MaxValue - AccumulatedSeconds) / UnitSeconds)
			{
				return false;
			}

			AccumulatedSeconds += CurrentNumber * UnitSeconds;

			CurrentNumber = 0;
			HasCurrentDigits = false;
			PreviousUnitRank = UnitRank;
		}

		if (HasCurrentDigits || PreviousUnitRank == 4)
		{
			return false;
		}

		TotalSeconds = (int)AccumulatedSeconds;
		return true;
	}

	/// <summary>
	/// Determines whether a string is a valid YouTube video identifier.
	/// </summary>
	/// <param name="VideoId">The candidate identifier.</param>
	/// <returns><see langword="true"/> when the identifier is valid; otherwise, <see langword="false"/>.</returns>
	private static bool IsValidVideoId([NotNullWhen(true)] string? VideoId)
	{
		if (VideoId is not { Length: 11 })
		{
			return false;
		}

		foreach (char Character in VideoId)
		{
			if (Character is not (>= 'a' and <= 'z') and
				not (>= 'A' and <= 'Z') and
				not (>= '0' and <= '9') and
				not '_' and
				not '-')
			{
				return false;
			}
		}

		return true;
	}
}

/// <summary>
/// Renders a validated YouTube block as a local, progressively enhanced click-to-load figure.
/// </summary>
public sealed class YouTubeBlockRenderer : HtmlObjectRenderer<YouTubeBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, YouTubeBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		string EncodedTitle = WebUtility.HtmlEncode(Block.Title);
		string FallbackUrl = $"https://www.youtube.com/watch?v={Block.VideoId}";
		if (Block.StartSeconds > 0)
		{
			FallbackUrl += $"&t={Block.StartSeconds.ToString(CultureInfo.InvariantCulture)}s";
		}

		Renderer.Write("<figure class=\"youtube-embed\">");
		Renderer.Write("<a class=\"youtube-embed__play\" href=\"");
		Renderer.Write(WebUtility.HtmlEncode(FallbackUrl));
		Renderer.Write("\" target=\"_blank\" rel=\"nofollow noopener\" data-youtube-video-id=\"");
		Renderer.Write(Block.VideoId);
		Renderer.Write('"');

		if (Block.StartSeconds > 0)
		{
			Renderer.Write(" data-youtube-start-seconds=\"");
			Renderer.Write(Block.StartSeconds.ToString(CultureInfo.InvariantCulture));
			Renderer.Write('"');
		}

		Renderer.Write(" aria-label=\"Play ");
		Renderer.Write(EncodedTitle);
		Renderer.Write(" on YouTube\">");
		Renderer.Write("<span class=\"youtube-embed__play-icon\" aria-hidden=\"true\"></span>");
		Renderer.Write("<span class=\"youtube-embed__title\">");
		Renderer.Write(EncodedTitle);
		Renderer.Write("</span><span class=\"youtube-embed__provider\">YouTube</span></a>");

		if (!string.IsNullOrWhiteSpace(Block.Caption))
		{
			Renderer.Write("<figcaption>");
			Renderer.Write(WebUtility.HtmlEncode(Block.Caption));
			Renderer.Write("</figcaption>");
		}

		Renderer.Write("</figure>");
	}
}

/// <summary>
/// Adds controlled, click-to-load YouTube embeds to the full article Markdown pipeline.
/// </summary>
public sealed class YouTubeExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);

		if (!Pipeline.BlockParsers.Contains<YouTubeBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new YouTubeBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);
		ArgumentNullException.ThrowIfNull(Renderer);

		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Contains<YouTubeBlockRenderer>())
		{
			HtmlRenderer.ObjectRenderers.Add(new YouTubeBlockRenderer());
		}
	}
}
