using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Text;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>Retains issue contents source so invalid input can be displayed without losing entries.</summary>
/// <param name="Parser">The parser collecting this directive.</param>
public sealed class IssueContentsBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>Gets the complete authored directive, including delimiters.</summary>
	public StringBuilder Source { get; } = new();

	/// <summary>Gets or sets whether a closing delimiter has been encountered.</summary>
	public bool IsClosed { get; set; }
}

/// <summary>Collects standalone issue contents directives in single-line or multiline form.</summary>
public sealed class IssueContentsParser : BlockParser
{
	internal const string Marker = "{{IssueContents";

	/// <summary>Registers the opening brace as the block trigger.</summary>
	public IssueContentsParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		string Line = Processor.Line.ToString();
		if (Processor.IsCodeIndent || !Line.StartsWith(Marker, StringComparison.Ordinal) ||
			(Line.Length > Marker.Length && !char.IsWhiteSpace(Line[Marker.Length]) &&
			 !Line.AsSpan(Marker.Length).StartsWith("}}", StringComparison.Ordinal)))
			return BlockState.None;
		IssueContentsBlock Block = new(this) { Line = Processor.LineIndex, Column = Processor.Column };
		Processor.NewBlocks.Push(Block);
		return AppendLine(Block, Line);
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block Block)
		=> AppendLine((IssueContentsBlock)Block, Processor.Line.ToString());

	/// <summary>Collects a line and consumes the closing line without emitting a spare paragraph.</summary>
	/// <param name="Block">The directive being collected.</param>
	/// <param name="Line">The current source line.</param>
	/// <returns>The continuation state for Markdig.</returns>
	private static BlockState AppendLine(IssueContentsBlock Block, string Line)
	{
		Block.Source.AppendLine(Line);
		Block.IsClosed = Line.Contains("}}", StringComparison.Ordinal);
		return Block.IsClosed ? BlockState.BreakDiscard : BlockState.Continue;
	}
}

/// <summary>Stores the printed information for one entry, without inferring links or page positions.</summary>
/// <param name="Title">The required entry title.</param>
/// <param name="Description">Optional descriptive copy.</param>
/// <param name="Credit">Optional contributor attribution.</param>
/// <param name="Pages">Optional printed page label or range.</param>
internal sealed record class IssueContentsEntry(string Title, string? Description, string? Credit, string? Pages);

/// <summary>Preserves an authored section and its entries in source order.</summary>
/// <param name="Title">The section heading, or null for an ungrouped list.</param>
/// <param name="Featured">Whether the section spans the available columns.</param>
internal sealed record class IssueContentsGroup(string? Title, bool Featured)
{
	/// <summary>Gets entries in their original order, including repeated titles.</summary>
	public List<IssueContentsEntry> Entries { get; } = [];
}

/// <summary>Renders noninteractive issue contents with escaped text and semantic headings and lists.</summary>
public sealed class IssueContentsRenderer : HtmlObjectRenderer<IssueContentsBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, IssueContentsBlock Block)
	{
		string Source = Block.Source.ToString().TrimEnd();
		List<IssueContentsGroup>? Groups = Block.IsClosed && Source.EndsWith("}}", StringComparison.Ordinal)
			? ParseGroups(Source[IssueContentsParser.Marker.Length..^2]) : null;
		if (Groups is null)
		{
			Renderer.Write("<pre class=\"issue-contents-error\">").WriteEscape(Source).WriteLine("</pre>");
			return;
		}
		Renderer.WriteLine("<section class=\"issue-contents\"><h2 class=\"issue-contents-heading\">In This Issue</h2>");
		Renderer.WriteLine("<div class=\"issue-contents-groups\">");
		foreach (IssueContentsGroup Group in Groups)
		{
			Renderer.Write(Group.Featured ? "<div class=\"issue-contents-group issue-contents-featured\">" : "<div class=\"issue-contents-group\">");
			if (Group.Title is not null)
				Renderer.Write("<h3 class=\"issue-contents-group-title\">").WriteEscape(Group.Title).WriteLine("</h3>");
			Renderer.WriteLine("<ol class=\"issue-contents-entries\" role=\"list\">");
			foreach (IssueContentsEntry Entry in Group.Entries)
			{
				string Heading = Group.Title is null ? "h3" : "h4";
				Renderer.Write("<li class=\"issue-contents-entry\"><div class=\"issue-contents-entry-heading\"><")
					.Write(Heading).Write(" class=\"issue-contents-title\">").WriteEscape(Entry.Title)
					.Write("</").Write(Heading).Write(">");
				if (Entry.Pages is not null)
					Renderer.Write("<span class=\"issue-contents-pages\">p. ").WriteEscape(Entry.Pages).Write("</span>");
				Renderer.WriteLine("</div>");
				if (Entry.Description is not null)
					Renderer.Write("<p class=\"issue-contents-description\">").WriteEscape(Entry.Description).WriteLine("</p>");
				if (Entry.Credit is not null)
					Renderer.Write("<p class=\"issue-contents-credit\">").WriteEscape(Entry.Credit).WriteLine("</p>");
				Renderer.WriteLine("</li>");
			}
			Renderer.WriteLine("</ol></div>");
		}
		Renderer.WriteLine("</div></section>");
	}

	/// <summary>Validates all items before rendering, preserving malformed source instead of dropping data.</summary>
	/// <param name="Payload">The text between the directive delimiters.</param>
	/// <returns>Ordered groups, or null if any item violates the authoring contract.</returns>
	private static List<IssueContentsGroup>? ParseGroups(string Payload)
	{
		if (Payload.Contains("{{", StringComparison.Ordinal) || Payload.Contains("}}", StringComparison.Ordinal))
			return null;
		List<IssueContentsGroup> Groups = [];
		foreach (string Item in Payload.Split("|~|", StringSplitOptions.None))
		{
			Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase);
			foreach (string Token in Item.Split("|#|", StringSplitOptions.None))
			{
				int Separator = Token.IndexOf('=');
				if (Separator < 1)
					return null;
				string Key = Token[..Separator].Trim();
				string Value = Token[(Separator + 1)..].Trim();
				if (Value.Length == 0 || !Properties.TryAdd(Key, Value))
					return null;
			}
			if (Properties.TryGetValue("Group", out string? GroupTitle))
			{
				bool Featured = false;
				if (Properties.Keys.Any(k => !k.Equals("Group", StringComparison.OrdinalIgnoreCase) && !k.Equals("Featured", StringComparison.OrdinalIgnoreCase)) ||
					(Properties.TryGetValue("Featured", out string? FeaturedText) && !bool.TryParse(FeaturedText, out Featured)))
					return null;
				Groups.Add(new(GroupTitle, Featured));
			}
			else
			{
				if (!Properties.TryGetValue("Title", out string? Title) || Properties.Keys.Any(k =>
					!k.Equals("Title", StringComparison.OrdinalIgnoreCase) && !k.Equals("Description", StringComparison.OrdinalIgnoreCase) &&
					!k.Equals("Credit", StringComparison.OrdinalIgnoreCase) && !k.Equals("Pages", StringComparison.OrdinalIgnoreCase)))
					return null;
				if (Groups.Count == 0)
					Groups.Add(new(null, true));
				Properties.TryGetValue("Description", out string? Description);
				Properties.TryGetValue("Credit", out string? Credit);
				Properties.TryGetValue("Pages", out string? Pages);
				Groups[^1].Entries.Add(new(Title, Description, Credit, Pages));
			}
		}
		return Groups.Count > 0 && Groups.All(g => g.Entries.Count > 0) ? Groups : null;
	}
}

/// <summary>Adds manually authored issue contents to article Markdown without data lookup or JavaScript.</summary>
public sealed class IssueContentsExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<IssueContentsParser>())
			Pipeline.BlockParsers.Insert(0, new IssueContentsParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Contains<IssueContentsRenderer>())
			Html.ObjectRenderers.Insert(0, new IssueContentsRenderer());
	}
}
