using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A validated <c>{{YouTubeLink ...}}</c> reference: the YouTube address it points to and the label readers see.
/// </summary>
/// <param name="Url">The canonical https://www.youtube.com address built from the validated video, channel or handle.</param>
/// <param name="Label">The plain-text link label supplied by the author.</param>
internal sealed record class YouTubeLinkTarget(string Url, string Label);

/// <summary>
/// An inline link to a YouTube video, channel or handle, written as <c>{{YouTubeLink ...}}</c>.
/// </summary>
public sealed class YouTubeLinkInline : LeafInline
{
	/// <summary>Gets the canonical YouTube address the link points to.</summary>
	public required string Url { get; init; }

	/// <summary>Gets the plain-text label readers see.</summary>
	public required string Label { get; init; }
}

/// <summary>
/// Parses <c>{{YouTubeLink Video=… |#| Label=…}}</c>, <c>{{YouTubeLink Channel=UC… |#| Label=…}}</c> and
/// <c>{{YouTubeLink Handle=@… |#| Label=…}}</c>.
/// </summary>
/// <remarks>
/// Authors name a YouTube video or channel in prose without pasting its address or raw ID. An invalid directive
/// is left as literal text, and <see cref="WikiWikiWorld.Web.Services.ArticleSaveWarnings"/> reports it when the article is saved.
/// </remarks>
public sealed class YouTubeLinkParser : InlineParser
{
	/// <summary>The directive's opening marker, including the space before its first property.</summary>
	internal const string MarkerStart = "{{YouTubeLink ";

	/// <summary>The properties a directive may set, matched without regard to case.</summary>
	private static readonly FrozenSet<string> PropertyNames =
		FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "Video", "Channel", "Handle", "Label");

	/// <summary>
	/// Initializes a new instance of the <see cref="YouTubeLinkParser"/> class, triggered by <c>{</c>.
	/// </summary>
	public YouTubeLinkParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		if (!Slice.Match(MarkerStart)) return false;

		int PayloadStart = Slice.Start + MarkerStart.Length;
		int EndPosition = DirectiveSyntax.FindClosingDelimiter(Slice.Text, PayloadStart);
		if (EndPosition < 0 || EndPosition + 1 > Slice.End) return false;

		if (!TryParsePayload(Slice.Text[PayloadStart..EndPosition], out YouTubeLinkTarget? Target, out _)) return false;

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);
		Processor.Inline = new YouTubeLinkInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPosition + 1 - Slice.Start)),
			Line = Line,
			Column = Column,
			Url = Target.Url,
			Label = Target.Label,
		};

		Slice.Start = EndPosition + 2;
		return true;
	}

	/// <summary>
	/// Validates a directive payload (the text between the opening marker and the closing braces).
	/// </summary>
	/// <param name="Payload">The <c>|#|</c>-separated properties.</param>
	/// <param name="Target">The validated link when parsing succeeds.</param>
	/// <param name="Problem">Why the payload is invalid, in plain English, when parsing fails.</param>
	/// <returns><see langword="true"/> when exactly one of <c>Video</c>, <c>Channel</c> or <c>Handle</c> is valid and
	/// <c>Label</c> is present; otherwise, <see langword="false"/>.</returns>
	internal static bool TryParsePayload(
		string Payload,
		[NotNullWhen(true)] out YouTubeLinkTarget? Target,
		[NotNullWhen(false)] out string? Problem)
	{
		Target = null;
		Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase);
		foreach (string Property in DirectiveSyntax.SplitProperties(Payload))
		{
			int EqualsPosition = Property.IndexOf('=');
			string Name = EqualsPosition > 0 ? Property[..EqualsPosition].Trim() : string.Empty;
			if (!PropertyNames.Contains(Name))
			{
				Problem = $"\"{Property.Trim()}\" is not a YouTubeLink property; use Video=, Channel= or Handle=, and Label=.";
				return false;
			}
			if (!Properties.TryAdd(Name, Property[(EqualsPosition + 1)..].Trim()))
			{
				Problem = $"{Name}= is written more than once.";
				return false;
			}
		}

		if (!Properties.TryGetValue("Label", out string? Label) || Label.Length == 0)
		{
			Problem = "Label= is missing: give the name readers should see, such as the channel's name.";
			return false;
		}

		int TargetCount = (Properties.ContainsKey("Video") ? 1 : 0) + (Properties.ContainsKey("Channel") ? 1 : 0)
			+ (Properties.ContainsKey("Handle") ? 1 : 0);
		if (TargetCount != 1)
		{
			Problem = "Give exactly one of Video=, Channel= or Handle=.";
			return false;
		}

		string? Url = null;
		if (Properties.TryGetValue("Video", out string? Video))
		{
			if (YouTubeBlockParser.TryParseVideoReference(Video, out string? VideoId, out int StartSeconds))
			{
				Url = $"https://www.youtube.com/watch?v={VideoId}"
					+ (StartSeconds > 0 ? $"&t={StartSeconds.ToString(CultureInfo.InvariantCulture)}s" : string.Empty);
			}
		}
		else if (Properties.TryGetValue("Channel", out string? Channel))
		{
			if (IsValidChannelId(Channel)) Url = $"https://www.youtube.com/channel/{Channel}";
		}
		else if (TryNormalizeHandle(Properties["Handle"], out string? Handle))
		{
			Url = $"https://www.youtube.com/@{Uri.EscapeDataString(Handle)}";
		}

		if (Url is null)
		{
			Problem = "The Video=, Channel= or Handle= value is not valid: a video is an 11-character ID or a YouTube "
				+ "video address, a channel ID is UC followed by 22 characters, and a handle is @ followed by 3 to 30 "
				+ "letters, digits, dots, hyphens or underscores.";
			return false;
		}

		Target = new YouTubeLinkTarget(Url, Label);
		Problem = null;
		return true;
	}

	/// <summary>
	/// Tells whether a value is a YouTube channel ID: <c>UC</c> followed by 22 URL-safe base64 characters.
	/// </summary>
	/// <param name="Value">The candidate channel ID.</param>
	/// <returns><see langword="true"/> for a well-formed channel ID.</returns>
	internal static bool IsValidChannelId(string Value) =>
		Value.Length == 24 && Value.StartsWith("UC", StringComparison.Ordinal)
		&& Value.AsSpan(2).IndexOfAnyExcept(UrlSafeBase64) < 0;

	/// <summary>Letters, digits, <c>-</c> and <c>_</c>: the alphabet of YouTube video and channel IDs.</summary>
	private static readonly SearchValues<char> UrlSafeBase64 =
		SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

	/// <summary>
	/// Accepts a YouTube handle with or without its leading <c>@</c>.
	/// </summary>
	/// <param name="Value">The author-supplied handle.</param>
	/// <param name="Handle">The handle without <c>@</c> when it is valid.</param>
	/// <returns><see langword="true"/> for 3 to 30 letters, digits, dots, hyphens or underscores.</returns>
	private static bool TryNormalizeHandle(string Value, [NotNullWhen(true)] out string? Handle)
	{
		string Candidate = Value.StartsWith('@') ? Value[1..] : Value;
		Handle = Candidate.Length is >= 3 and <= 30
			&& Candidate.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_') ? Candidate : null;
		return Handle is not null;
	}
}

/// <summary>
/// Renders a <see cref="YouTubeLinkInline"/> as an ordinary link marked as going to YouTube.
/// </summary>
/// <remarks>
/// The play icon comes from CSS (<c>a.youtube-link</c>); a visually hidden "(YouTube)" tells screen-reader users
/// where the link goes. Links are always <c>nofollow</c>, like the <c>{{YouTube}}</c> embed's fallback link.
/// </remarks>
public sealed class YouTubeLinkRenderer : HtmlObjectRenderer<YouTubeLinkInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, YouTubeLinkInline Inline)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Inline);

		Renderer.Write("<a class=\"youtube-link\" href=\"");
		Renderer.Write(WebUtility.HtmlEncode(Inline.Url));
		Renderer.Write("\" rel=\"nofollow noopener\">");
		Renderer.WriteEscape(Inline.Label);
		Renderer.Write("<span class=\"visually-hidden\"> (YouTube)</span></a>");
	}
}

/// <summary>
/// Adds the <c>{{YouTubeLink ...}}</c> inline directive to a pipeline.
/// </summary>
public sealed class YouTubeLinkExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<YouTubeLinkParser>())
			Pipeline.InlineParsers.Add(new YouTubeLinkParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Contains<YouTubeLinkRenderer>())
			Html.ObjectRenderers.Add(new YouTubeLinkRenderer());
	}
}
