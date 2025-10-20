using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WikiWikiWorld.MarkdigExtensions;

/// <summary>
/// Block representing the {{Citations}} marker
/// </summary>
public sealed class CitationsBlock : LeafBlock
{
	public CitationsBlock(BlockParser Parser) : base(Parser) { }
}

/// <summary>
/// Parser that identifies the {{Citations}} marker
/// </summary>
public sealed class CitationsParser : BlockParser
{
	private const string Marker = "{{Citations}}";

	public CitationsParser()
	{
		OpeningCharacters = ['{'];
	}

	public override BlockState TryOpen(BlockProcessor Processor)
	{
		// Check if the line starts with "{{Citations}}"
		if (!Processor.Line.Match(Marker))
		{
			return BlockState.None;
		}

		// Move cursor forward to avoid infinite loop
		Processor.Line.Start += Marker.Length;

		// Push new block
		Processor.NewBlocks.Push(new CitationsBlock(this));
		return BlockState.BreakDiscard;
	}
}

/// <summary>
/// Renderer that outputs the list of citations in HTML
/// </summary>
public sealed class CitationsRenderer : HtmlObjectRenderer<CitationsBlock>
{
	private readonly Dictionary<string, Citation> Citations;

	public CitationsRenderer(Dictionary<string, Citation> Citations)
	{
		this.Citations = Citations;
	}

	protected override void Write(HtmlRenderer Renderer, CitationsBlock Block)
	{
		if (Citations is null || Citations.Count == 0)
		{
			return;
		}

		// Start citations section
		Renderer.Write("<div class=\"citations\">");
		Renderer.Write("<hr />");
		Renderer.Write("<h3>Citations</h3>");
		Renderer.Write("<ol class=\"citation-list\">");

		// Write each citation
		foreach (Citation Citation in Citations.Values.OrderBy(c => c.Number))
		{
			Renderer.Write("<li id=\"cit:");
			Renderer.Write(Citation.Number.ToString());
			Renderer.Write("\" class=\"citation-item\">");

			// Write the citation in a structured format
			WriteFormattedCitation(Renderer, Citation);

			// Add back-references
			if (Citation.ReferencedBy.Count > 0)
			{
				Renderer.Write("<div class=\"citation-references\">");
				Renderer.Write("Referenced by: ");

				for (int i = 0; i < Citation.ReferencedBy.Count; i++)
				{
					if (i > 0)
					{
						Renderer.Write(", ");
					}

					string RefId = Citation.ReferencedBy[i];
					Renderer.Write("<a href=\"#");
					Renderer.Write(RefId);
					Renderer.Write("\" class=\"citation-backref\">†");
					Renderer.Write(Citation.Number.ToString());
					if (i + 1 < Citation.ReferencedBy.Count)
					{
						Renderer.Write("<sup>");
						Renderer.Write((i + 1).ToString());
						Renderer.Write("</sup>");
					}
					Renderer.Write("</a>");
				}

				Renderer.Write("</div>");
			}

			Renderer.Write("</li>");
		}

		// End citations section
		Renderer.Write("</ol>");
		Renderer.Write("</div>");
	}

	/// <summary>
	/// Writes a formatted citation based on available properties
	/// </summary>
	private static void WriteFormattedCitation(HtmlRenderer Renderer, Citation Citation)
	{
		// Format the citation based on available properties
		// This is a simplified version that can be expanded for different citation styles

		// Author(s)
		if (Citation.Properties.TryGetValue("author", out List<string>? Authors))
		{
			WriteAuthors(Renderer, Authors);
			Renderer.Write(". ");
		}

		// Title
		if (Citation.Properties.TryGetValue("title", out List<string>? Titles) && Titles.Count > 0)
		{
			Renderer.Write("<em>");
			Renderer.Write(Titles[0]);
			Renderer.Write("</em>");

			if (!Titles[0].EndsWith('.'))
			{
				Renderer.Write(".");
			}

			Renderer.Write(" ");
		}

		// Journal/Publication
		if (Citation.Properties.TryGetValue("journal", out List<string>? Journals) && Journals.Count > 0)
		{
			Renderer.Write("<em>");
			Renderer.Write(Journals[0]);
			Renderer.Write("</em>");
			Renderer.Write(", ");
		}
		else if (Citation.Properties.TryGetValue("publisher", out List<string>? Publishers) && Publishers.Count > 0)
		{
			Renderer.Write(Publishers[0]);
			Renderer.Write(", ");
		}

		// Volume/Issue
		if (Citation.Properties.TryGetValue("volume", out List<string>? Volumes) && Volumes.Count > 0)
		{
			Renderer.Write("vol. ");
			Renderer.Write(Volumes[0]);

			if (Citation.Properties.TryGetValue("issue", out List<string>? Issues) && Issues.Count > 0)
			{
				Renderer.Write(", no. ");
				Renderer.Write(Issues[0]);
			}

			Renderer.Write(", ");
		}

		// Pages
		if (Citation.Properties.TryGetValue("pages", out List<string>? Pages) && Pages.Count > 0)
		{
			Renderer.Write("pp. ");
			Renderer.Write(Pages[0]);
			Renderer.Write(", ");
		}

		// Year
		if (Citation.Properties.TryGetValue("year", out List<string>? Years) && Years.Count > 0)
		{
			Renderer.Write(Years[0]);
			Renderer.Write(". ");
		}

		// DOI
		if (Citation.Properties.TryGetValue("doi", out List<string>? Dois) && Dois.Count > 0)
		{
			Renderer.Write("DOI: <a href=\"https://doi.org/");
			Renderer.Write(Dois[0]);
			Renderer.Write("\" target=\"_blank\">");
			Renderer.Write(Dois[0]);
			Renderer.Write("</a>");
		}

		// URL
		if (Citation.Properties.TryGetValue("url", out List<string>? Urls) && Urls.Count > 0)
		{
			Renderer.Write("URL: <a href=\"");
			Renderer.Write(Urls[0]);
			Renderer.Write("\" target=\"_blank\">");
			Renderer.Write(Urls[0]);
			Renderer.Write("</a>");
	}

	// Write any other properties not specifically handled
	foreach (KeyValuePair<string, List<string>> Property in Citation.Properties)
	{
		string[] KnownProperties = ["author", "title", "journal", "publisher", "volume", "issue", "pages", "year", "doi", "url"];
		if (!KnownProperties.Contains(Property.Key, StringComparer.OrdinalIgnoreCase))
		{
			Renderer.Write("<br/>");
			Renderer.Write("<strong>");
			Renderer.Write(Property.Key);
			Renderer.Write("</strong>: ");
			Renderer.Write(string.Join(", ", Property.Value));
		}
	}
}	/// <summary>
	/// Formats and writes author names
	/// </summary>
	private static void WriteAuthors(HtmlRenderer Renderer, List<string> Authors)
	{
		for (int i = 0; i < Authors.Count; i++)
		{
			if (i > 0)
			{
				if (i == Authors.Count - 1)
				{
					Renderer.Write(" and ");
				}
				else
				{
					Renderer.Write(", ");
				}
			}

			Renderer.Write(Authors[i]);
		}
	}
}

/// <summary>
/// Main extension class that registers the parser and renderer for the citations block
/// </summary>
public sealed class CitationsExtension(Dictionary<string, Citation> Citations) : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<CitationsParser>())
		{
			Pipeline.BlockParsers.Add(new CitationsParser());
		}
	}

	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer HtmlRendererInstance &&
			!HtmlRendererInstance.ObjectRenderers.Contains<CitationsRenderer>())
		{
			HtmlRendererInstance.ObjectRenderers.Add(new CitationsRenderer(Citations));
		}
	}
}
