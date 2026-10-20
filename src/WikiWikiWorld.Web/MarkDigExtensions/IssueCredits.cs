using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// Retains a credits directive until its closing delimiter is found, including malformed source for display.
/// </summary>
/// <param name="Parser">The parser responsible for this block.</param>
public sealed class IssueCreditsBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets the original directive text so invalid input can remain visible to its author.
	/// </summary>
	public StringBuilder Source { get; } = new();

	/// <summary>
	/// Gets or sets whether a closing delimiter was encountered.
	/// </summary>
	public bool IsClosed { get; set; }
}

/// <summary>
/// Recognizes standalone, single-line or multiline issue credits directives.
/// </summary>
public sealed class IssueCreditsParser : BlockParser
{
	internal const string Marker = "{{IssueCredits";

	/// <summary>
	/// Registers the opening brace as the trigger for credits blocks.
	/// </summary>
	public IssueCreditsParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		string Line = Processor.Line.ToString();
		if (Processor.IsCodeIndent || !Line.StartsWith(Marker, StringComparison.Ordinal) ||
			(Line.Length > Marker.Length && !char.IsWhiteSpace(Line[Marker.Length]) &&
			 !Line.AsSpan(Marker.Length).StartsWith("}}", StringComparison.Ordinal)))
			return BlockState.None;

		IssueCreditsBlock Block = new(this) { Line = Processor.LineIndex, Column = Processor.Column };
		Processor.NewBlocks.Push(Block);
		return AppendLine(Block, Line);
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block Block)
		=> AppendLine((IssueCreditsBlock)Block, Processor.Line.ToString());

	/// <summary>
	/// Collects a source line and consumes the closing line without creating a trailing paragraph.
	/// </summary>
	/// <param name="Block">The directive being collected.</param>
	/// <param name="Line">The current source line.</param>
	/// <returns>The continuation state for Markdig.</returns>
	private static BlockState AppendLine(IssueCreditsBlock Block, string Line)
	{
		Block.Source.AppendLine(Line);
		Block.IsClosed = Line.Contains("}}", StringComparison.Ordinal);
		return Block.IsClosed ? BlockState.BreakDiscard : BlockState.Continue;
	}
}

/// <summary>
/// Holds one role and its separately rendered contributors; a missing role denotes a plain contributor list.
/// </summary>
/// <param name="Role">The optional plain-text role.</param>
/// <param name="Names">The contributor names with inline Markdown.</param>
internal sealed record class IssueCreditEntry(string? Role, IReadOnlyList<string> Names);

/// <summary>
/// Holds one independently expandable or permanently visible group in source order.
/// </summary>
/// <param name="Title">The author-defined plain-text group title.</param>
/// <param name="Featured">Whether the group is permanently visible.</param>
/// <param name="Open">Whether a collapsible group starts expanded.</param>
internal sealed record class IssueCreditGroup(string Title, bool Featured, bool Open)
{
	/// <summary>
	/// Gets the entries belonging to this group, in source order.
	/// </summary>
	public List<IssueCreditEntry> Entries { get; } = [];
}

/// <summary>
/// Emits semantic credits markup with stable class hooks and native disclosure controls.
/// </summary>
/// <param name="NamePipeline">The restricted inline-formatting pipeline with the site's external-link policy.</param>
public sealed class IssueCreditsRenderer(MarkdownPipeline NamePipeline) : HtmlObjectRenderer<IssueCreditsBlock>
{
	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, IssueCreditsBlock Block)
	{
		string Source = Block.Source.ToString().TrimEnd();
		List<IssueCreditGroup>? Groups = Block.IsClosed && Source.EndsWith("}}", StringComparison.Ordinal)
			? ParseGroups(Source[IssueCreditsParser.Marker.Length..^2])
			: null;
		if (Groups is null)
		{
			Renderer.Write("<pre class=\"issue-credits-error\">").WriteEscape(Source).WriteLine("</pre>");
			return;
		}

		Renderer.WriteLine("<aside class=\"issue-credits\">");
		Renderer.WriteLine("<h2 class=\"issue-credits-heading\">Credits</h2>");
		foreach (IssueCreditGroup Group in Groups)
		{
			Renderer.Write(Group.Featured
				? "<section class=\"issue-credits-group issue-credits-group-featured\">"
				: Group.Open ? "<details class=\"issue-credits-group\" open>" : "<details class=\"issue-credits-group\">");
			string TitleTag = Group.Featured ? "h3" : "summary";
			Renderer.Write("<").Write(TitleTag).Write(" class=\"issue-credits-title\">")
				.WriteEscape(Group.Title).Write("</").Write(TitleTag).WriteLine(">");
			Renderer.WriteLine("<div class=\"issue-credits-body\">");
			foreach (IssueCreditEntry Entry in Group.Entries)
			{
				if (Entry.Role is not null)
				{
					Renderer.Write("<dl class=\"issue-credits-entry\"><dt class=\"issue-credits-role\">")
						.WriteEscape(Entry.Role).Write("</dt><dd class=\"issue-credits-people\">");
				}
				Renderer.Write("<ul class=\"issue-credits-names\">");
				foreach (string Name in Entry.Names)
				{
					Renderer.Write("<li class=\"issue-credits-name\">");
					MarkdownDocument Document = Markdown.Parse(Name, NamePipeline);
					foreach (LinkInline Link in Document.Descendants<LinkInline>())
					{
						string Url = Link.Url ?? string.Empty;
						if (Url.Any(char.IsControl) ||
							(Uri.TryCreate(Url, UriKind.Absolute, out Uri? Target) &&
							 Target.Scheme is not ("http" or "https")))
						{
							Link.Url = string.Empty;
						}
					}
					if (Document.Count == 1 && Document[0] is ParagraphBlock Paragraph)
					{
						Renderer.WriteChildren(Paragraph.Inline!);
					}
					else
					{
						Renderer.WriteEscape(Name);
					}
					Renderer.Write("</li>");
				}
				Renderer.WriteLine("</ul>");
				if (Entry.Role is not null)
					Renderer.WriteLine("</dd></dl>");
			}
			Renderer.Write("</div>").WriteLine(Group.Featured ? "</section>" : "</details>");
		}
		Renderer.WriteLine("</aside>");
	}

	/// <summary>
	/// Validates group headers and credit entries without silently discarding misspelled properties or empty groups.
	/// </summary>
	/// <param name="Payload">The content between the directive delimiters.</param>
	/// <returns>Ordered groups, or null when any item violates the authoring contract.</returns>
	private static List<IssueCreditGroup>? ParseGroups(string Payload)
	{
		List<IssueCreditGroup> Groups = [];
		foreach (string Item in Payload.Split("|~|", StringSplitOptions.None))
		{
			Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase);
			List<string> Names = [];
			foreach (string Token in Item.Split("|#|", StringSplitOptions.None))
			{
				int Separator = Token.IndexOf('=');
				if (Separator < 1)
					return null;
				string Key = Token[..Separator].Trim();
				string Value = Token[(Separator + 1)..].Trim();
				if (Value.Length == 0)
					return null;
				if (Key.Equals("Name", StringComparison.OrdinalIgnoreCase))
				{
					Names.Add(Value);
				}
				else if (!Properties.TryAdd(Key, Value))
				{
					return null;
				}
			}
			if (Properties.TryGetValue("Group", out string? Title))
			{
				if (Names.Count != 0 || Properties.Keys.Any(k =>
					!k.Equals("Group", StringComparison.OrdinalIgnoreCase) &&
					!k.Equals("Featured", StringComparison.OrdinalIgnoreCase) &&
					!k.Equals("Open", StringComparison.OrdinalIgnoreCase)))
					return null;
				bool Featured = false;
				bool Open = false;
				if ((Properties.TryGetValue("Featured", out string? FeaturedText) && !bool.TryParse(FeaturedText, out Featured)) ||
					(Properties.TryGetValue("Open", out string? OpenText) && !bool.TryParse(OpenText, out Open)))
					return null;
				Groups.Add(new(Title, Featured, Open));
			}
			else
			{
				if (Groups.Count == 0 || Names.Count == 0 ||
					Properties.Keys.Any(k => !k.Equals("Role", StringComparison.OrdinalIgnoreCase)))
					return null;
				Properties.TryGetValue("Role", out string? Role);
				Groups[^1].Entries.Add(new(Role, Names));
			}
		}
		return Groups.Count > 0 && Groups.All(g => g.Entries.Count > 0) ? Groups : null;
	}
}

/// <summary>
/// Adds issue credits to article Markdown without enabling raw HTML or nested directives in contributor names.
/// </summary>
/// <param name="ApprovedDomains">External domains covered by the site's approved-link policy.</param>
public sealed class IssueCreditsExtension(IReadOnlyList<string> ApprovedDomains) : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		if (!Pipeline.BlockParsers.Contains<IssueCreditsParser>())
			Pipeline.BlockParsers.Insert(0, new IssueCreditsParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		if (Renderer is HtmlRenderer Html && !Html.ObjectRenderers.Contains<IssueCreditsRenderer>())
		{
			MarkdownPipeline Names = new MarkdownPipelineBuilder()
				.Use(new ApprovedNoFollowLinksExtension(ApprovedDomains))
				.DisableHtml()
				.Build();
			Html.ObjectRenderers.Insert(0, new IssueCreditsRenderer(Names));
		}
	}
}
