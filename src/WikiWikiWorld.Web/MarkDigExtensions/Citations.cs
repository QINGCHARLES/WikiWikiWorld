using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.Web.MarkdigExtensions;

// --- Data Models (AST Nodes) ---

/// <summary>
/// A Markdown inline element representing a citation reference (e.g., [1]).
/// </summary>
public sealed class CitationInline : LeafInline
{
	/// <summary>
	/// Gets or initializes the raw content of the citation tag.
	/// </summary>
	public required StringSlice Data { get; init; }

	/// <summary>
	/// Gets or initializes the unique identifier for this citation.
	/// </summary>
	public required string CitationId { get; init; }

	/// <summary>
	/// Gets or sets the assigned sequential number for this citation.
	/// </summary>
	public int CitationNumber { get; set; }

	/// <summary>
	/// Gets or sets the HTML ID used for back-reference linking.
	/// </summary>
	public string BackRefId { get; set; } = string.Empty;
}

/// <summary>
/// Represents additional metadata and properties for a single citation.
/// </summary>
public class Citation
{
	/// <summary>
	/// Gets or sets the sequential number assigned to this citation.
	/// </summary>
	public int Number { get; set; }

	/// <summary>
	/// Gets or sets the unique string identifier for this citation.
	/// </summary>
	public required string Id { get; set; }

	/// <summary>
	/// Gets or sets the collection of key-value properties parsed from the citation.
	/// </summary>
	public required Dictionary<string, List<string>> Properties { get; set; }

	/// <summary>
	/// Gets or sets a list of IDs of elements that reference this citation.
	/// </summary>
	public required List<string> ReferencedBy { get; set; }
}

/// <summary>
/// A block element representing the unified list of citations at the end of a document.
/// </summary>
/// <param name="Parser">The block parser used to create this block.</param>
public sealed class CitationsBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets or sets the collection of citations to be rendered in this block.
	/// </summary>
	public Dictionary<string, Citation> Citations { get; set; } = [];
}

// --- Parsers ---

/// <summary>
/// Parses {{Citation ...}} syntaxes in Markdown.
/// </summary>
public sealed class CitationParser : InlineParser
{
	private const string MarkerStart = "{{Citation ";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="CitationParser"/> class.
	/// </summary>
	public CitationParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override bool Match(InlineProcessor Processor, ref StringSlice Slice)
	{
		int StartPosition = Slice.Start;
		if (!Slice.Match(MarkerStart)) return false;

		Slice.Start += MarkerStart.Length;
		int EndPos = Slice.Text.IndexOf(MarkerEnd, Slice.Start, StringComparison.Ordinal);
		
		if (EndPos == -1)
		{
			Slice.Start = StartPosition;
			return false;
		}

		string CitationContent = Slice.Text.Substring(Slice.Start, EndPos - Slice.Start).Trim();
		string CitationId = GenerateCitationId(CitationContent);

		int InlineStart = Processor.GetSourcePosition(Slice.Start, out int Line, out int Column);

		Processor.Inline = new CitationInline
		{
			Span = new SourceSpan(InlineStart, InlineStart + (EndPos - Slice.Start)),
			Line = Line,
			Column = Column,
			Data = new StringSlice(Slice.Text, Slice.Start, EndPos - 1),
			CitationId = CitationId,
			CitationNumber = 0 // Assigned in ReprocessCitations
		};

		Slice.Start = EndPos + MarkerEnd.Length;
		return true;
	}

	/// <summary>
	/// Generates a unique ID for a citation based on its content hash.
	/// </summary>
	/// <param name="Content">The citation content to hash.</param>
	/// <returns>A unique citation ID string.</returns>
	private static string GenerateCitationId(string Content)
	{
		int Hash = 0;
		foreach (char C in Content)
		{
			Hash = (Hash * 31) + C;
		}
		return $"cit_{Math.Abs(Hash)}";
	}
}

/// <summary>
/// Parses the {{Citations}} block marker.
/// </summary>
public sealed class CitationsBlockParser : BlockParser
{
	private const string Marker = "{{Citations}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="CitationsBlockParser"/> class.
	/// </summary>
	public CitationsBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		if (!Processor.Line.Match(Marker)) return BlockState.None;

		Processor.Line.Start += Marker.Length;
		Processor.NewBlocks.Push(new CitationsBlock(this));
		return BlockState.BreakDiscard;
	}
}

// --- Renderers ---

/// <summary>
/// Renders a <see cref="CitationInline"/> into HTML (typically a superscript number).
/// </summary>
public sealed class CitationRenderer : HtmlObjectRenderer<CitationInline>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CitationInline Inline)
	{
		string Display = Inline.CitationNumber > 0 ? Inline.CitationNumber.ToString() : "?";
		string Id = !string.IsNullOrEmpty(Inline.BackRefId) ? Inline.BackRefId : $"citref:{Display}";

		Renderer.Write($"<sup id=\"{Id}\" class=\"citation-ref\">");
		Renderer.Write($"<a href=\"#cit:{Display}\">†{Display}</a>");
		Renderer.Write("</sup>");
	}
}

/// <summary>
/// Renders a <see cref="CitationsBlock"/> as a formatted bibliography.
/// </summary>
public sealed class CitationsRenderer : HtmlObjectRenderer<CitationsBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, CitationsBlock Block)
	{
		if (Block.Citations is null || Block.Citations.Count == 0)
			return;

		Renderer.Write("<div class=\"citations\">");
		Renderer.Write("<hr />");
		Renderer.Write("<h3>Citations</h3>");
		Renderer.Write("<ol class=\"citation-list\">");

		foreach (Citation Citation in Block.Citations.Values.OrderBy(c => c.Number))
		{
			Renderer.Write($"<li id=\"cit:{Citation.Number}\" class=\"citation-item\">");

			WriteFormattedCitation(Renderer, Citation);

			if (Citation.ReferencedBy.Count > 0)
			{
				Renderer.Write("<div class=\"citation-references\">");
				Renderer.Write("Referenced by: ");

				for (int i = 0; i < Citation.ReferencedBy.Count; i++)
				{
					if (i > 0) Renderer.Write(", ");

					string RefId = Citation.ReferencedBy[i];
					Renderer.Write($"<a href=\"#{RefId}\" class=\"citation-backref\">†{Citation.Number}");
					
					if (Citation.ReferencedBy.Count > 1)
					{
						Renderer.Write($"<sup>{i + 1}</sup>");
					}
					Renderer.Write("</a>");
				}

				Renderer.Write("</div>");
			}

			Renderer.Write("</li>");
		}

		Renderer.Write("</ol>");
		Renderer.Write("</div>");
	}

	/// <summary>
	/// Writes a formatted citation entry to the HTML output.
	/// </summary>
	/// <param name="Renderer">The HTML renderer.</param>
	/// <param name="Citation">The citation to format.</param>
	private static void WriteFormattedCitation(HtmlRenderer Renderer, Citation Citation)
	{
		// Author(s)
		if (Citation.Properties.TryGetValue("author", out List<string>? Authors))
		{
			WriteAuthors(Renderer, Authors);
			Renderer.Write(". ");
		}

		// Title
		if (Citation.Properties.TryGetValue("title", out List<string>? Titles) && Titles.Count > 0)
		{
			Renderer.Write($"<em>{Titles[0]}</em>");
			if (!Titles[0].EndsWith('.')) Renderer.Write(".");
			Renderer.Write(" ");
		}

		// Journal/Publication
		if (Citation.Properties.TryGetValue("journal", out List<string>? Journals) && Journals.Count > 0)
		{
			Renderer.Write($"<em>{Journals[0]}</em>, ");
		}
		else if (Citation.Properties.TryGetValue("publisher", out List<string>? Publishers) && Publishers.Count > 0)
		{
			Renderer.Write($"{Publishers[0]}, ");
		}

		// Volume
		if (Citation.Properties.TryGetValue("volume", out List<string>? Volumes) && Volumes.Count > 0)
		{
			Renderer.Write($"vol. {Volumes[0]}");
			if (Citation.Properties.TryGetValue("issue", out List<string>? Issues) && Issues.Count > 0)
			{
				Renderer.Write($", no. {Issues[0]}");
			}
			Renderer.Write(", ");
		}

		// Pages
		if (Citation.Properties.TryGetValue("pages", out List<string>? Pages) && Pages.Count > 0)
		{
			Renderer.Write($"pp. {Pages[0]}, ");
		}

		// Year
		if (Citation.Properties.TryGetValue("year", out List<string>? Years) && Years.Count > 0)
		{
			Renderer.Write($"{Years[0]}. ");
		}

		// DOI
		if (Citation.Properties.TryGetValue("doi", out List<string>? Dois) && Dois.Count > 0)
		{
			Renderer.Write($"DOI: <a href=\"https://doi.org/{Dois[0]}\" target=\"_blank\">{Dois[0]}</a>");
		}

		// URL
		if (Citation.Properties.TryGetValue("url", out List<string>? Urls) && Urls.Count > 0)
		{
			Renderer.Write($"URL: <a href=\"{Urls[0]}\" target=\"_blank\">{Urls[0]}</a>");
		}
	}

	/// <summary>
	/// Writes a list of authors to the HTML output with proper formatting.
	/// </summary>
	/// <param name="Renderer">The HTML renderer.</param>
	/// <param name="Authors">The list of author names.</param>
	private static void WriteAuthors(HtmlRenderer Renderer, List<string> Authors)
	{
		for (int i = 0; i < Authors.Count; i++)
		{
			if (i > 0)
			{
				Renderer.Write(i == Authors.Count - 1 ? " and " : ", ");
			}
			Renderer.Write(Authors[i]);
		}
	}
}

// --- Extension Definition ---

/// <summary>
/// A Markdig extension that adds support for citations and bibliographies.
/// </summary>
public sealed class CitationExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.InlineParsers.Contains<CitationParser>())
			Pipeline.InlineParsers.Add(new CitationParser());
		
		if (!Pipeline.BlockParsers.Contains<CitationsBlockParser>())
			Pipeline.BlockParsers.Add(new CitationsBlockParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRenderer)
		{
			if (!HtmlRenderer.ObjectRenderers.Contains<CitationRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new CitationRenderer());
			
			if (!HtmlRenderer.ObjectRenderers.Contains<CitationsRenderer>())
				HtmlRenderer.ObjectRenderers.Add(new CitationsRenderer());
		}
	}

	/// <summary>
	/// Reprocesses citation inlines and aggregates them into the citations block.
	/// </summary>
	/// <param name="Document">The markdown document to process.</param>
	public static void ReprocessCitations(MarkdownDocument Document)
	{
		List<CitationInline> Inlines = [.. Document.Descendants<CitationInline>()];
		if (Inlines.Count == 0) return;

		Dictionary<string, Citation> CitationsMap = [];
		int Counter = 1;
		
		foreach (CitationInline Inline in Inlines)
		{
			Citation Citation;
			if (CitationsMap.TryGetValue(Inline.CitationId, out Citation? ExistingCitation))
			{
				Citation = ExistingCitation;
			}
			else
			{
				Citation = ParseCitation(Inline.Data.ToString(), Inline.CitationId);
				Citation.Number = Counter++;
				CitationsMap[Inline.CitationId] = Citation;
			}

			Inline.CitationNumber = Citation.Number;
			
			// Generate a unique ID for this specific reference occurrence
			string BackRefId = $"citref:{Citation.Number}-{Citation.ReferencedBy.Count + 1}";
			
			Inline.BackRefId = BackRefId;
			Citation.ReferencedBy.Add(BackRefId);
		}

		CitationsBlock? Block = Document.Descendants<CitationsBlock>().FirstOrDefault();
		if (Block != null)
		{
			Block.Citations = CitationsMap;
		}
	}

	/// <summary>
	/// Parses citation content into a structured Citation object.
	/// </summary>
	/// <param name="Content">The raw citation content string.</param>
	/// <param name="CitationId">The unique ID for this citation.</param>
	/// <returns>A parsed Citation object with properties extracted from the content.</returns>
	private static Citation ParseCitation(string Content, string CitationId)
	{
		Dictionary<string, List<string>> Properties = new(StringComparer.OrdinalIgnoreCase);
		const string AttributeSeparator = "|#|";
		string[] Tokens = Content.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

		foreach (string Token in Tokens)
		{
			string Trimmed = Token.Trim();
			if (string.IsNullOrEmpty(Trimmed)) continue;

			int EqPos = Trimmed.IndexOf('=');
			if (EqPos <= 0) continue;

			string Key = Trimmed[..EqPos].Trim();
			string Value = Trimmed[(EqPos + 1)..].Trim();

			if (!Properties.TryGetValue(Key, out List<string>? List))
			{
				List = [];
				Properties[Key] = List;
			}
			List.Add(Value);
		}

		return new Citation
		{
			Number = 0,
			Id = CitationId,
			Properties = Properties,
			ReferencedBy = []
		};
	}
}
