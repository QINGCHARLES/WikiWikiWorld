using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// An infobox: facts whose values participate in the article's normal inline Markdown processing.
/// The type named after <c>{{Infobox</c> selects the site schema that orders and labels the facts.
/// </summary>
/// <param name="Parser">The parser that collects the enclosing directive.</param>
public sealed class InfoboxBlock(BlockParser Parser) : ContainerBlock(Parser)
{
	/// <summary>Retains the payload for fact parsing and malformed-source display.</summary>
	public StringBuilder RawContent { get; } = new();

	/// <summary>Retains raw values grouped by their first case-insensitive property occurrence.</summary>
	public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Gets the type written after <c>{{Infobox</c>, such as <c>Publication</c>; <c>PublicationIssue</c> for the legacy
	/// <c>{{PublicationIssueInfobox</c> name; or <see langword="null"/> when no type was written.
	/// </summary>
	public string? TypeName { get; internal init; }

	/// <summary>
	/// Gets or sets the site's schema for <see cref="TypeName"/>, attached by <see cref="InfoboxExtension.Enrich"/>.
	/// <see langword="null"/> when the block was not enriched, has no type, or the site does not define the type;
	/// the facts then render in written order with their raw keys as labels.
	/// </summary>
	public InfoboxTypeSchema? Schema { get; set; }

	/// <summary>Gets or sets the culture whose labels to show, attached by <see cref="InfoboxExtension.Enrich"/>.</summary>
	public string? Culture { get; set; }

	/// <summary>Identifies the matching outer closing braces, or -1 for incomplete input.</summary>
	internal int ClosingPosition { get; init; }

	/// <summary>Gets the directive name and any type name exactly as written, for redisplaying incomplete input.</summary>
	internal string OpeningSource { get; init; } = string.Empty;
}

/// <summary>Holds a fact value as an inline leaf so its notes and links remain part of the article tree.</summary>
internal sealed class InfoboxFactBlock : LeafBlock
{
	/// <summary>Gets the first spelling of this property's plain-text key.</summary>
	internal string Key { get; }

	/// <summary>Supplies a value to Markdig's inline pass without enabling block Markdown in the value.</summary>
	/// <param name="Key">The plain-text property name.</param>
	/// <param name="Value">The inline Markdown to parse.</param>
	internal InfoboxFactBlock(string Key, string Value) : base(null)
	{
		this.Key = Key;
		ProcessInlines = true;
		Lines = new StringLineGroup(Value);
	}
}

/// <summary>
/// Collects <c>{{Infobox Type</c> and legacy <c>{{PublicationIssueInfobox</c> payloads and creates value leaves
/// before the article's inline parsing pass.
/// </summary>
public sealed class InfoboxBlockParser : BlockParser
{
	internal const string MarkerStart = "{{Infobox";
	internal const string LegacyMarkerStart = "{{PublicationIssueInfobox";

	/// <summary>The type the legacy directive name stands for.</summary>
	internal const string LegacyTypeName = "PublicationIssue";

	/// <summary>Registers the opening brace used by standalone infobox directives.</summary>
	public InfoboxBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		StringSlice Slice = Processor.Line;
		if (Processor.IsCodeIndent) return BlockState.None;

		string Marker;
		if (Slice.Match(LegacyMarkerStart)) Marker = LegacyMarkerStart;
		else if (Slice.Match(MarkerStart)) Marker = MarkerStart;
		else return BlockState.None;

		int Start = Slice.Start + Marker.Length;
		if (!IsBoundary(Slice.Text, Start, Slice.End)) return BlockState.None;

		string? TypeName = LegacyTypeName;
		if (ReferenceEquals(Marker, MarkerStart))
		{
			Start = ReadTypeName(Slice.Text, Start, Slice.End, out TypeName);
		}

		InfoboxBlock Block = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column,
			TypeName = TypeName,
			OpeningSource = Slice.Text[Slice.Start..Start],
			ClosingPosition = DirectiveSyntax.FindClosingDelimiter(Slice.Text, Start)
		};
		Processor.NewBlocks.Push(Block);
		Slice.Start = Start;
		return AppendLine(Block, Slice);
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock) =>
		AppendLine((InfoboxBlock)CurrentBlock, Processor.Line);

	/// <inheritdoc/>
	public override bool Close(BlockProcessor Processor, Block CurrentBlock)
	{
		InfoboxBlock Block = (InfoboxBlock)CurrentBlock;
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
			foreach (string Value in Property.Value)
			{
				Block.Add(new InfoboxFactBlock(Property.Key, Value) { Line = Block.Line, Column = Block.Column });
			}
		}
		return true;
	}

	/// <summary>
	/// Checks that the directive name ends here, so <c>{{InfoboxExtra</c> is not mistaken for an infobox.
	/// </summary>
	/// <param name="Text">The source text.</param>
	/// <param name="Position">The position just after the directive name.</param>
	/// <param name="LineEnd">The last position of the current line.</param>
	/// <returns><see langword="true"/> at the line end, whitespace, or the closing <c>}}</c>.</returns>
	private static bool IsBoundary(string Text, int Position, int LineEnd) =>
		Position > LineEnd || char.IsWhiteSpace(Text[Position]) || Text.AsSpan(Position).StartsWith("}}", StringComparison.Ordinal);

	/// <summary>
	/// Reads an optional type name after <c>{{Infobox</c>: letters and digits starting with a letter, followed by
	/// whitespace, the line end or <c>}}</c>. A first token such as <c>Editor=Jane</c> is a fact, not a type.
	/// </summary>
	/// <param name="Text">The source text.</param>
	/// <param name="Start">The position just after <c>{{Infobox</c>.</param>
	/// <param name="LineEnd">The last position of the current line.</param>
	/// <param name="TypeName">The type name, or <see langword="null"/> when none was written.</param>
	/// <returns>The position after the type name, or <paramref name="Start"/> when there is none.</returns>
	private static int ReadTypeName(string Text, int Start, int LineEnd, out string? TypeName)
	{
		TypeName = null;
		int Position = Start;
		while (Position <= LineEnd && Text[Position] is ' ' or '\t') Position++;

		int TokenStart = Position;
		while (Position <= LineEnd && char.IsAsciiLetterOrDigit(Text[Position])) Position++;

		if (Position == TokenStart || !char.IsAsciiLetter(Text[TokenStart]) || !IsBoundary(Text, Position, LineEnd))
		{
			return Start;
		}

		TypeName = Text[TokenStart..Position];
		return Position;
	}

	/// <summary>Consumes a payload line without interpreting it as nested block Markdown.</summary>
	/// <param name="Block">The enclosing infobox and its precomputed closing position.</param>
	/// <param name="Slice">The current payload line within the original article source.</param>
	/// <returns>A discard state that ends the block at its outer closing line.</returns>
	private static BlockState AppendLine(InfoboxBlock Block, StringSlice Slice)
	{
		bool IsClosingLine = Block.ClosingPosition >= Slice.Start && Block.ClosingPosition <= Slice.End;
		if (IsClosingLine) Slice.End = Block.ClosingPosition - 1;
		Block.RawContent.AppendLine(Slice.ToString());
		return IsClosingLine ? BlockState.BreakDiscard : BlockState.ContinueDiscard;
	}
}

/// <summary>
/// Renders infobox facts as a definition list with plain-text labels and inline Markdown values.
/// With a schema attached, defined fields come first in schema order with the culture's labels, then any other
/// facts in written order with their raw keys; without one, every fact renders in written order with its raw key.
/// </summary>
public sealed class InfoboxRenderer : HtmlObjectRenderer<InfoboxBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, InfoboxBlock Block)
	{
		if (Block.ClosingPosition < 0)
		{
			Renderer.Write("<pre class=\"infobox-error\">").WriteEscape(Block.OpeningSource)
				.WriteEscape(Block.RawContent.ToString()).Write("</pre>");
			return;
		}
		Renderer.Write("<aside class=\"infobox\">");
		if (Block.Count > 0)
		{
			Renderer.Write("<dl>");
			foreach ((InfoboxFactBlock Fact, string Label) in OrderFacts(Block))
			{
				Renderer.Write("<dt>").WriteEscape(Label).Write("</dt><dd>");
				Renderer.WriteLeafInline(Fact);
				Renderer.Write("</dd>");
			}
			Renderer.Write("</dl>");
		}
		Renderer.Write("</aside>");
	}

	/// <summary>Pairs each fact with its label in display order.</summary>
	/// <param name="Block">The infobox, with or without an attached schema.</param>
	/// <returns>Every fact exactly once, defined fields first when a schema is attached.</returns>
	private static IEnumerable<(InfoboxFactBlock Fact, string Label)> OrderFacts(InfoboxBlock Block)
	{
		InfoboxFactBlock[] Facts = [.. Block.Cast<InfoboxFactBlock>()];
		if (Block.Schema is null)
		{
			foreach (InfoboxFactBlock Fact in Facts) yield return (Fact, Fact.Key);
			yield break;
		}

		bool[] Shown = new bool[Facts.Length];
		foreach (InfoboxFieldSchema Field in Block.Schema.Fields)
		{
			string Label = GetLabel(Field, Block.Culture);
			for (int i = 0; i < Facts.Length; i++)
			{
				if (Shown[i] || !string.Equals(Facts[i].Key, Field.Key, StringComparison.OrdinalIgnoreCase)) continue;
				Shown[i] = true;
				yield return (Facts[i], Label);
			}
		}

		for (int i = 0; i < Facts.Length; i++)
		{
			if (!Shown[i]) yield return (Facts[i], Facts[i].Key);
		}
	}

	/// <summary>Chooses the label for the page's culture, falling back to the required English label.</summary>
	/// <param name="Field">The schema field.</param>
	/// <param name="Culture">The page culture, or <see langword="null"/> when unknown.</param>
	/// <returns>The display label.</returns>
	private static string GetLabel(InfoboxFieldSchema Field, string? Culture) =>
		Culture is not null && Field.Labels.TryGetValue(Culture, out string? Label)
			? Label
			: Field.Labels[InfoboxSchemaCatalog.FallbackCulture];
}

/// <summary>Adds infoboxes to the article's block and inline Markdown processing.</summary>
public sealed class InfoboxExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<InfoboxBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new InfoboxBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Any(x => x is InfoboxRenderer))
		{
			Html.ObjectRenderers.Add(new InfoboxRenderer());
		}
	}

	/// <summary>
	/// Attaches the site's schema and the page culture to every infobox in the document, so they render with the
	/// site's field order and labels. Infoboxes whose type the site does not define keep their written order and raw keys.
	/// </summary>
	/// <param name="Document">The parsed article.</param>
	/// <param name="Catalog">The schemas loaded at startup.</param>
	/// <param name="SiteId">The site the article belongs to.</param>
	/// <param name="Culture">The article's culture code.</param>
	public static void Enrich(MarkdownDocument Document, InfoboxSchemaCatalog Catalog, int SiteId, string Culture)
	{
		ArgumentNullException.ThrowIfNull(Document);
		ArgumentNullException.ThrowIfNull(Catalog);

		foreach (InfoboxBlock Block in Document.Descendants<InfoboxBlock>())
		{
			Block.Culture = Culture;
			if (Block.TypeName is not null && Catalog.TryGetType(SiteId, Block.TypeName, out InfoboxTypeSchema? Type))
			{
				Block.Schema = Type;
			}
		}
	}

	/// <summary>
	/// Lists problems an author should fix in the document's infoboxes, checked against the site's schemas: an
	/// unclosed infobox, a missing or undefined type, a key the type does not define, and a non-repeatable field
	/// written more than once. Problems never block a save; sites without a schema file report only unclosed infoboxes.
	/// </summary>
	/// <param name="Document">The parsed article.</param>
	/// <param name="Catalog">The schemas loaded at startup.</param>
	/// <param name="SiteId">The site the article belongs to.</param>
	/// <returns>One plain-English message per problem, in document order; empty when there are none.</returns>
	public static IReadOnlyList<string> FindProblems(MarkdownDocument Document, InfoboxSchemaCatalog Catalog, int SiteId)
	{
		ArgumentNullException.ThrowIfNull(Document);
		ArgumentNullException.ThrowIfNull(Catalog);

		List<string> Problems = [];
		IReadOnlyCollection<string> TypeNames = Catalog.GetTypeNames(SiteId);
		string Available = string.Join(", ", TypeNames);

		foreach (InfoboxBlock Block in Document.Descendants<InfoboxBlock>())
		{
			string Where = $"Infobox on line {Block.Line + 1}";
			if (Block.ClosingPosition < 0)
			{
				Problems.Add($"{Where} is not closed with }}}} and shows as source text.");
				continue;
			}
			if (TypeNames.Count == 0) continue;

			if (Block.TypeName is null)
			{
				Problems.Add($"{Where} has no type, so its rows keep their raw keys. Write {{{{Infobox <Type>}}}} using one of: {Available}.");
				continue;
			}
			if (!Catalog.TryGetType(SiteId, Block.TypeName, out InfoboxTypeSchema? Type))
			{
				Problems.Add($"{Where}: type \"{Block.TypeName}\" is not defined on this site. Defined types: {Available}.");
				continue;
			}

			foreach ((string Key, List<string> Values) in Block.Properties)
			{
				InfoboxFieldSchema? Field = Type.Fields.FirstOrDefault(Field => string.Equals(Field.Key, Key, StringComparison.OrdinalIgnoreCase));
				if (Field is null)
				{
					Problems.Add($"{Where} ({Block.TypeName}): \"{Key}\" is not a field of this type and shows with its raw key.");
				}
				else if (!Field.Repeatable && Values.Count > 1)
				{
					Problems.Add($"{Where} ({Block.TypeName}): \"{Field.Key}\" appears {Values.Count} times but is not repeatable.");
				}
			}
		}
		return Problems;
	}
}
