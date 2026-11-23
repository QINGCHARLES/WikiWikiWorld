using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>The public facts extracted from a Magazedia article's parsed Markdown.</summary>
/// <param name="IsIssue">Whether the subject is a single issue of a serial publication.</param>
/// <param name="Lead">A short, plain-text description from the article lead, if available.</param>
/// <param name="Fields">Recognized facts, preserving repeated fields in source order.</param>
/// <param name="Cover">The front cover from the first carousel, if its primary image resolved.</param>
public sealed partial record PublicationFacts(
	bool IsIssue, string? Lead, IReadOnlyList<PublicationFact> Fields, CoverCarouselImage? Cover)
{
	/// <summary>Identifies names that may provide subject facts and their links.</summary>
	private static readonly HashSet<string> FactKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"ISSN", "Language", "Publisher", "Editor", "Website", "Publication", "Issue",
		"CoverDate", "PrintPublicationDate", "ElectronicPublicationDate"
	};

	/// <summary>Classifies the article and projects public facts from its existing parsed blocks.</summary>
	/// <param name="Document">The enriched article document.</param>
	/// <returns>The subject facts, or null when the article lacks a clear publication subject.</returns>
	internal static PublicationFacts? Extract(MarkdownDocument Document)
	{
		InfoboxBlock[] Boxes = [.. Document.OfType<InfoboxBlock>().Where(Box => GetBoxType(Box).HasValue)];
		bool? BoxIsIssue = Boxes.FirstOrDefault() is { } FirstBox ? GetBoxType(FirstBox) : null;
		HashSet<string> BoxKeys = new(StringComparer.OrdinalIgnoreCase);
		foreach (InfoboxBlock Box in Boxes)
		{
			// A publication may split one infobox across its lead. A repeated field across boxes can describe another subject.
			if (GetBoxType(Box) != BoxIsIssue || Box.Properties.Keys.Any(Key => !BoxKeys.Add(Key))) return null;
		}
		EyebrowType? Eyebrow = Document.GetData(EyebrowExtension.TypeDocumentKey) as EyebrowType?;
		bool? EyebrowIsIssue = Eyebrow switch
		{
			EyebrowType Type when EyebrowExtension.IsPublicationIssue(Type) => true,
			EyebrowType.Magazine or EyebrowType.Zine or EyebrowType.Journal or EyebrowType.Newsletter => false,
			_ => null
		};
		if (Eyebrow.HasValue && !EyebrowIsIssue.HasValue
			|| BoxIsIssue.HasValue && EyebrowIsIssue.HasValue && BoxIsIssue != EyebrowIsIssue
			|| (BoxIsIssue ?? EyebrowIsIssue) is not { } IsIssue)
		{
			return null;
		}

		List<PublicationFact> Fields = [];
		foreach (InfoboxBlock Box in Boxes)
		{
			foreach (InfoboxFactBlock Fact in Box.OfType<InfoboxFactBlock>())
			{
				if (!FactKeys.Contains(Fact.Key)
					|| Box.Schema?.Fields.Any(Field => Field.Key.Equals(Fact.Key, StringComparison.OrdinalIgnoreCase)) != true)
					continue;
				string Value = PlainText(Fact.Inline);
				if (Value.Length == 0 || Value is "?" or "—" || Value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
					continue;
				string? Link = Fact.Inline?.Descendants<LinkInline>().Where(Item => !Item.IsImage)
					.Select(Item => Item.Url).Take(2).ToArray() is [string OnlyLink] ? OnlyLink : null;
				Fields.Add(new PublicationFact(Fact.Key, Value, Link));
			}
		}

		string? Lead = Document.OfType<ParagraphBlock>().Select(Paragraph => PlainText(Paragraph.Inline))
			.FirstOrDefault(Value => Value.Length >= 30);
		if (Lead is not null)
		{
			int SentenceEnd = SentenceBoundary().Match(Lead) is { Success: true } Match ? Match.Index + Match.Length : Lead.Length;
			Lead = Lead[..SentenceEnd];
			if (Lead.Length > 220)
			{
				int Cut = Lead.LastIndexOf(' ', 219);
				Lead = Lead[..(Cut >= 100 ? Cut : 219)] + "…";
			}
		}
		CoverCarouselImage? Cover = IsIssue ? Document.OfType<CoverCarouselBlock>().FirstOrDefault()?.CachedCoverImages.FirstOrDefault() : null;
		if (Cover is not null && (Cover.ImageUrl.EndsWith("/missing-image.png", StringComparison.OrdinalIgnoreCase)
			|| Cover.AltText.Contains("back cover", StringComparison.OrdinalIgnoreCase)
			|| Cover.Caption?.Contains("back cover", StringComparison.OrdinalIgnoreCase) == true))
		{
			Cover = null;
		}
		return new PublicationFacts(IsIssue, Lead, Fields, Cover);
	}

	/// <summary>Recognizes a closed, configured infobox that describes a publication subject.</summary>
	/// <param name="Box">A top-level infobox block.</param>
	/// <returns>Whether it describes an issue, or null for other infoboxes.</returns>
	private static bool? GetBoxType(InfoboxBlock Box) =>
		Box is { ClosingPosition: >= 0, Schema: not null } ? Box.TypeName?.ToUpperInvariant() switch
		{
			"PUBLICATION" => false,
			"PUBLICATIONISSUE" => true,
			_ => null
		} : null;

	/// <summary>Reads the one usable field value when the author supplied it unambiguously.</summary>
	/// <param name="Key">The schema field to find.</param>
	/// <returns>The single fact or null for a missing or repeated value.</returns>
	internal PublicationFact? Single(string Key) =>
		Fields.Where(Fact => Fact.Key.Equals(Key, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray() is [PublicationFact Only]
			? Only : null;

	/// <summary>Reads text and links' labels from inline Markdown, leaving citation and footnote nodes out.</summary>
	/// <param name="Container">The parsed inline content.</param>
	/// <returns>Whitespace-normalized text without markup.</returns>
	private static string PlainText(ContainerInline? Container)
	{
		StringBuilder Text = new();
		AppendText(Container, Text);
		return Whitespace().Replace(Text.ToString(), " ").Trim();
	}

	/// <summary>Appends only the inline nodes that contribute readable prose.</summary>
	/// <param name="Container">The current inline container.</param>
	/// <param name="Text">The destination for readable prose.</param>
	private static void AppendText(ContainerInline? Container, StringBuilder Text)
	{
		for (Inline? Node = Container?.FirstChild; Node is not null; Node = Node.NextSibling)
		{
			switch (Node)
			{
				case LiteralInline Literal: Text.Append(Literal.Content.ToString()); break;
				case HtmlEntityInline Entity: Text.Append(Entity.Transcoded.ToString()); break;
				case CodeInline Code: Text.Append(Code.Content); break;
				case LineBreakInline: Text.Append(' '); break;
				case LinkInline { IsImage: false } Link: AppendText(Link, Text); break;
				case EmphasisInline Emphasis: AppendText(Emphasis, Text); break;
			}
		}
	}

	/// <summary>Finds runs of spacing in extracted prose.</summary>
	/// <returns>A matcher for Unicode whitespace.</returns>
	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	/// <summary>Finds a sentence end followed by whitespace, or a CJK sentence stop.</summary>
	/// <returns>A matcher that leaves the punctuation in the summary.</returns>
	[GeneratedRegex(@"[.!?](?=\s|$)|[。！？]")]
	private static partial Regex SentenceBoundary();
}

/// <summary>One plain-text infobox fact and its single link destination, when present.</summary>
/// <param name="Key">The schema field key.</param>
/// <param name="Value">The visible text without footnote markers.</param>
/// <param name="Link">The destination as written in the markup, if the value has exactly one link.</param>
public sealed record PublicationFact(string Key, string Value, string? Link);
