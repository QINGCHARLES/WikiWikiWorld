using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>Contains facts whose values participate in the article's normal inline Markdown processing.</summary>
/// <param name="Parser">The parser that collects the enclosing directive.</param>
public sealed class PublicationIssueInfoboxBlock(BlockParser Parser) : ContainerBlock(Parser)
{
	/// <summary>Retains the payload for fact parsing and malformed-source display.</summary>
	public StringBuilder RawContent { get; } = new();

	/// <summary>Retains raw values grouped by their first case-insensitive property occurrence.</summary>
	public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Identifies the matching outer closing braces, or -1 for incomplete input.</summary>
	internal int ClosingPosition { get; init; }
}

/// <summary>Holds a fact value as an inline leaf so its notes and links remain part of the article tree.</summary>
internal sealed class PublicationIssueFactBlock : LeafBlock
{
	/// <summary>Gets the first spelling of this property's plain-text label.</summary>
	internal string Key { get; }

	/// <summary>Supplies a value to Markdig's inline pass without enabling block Markdown in the value.</summary>
	/// <param name="Key">The plain-text property name.</param>
	/// <param name="Value">The inline Markdown to parse.</param>
	internal PublicationIssueFactBlock(string Key, string Value) : base(null)
	{
		this.Key = Key;
		ProcessInlines = true;
		Lines = new StringLineGroup(Value);
	}
}

/// <summary>Collects infobox payloads and creates value leaves before the article's inline parsing pass.</summary>
public sealed class PublicationIssueInfoboxBlockParser : BlockParser
{
	internal const string MarkerStart = "{{PublicationIssueInfobox";

	/// <summary>Registers the opening brace used by standalone infobox directives.</summary>
	public PublicationIssueInfoboxBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		if (Processor.IsCodeIndent || !Slice.Match(MarkerStart)) return BlockState.None;
		int Start = Slice.Start + MarkerStart.Length;
		if (Start <= Slice.End && !char.IsWhiteSpace(Slice.Text[Start]) &&
			!Slice.Text.AsSpan(Start).StartsWith("}}", StringComparison.Ordinal)) return BlockState.None;

		PublicationIssueInfoboxBlock Block = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column,
			ClosingPosition = DirectiveSyntax.FindClosingDelimiter(Slice.Text, Start)
		};
		Processor.NewBlocks.Push(Block);
		Slice.Start = Start;
		return AppendLine(Block, Slice);
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock) =>
		AppendLine((PublicationIssueInfoboxBlock)CurrentBlock, Processor.Line);

	/// <inheritdoc/>
	public override bool Close(BlockProcessor Processor, Block CurrentBlock)
	{
		PublicationIssueInfoboxBlock Block = (PublicationIssueInfoboxBlock)CurrentBlock;
		if (Block.ClosingPosition < 0) return true;
		foreach (string Property in DirectiveSyntax.SplitProperties(Block.RawContent.ToString()))
		{
			int EqualsPosition = Property.IndexOf('=');
			if (EqualsPosition <= 0) continue;
			string Key = Property[..EqualsPosition].Trim();
			if (Key.Length == 0) continue;
			string Value = Property[(EqualsPosition + 1)..].Trim();
			if (!Block.Properties.TryGetValue(Key, out List<string>? Values))
			{
				Values = [];
				Block.Properties.Add(Key, Values);
			}
			Values.Add(Value);
		}

		foreach (KeyValuePair<string, List<string>> Property in Block.Properties)
		{
			if (Property.Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase)) continue;
			foreach (string Value in Property.Value)
			{
				Block.Add(new PublicationIssueFactBlock(Property.Key, Value) { Line = Block.Line, Column = Block.Column });
			}
		}
		return true;
	}

	/// <summary>Consumes a payload line without interpreting it as nested block Markdown.</summary>
	/// <param name="Block">The enclosing infobox and its precomputed closing position.</param>
	/// <param name="Slice">The current payload line within the original article source.</param>
	/// <returns>A discard state that ends the block at its outer closing line.</returns>
	private static BlockState AppendLine(PublicationIssueInfoboxBlock Block, StringSlice Slice)
	{
		bool IsClosingLine = Block.ClosingPosition >= Slice.Start && Block.ClosingPosition <= Slice.End;
		if (IsClosingLine) Slice.End = Block.ClosingPosition - 1;
		Block.RawContent.AppendLine(Slice.ToString());
		return IsClosingLine ? BlockState.BreakDiscard : BlockState.ContinueDiscard;
	}
}

/// <summary>Renders publication facts with plain-text labels and inline Markdown values.</summary>
public sealed class PublicationIssueInfoboxRenderer : HtmlObjectRenderer<PublicationIssueInfoboxBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, PublicationIssueInfoboxBlock Block)
	{
		if (Block.ClosingPosition < 0)
		{
			Renderer.Write("<pre class=\"infobox-error\">").WriteEscape(PublicationIssueInfoboxBlockParser.MarkerStart)
				.WriteEscape(Block.RawContent.ToString()).Write("</pre>");
			return;
		}
		Renderer.Write("<aside class=\"infobox\">");
		if (Block.Count > 0)
		{
			Renderer.Write("<dl>");
			foreach (PublicationIssueFactBlock Fact in Block.Cast<PublicationIssueFactBlock>())
			{
				Renderer.Write("<dt>").WriteEscape(GetFriendlyName(Fact.Key)).Write("</dt><dd>");
				Renderer.WriteLeafInline(Fact);
				Renderer.Write("</dd>");
			}
			Renderer.Write("</dl>");
		}
		Renderer.Write("</aside>");
	}

	/// <summary>Preserves established canonical labels and displays custom keys as plain text.</summary>
	/// <param name="Key">The property name, retaining its first authored casing.</param>
	/// <returns>The existing friendly label or the unchanged custom name.</returns>
	private static string GetFriendlyName(string Key) => Key switch
	{
		"CoverPrice" => "Cover Price",
		"PublicationFormats" => "Publication Formats",
		"CoverDate" => "Cover Date",
		"PrintPublicationDate" => "Print Publication Date",
		"ElectronicPublicationDate" => "Electronic Publication Date",
		"PrintEAN-13" => "Print EAN-13",
		"PrintEAN-2" => "Print EAN-2",
		"ISSN" => "ISSN",
		_ => Key,
	};
}

/// <summary>Adds infoboxes to the article's block and inline Markdown processing.</summary>
public sealed class PublicationIssueInfoboxExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<PublicationIssueInfoboxBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new PublicationIssueInfoboxBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Any(x => x is PublicationIssueInfoboxRenderer))
		{
			Html.ObjectRenderers.Add(new PublicationIssueInfoboxRenderer());
		}
	}
}
