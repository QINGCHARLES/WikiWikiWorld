using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.MarkdigExtensions;



/// <summary>
/// Represents the citation inline element in the markdown AST
/// </summary>
public sealed class CitationInline : LeafInline
{
	public required StringSlice Data { get; init; }
	public required string CitationId { get; init; } // For cross-referencing
	public int CitationNumber { get; set; } // Will be set by the renderer
}

/// <summary>
/// Main extension class that registers the parser and renderer for inline citations
/// </summary>
public sealed class CitationExtension(Dictionary<string, Citation> Citations) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<CitationParser>())
		{
			Pipeline.InlineParsers.Add(new CitationParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<CitationRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new CitationRenderer(Citations));
		}
	}
}

/// <summary>
/// Parser for citation syntax {{Citation author=Smith |#| year=2023}}
/// </summary>
public sealed class CitationParser : InlineParser
{
	private const string MarkerStart = "{{Citation ";
	private const string MarkerEnd = "}}";

	public CitationParser()
	{
		OpeningCharacters = ['{'];
	}

	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;

		// Check if the slice starts with the marker
		if (!Slice.Match(MarkerStart))
		{
			return false;
		}

		// Move past the marker
		Slice.Start += MarkerStart.Length;

		// Find the end marker
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		if (EndPos == -1)
		{
			// No end marker found, reset and return false
			Slice.Start = StartPosition;
			return false;
		}

		// Extract the citation content
		string CitationContent = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();

		// Generate a citation ID based on the content (used for cross-referencing)
		string CitationId = GenerateCitationId(CitationContent);

		// Create the inline
		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);
		Processor.Inline = new CitationInline
		{
			Span =
			{
				Start = InlineStart,
				End = InlineStart + (EndPos - Slice.Start)
			},
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1),
			CitationId = CitationId
		};

		// Move past the end marker
		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}

	/// <summary>
	/// Generates a unique ID for a citation based on its content
	/// </summary>
	private static string GenerateCitationId(string Content)
	{
		// Use a simple deterministic hash to create an ID
		// This ensures the same citation content gets the same ID
		int Hash = 0;
		foreach (char C in Content)
		{
			Hash = (Hash * 31) + C;
		}
		return $"cit_{Math.Abs(Hash)}";
	}
}

/// <summary>
/// Renderer that outputs a cross/dagger with a superscript number for the citation
/// </summary>
public sealed class CitationRenderer : HtmlObjectRenderer<CitationInline>
{
	private readonly Dictionary<string, Citation> Citations;

	public CitationRenderer(Dictionary<string, Citation> Citations)
	{
		this.Citations = Citations;
	}

	protected override void Write(HtmlRenderer Renderer, CitationInline Inline)
	{
		// Process the citation content
		Citation CurrentCitation = ProcessCitation(Inline.Data.ToString(), Inline.CitationId);

		// Render the citation marker (cross/dagger + number)
		Renderer.Write("<sup id=\"citref:");
		Renderer.Write(CurrentCitation.Number.ToString());
		Renderer.Write("\" class=\"citation-ref\">");
		Renderer.Write("<a href=\"#cit:");
		Renderer.Write(CurrentCitation.Number.ToString());
		Renderer.Write("\">");
		Renderer.Write("†"); // Cross/dagger symbol
		Renderer.Write(CurrentCitation.Number.ToString());
		Renderer.Write("</a>");
		Renderer.Write("</sup>");
	}

	/// <summary>
	/// Processes a citation, adding it to the dictionary if it doesn't exist
	/// </summary>
	private Citation ProcessCitation(string Content, string CitationId)
	{
		// If this citation ID already exists, just return it
		if (Citations.TryGetValue(CitationId, out Citation? ExistingCitation))
		{
			// Add this location as a reference
			ExistingCitation.ReferencedBy.Add($"citref:{ExistingCitation.Number}");
			return ExistingCitation;
		}

		// Parse the citation properties
		Dictionary<string, List<string>> Properties = new(StringComparer.OrdinalIgnoreCase);

		const string AttributeSeparator = "|#|";
		string[] Tokens = Content.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

		foreach (string Token in Tokens)
		{
			string Trimmed = Token.Trim();
			if (string.IsNullOrEmpty(Trimmed))
			{
				continue;
			}

			int EqPos = Trimmed.IndexOf('=');
			if (EqPos <= 0)
			{
			continue;
		}

		string Key = Trimmed[..EqPos].Trim();
		string Value = Trimmed[(EqPos + 1)..].Trim();

		if (!Properties.TryGetValue(Key, out List<string>? List))
		{
			List = [];
			Properties[Key] = List;
		}
		List.Add(Value);
	}		// Create a new citation
		int Number = Citations.Count + 1;
		Citation NewCitation = new()
		{
			Number = Number,
			Id = CitationId,
			Properties = Properties,
			ReferencedBy = [$"citref:{Number}"]
		};

		Citations[CitationId] = NewCitation;
		return NewCitation;
	}
}
