using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Net;
using System.Text;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A single timeline entry parsed from a <see cref="TimelineBlock"/>.
/// </summary>
/// <param name="Title">The required event title/heading.</param>
/// <param name="Date">The optional date, year, or timeframe string.</param>
/// <param name="Description">The optional narrative description text with Markdown support.</param>
public sealed record TimelineEntry(string Title, string? Date, string? Description);

/// <summary>
/// A block element representing a vertical timeline with milestone events.
/// Each event is separated from the next by the item separator <c>|~|</c>.
/// Within an event, properties (<c>Title=</c>, <c>Date=</c>, <c>Description=</c>)
/// are separated by the property separator <c>|#|</c>.
/// </summary>
/// <param name="Parser">The block parser.</param>
public class TimelineBlock(BlockParser Parser) : LeafBlock(Parser)
{
	/// <summary>
	/// Gets the raw content of the timeline block (collected between <c>{{Timeline</c> and <c>}}</c>).
	/// </summary>
	public StringBuilder RawContent { get; } = new();

	/// <summary>
	/// Gets the ordered list of timeline entries parsed from the block.
	/// </summary>
	public List<TimelineEntry> Entries { get; } = [];
}

/// <summary>
/// Parses the <c>{{Timeline ...}}</c> block syntax. Accepts either a single-line
/// form or a multi-line form terminated by <c>}}</c>.
/// </summary>
public class TimelineBlockParser : BlockParser
{
	private const string MarkerStart = "{{Timeline";
	private const string MarkerEnd = "}}";

	/// <summary>
	/// Initializes a new instance of the <see cref="TimelineBlockParser"/> class.
	/// </summary>
	public TimelineBlockParser() => OpeningCharacters = ['{'];

	/// <inheritdoc/>
	public override BlockState TryOpen(BlockProcessor Processor)
	{
		ArgumentNullException.ThrowIfNull(Processor);

		StringSlice Slice = Processor.Line;
		if (!Slice.Match(MarkerStart))
		{
			return BlockState.None;
		}

		TimelineBlock Block = new(this)
		{
			Line = Processor.LineIndex,
			Column = Processor.Column
		};

		Slice.Start += MarkerStart.Length;
		string Remaining = Slice.ToString();
		if (!string.IsNullOrWhiteSpace(Remaining))
		{
			int EndPos = Remaining.IndexOf(MarkerEnd, StringComparison.Ordinal);
			if (EndPos != -1)
			{
				Block.RawContent.AppendLine(Remaining[..EndPos]);
				Processor.NewBlocks.Push(Block);
				return BlockState.Break;
			}

			Block.RawContent.AppendLine(Remaining);
		}

		Processor.NewBlocks.Push(Block);
		return BlockState.Continue;
	}

	/// <inheritdoc/>
	public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
	{
		ArgumentNullException.ThrowIfNull(Processor);
		ArgumentNullException.ThrowIfNull(CurrentBlock);

		StringSlice Slice = Processor.Line;
		string Line = Slice.ToString();
		int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
		TimelineBlock Block = CurrentBlock as TimelineBlock ?? throw new InvalidOperationException("Block is not a TimelineBlock.");

		if (EndPos != -1)
		{
			Block.RawContent.AppendLine(Line[..EndPos]);
			return BlockState.Break;
		}

		Block.RawContent.AppendLine(Line);
		return BlockState.Continue;
	}
}

/// <summary>
/// Renders a <see cref="TimelineBlock"/> as an ordered list (<c>&lt;ol class="timeline"&gt;</c>)
/// with vertical dotted connector spine and circular hollow node markers.
/// </summary>
public class TimelineBlockRenderer : HtmlObjectRenderer<TimelineBlock>
{
	private static readonly Lazy<MarkdownPipeline> DescriptionPipeline = new(() =>
		new MarkdownPipelineBuilder()
			.UseAutoLinks()
			.UseEmphasisExtras()
			.UsePipeTables()
			.DisableHtml()
			.Build());

	/// <inheritdoc/>
	protected override void Write(HtmlRenderer Renderer, TimelineBlock Block)
	{
		ArgumentNullException.ThrowIfNull(Renderer);
		ArgumentNullException.ThrowIfNull(Block);

		if (Block.Entries.Count == 0)
		{
			List<TimelineEntry> ParsedEntries = ParseEntries(Block.RawContent.ToString());
			Block.Entries.AddRange(ParsedEntries);
		}

		if (Block.Entries.Count == 0)
		{
			return;
		}

		Renderer.Write("<ol class=\"timeline\">");

		foreach (TimelineEntry Entry in Block.Entries)
		{
			Renderer.Write("<li class=\"timeline-item\">");
			Renderer.Write("<div class=\"timeline-marker\" aria-hidden=\"true\"></div>");
			Renderer.Write("<div class=\"timeline-content\">");
			Renderer.Write($"<h3 class=\"timeline-title\">{WebUtility.HtmlEncode(Entry.Title)}</h3>");

			if (!string.IsNullOrWhiteSpace(Entry.Date))
			{
				Renderer.Write($"<div class=\"timeline-date\">{WebUtility.HtmlEncode(Entry.Date)}</div>");
			}

			if (!string.IsNullOrWhiteSpace(Entry.Description))
			{
				string RenderedDescription = Markdown.ToHtml(Entry.Description, DescriptionPipeline.Value).Trim();
				Renderer.Write($"<div class=\"timeline-body\">{RenderedDescription}</div>");
			}

			Renderer.Write("</div>");
			Renderer.Write("</li>");
		}

		Renderer.Write("</ol>");
	}

	/// <summary>
	/// Parses the raw timeline tag payload into a list of <see cref="TimelineEntry"/> instances.
	/// </summary>
	/// <param name="Raw">The raw text within the timeline tag.</param>
	/// <returns>A list of parsed timeline entries.</returns>
	public static List<TimelineEntry> ParseEntries(string Raw)
	{
		if (string.IsNullOrWhiteSpace(Raw))
		{
			return [];
		}

		const string ItemSeparator = "|~|";
		const string PropertySeparator = "|#|";

		List<TimelineEntry> Entries = [];
		string[] Items = Raw.Split(ItemSeparator, StringSplitOptions.RemoveEmptyEntries);

		foreach (string Item in Items)
		{
			string? Title = null;
			string? Date = null;
			string? Description = null;

			string[] Properties = Item.Split(PropertySeparator, StringSplitOptions.RemoveEmptyEntries);

			foreach (string Property in Properties)
			{
				string Trimmed = Property.Trim();
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

				if (Key.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Heading", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Header", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Name", StringComparison.OrdinalIgnoreCase))
				{
					if (Value.Length > 0)
					{
						Title = Value;
					}
				}
				else if (Key.Equals("Date", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Time", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Period", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Year", StringComparison.OrdinalIgnoreCase))
				{
					if (Value.Length > 0)
					{
						Date = Value;
					}
				}
				else if (Key.Equals("Description", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Body", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Text", StringComparison.OrdinalIgnoreCase) ||
					Key.Equals("Desc", StringComparison.OrdinalIgnoreCase))
				{
					if (Value.Length > 0)
					{
						Description = Value;
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(Title))
			{
				Entries.Add(new TimelineEntry(Title, Date, Description));
			}
		}

		return Entries;
	}
}

/// <summary>
/// A Markdig extension that adds support for vertical timeline blocks via <c>{{Timeline ...}}</c>.
/// </summary>
public class TimelineExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder Pipeline)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);

		if (!Pipeline.BlockParsers.Contains<TimelineBlockParser>())
		{
			Pipeline.BlockParsers.Insert(0, new TimelineBlockParser());
		}
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline Pipeline, IMarkdownRenderer Renderer)
	{
		ArgumentNullException.ThrowIfNull(Pipeline);
		ArgumentNullException.ThrowIfNull(Renderer);

		if (Renderer is HtmlRenderer HtmlRenderer &&
			!HtmlRenderer.ObjectRenderers.Any(R => R is TimelineBlockRenderer))
		{
			HtmlRenderer.ObjectRenderers.Add(new TimelineBlockRenderer());
		}
	}
}
