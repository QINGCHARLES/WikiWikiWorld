using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Text;

namespace WikiWikiWorld.Web.MarkdigExtensions;

/// <summary>
/// A block element representing an infobox for a publication issue.
/// </summary>
/// <param name="Parser">The block parser.</param>
public class PublicationIssueInfoboxBlock(BlockParser Parser) : LeafBlock(Parser)
{
    /// <summary>
    /// Gets the raw content of the infobox.
    /// </summary>
    public StringBuilder RawContent { get; } = new();

    /// <summary>
    /// Gets the collection of parsed properties.
    /// </summary>
    public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

}

/// <summary>
/// Parses the {{PublicationIssueInfobox ...}} block syntax.
/// </summary>
public class PublicationIssueInfoboxBlockParser : BlockParser
{
    private const string MarkerStart = "{{PublicationIssueInfobox";
    private const string MarkerEnd = "}}";

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicationIssueInfoboxBlockParser"/> class.
    /// </summary>
    public PublicationIssueInfoboxBlockParser() => OpeningCharacters = ['{'];

    /// <inheritdoc/>
    public override BlockState TryOpen(BlockProcessor Processor)
    {
        StringSlice Slice = Processor.Line;
        if (!Slice.Match(MarkerStart))
        {
            return BlockState.None;
        }

        PublicationIssueInfoboxBlock IssueBlock = new(this)
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
                IssueBlock.RawContent.AppendLine(Remaining[..EndPos]);
                Processor.NewBlocks.Push(IssueBlock);
                return BlockState.Break;
            }
            IssueBlock.RawContent.AppendLine(Remaining);
        }

        Processor.NewBlocks.Push(IssueBlock);
        return BlockState.Continue;
    }

    /// <inheritdoc/>
    public override BlockState TryContinue(BlockProcessor Processor, Block CurrentBlock)
    {
        StringSlice Slice = Processor.Line;
        string Line = Slice.ToString();
        int EndPos = Line.IndexOf(MarkerEnd, StringComparison.Ordinal);
        PublicationIssueInfoboxBlock IssueBlock = CurrentBlock as PublicationIssueInfoboxBlock ?? throw new InvalidOperationException("Block is not a PublicationIssueInfoboxBlock.");

        if (EndPos != -1)
        {
            IssueBlock.RawContent.AppendLine(Line[..EndPos]);
            return BlockState.Break;
        }
        IssueBlock.RawContent.AppendLine(Line);
        return BlockState.Continue;
    }
}

/// <summary>
/// Renders the <see cref="PublicationIssueInfoboxBlock"/> as an HTML aside containing publication facts only. Legacy CoverImage properties are ignored.
/// </summary>
public class PublicationIssueInfoboxRenderer : HtmlObjectRenderer<PublicationIssueInfoboxBlock>
{

    /// <inheritdoc/>
    protected override void Write(HtmlRenderer Renderer, PublicationIssueInfoboxBlock Block)
    {
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Block);

        const string AttributeSeparator = "|#|";

        // Parse facts once; cover references belong in a separate CoverCarousel block.
        if (Block.Properties.Count == 0)
        {
            string Raw = Block.RawContent.ToString();
            string[] Tokens = Raw.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

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

                if (!Block.Properties.TryGetValue(Key, out List<string>? List))
                {
                    List = [];
                    Block.Properties[Key] = List;
                }
                List.Add(Value);
            }
        }

        Renderer.Write("<aside class=\"infobox\">");

        List<KeyValuePair<string, List<string>>> OtherProps = [.. Block.Properties
            .Where(KeyValuePair => !KeyValuePair.Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))];

        if (OtherProps.Any())
        {
            Renderer.Write("<dl>");
            foreach (KeyValuePair<string, List<string>> KeyValuePair in OtherProps)
            {
                string Friendly = GetFriendlyName(KeyValuePair.Key);
                foreach (string Val in KeyValuePair.Value)
                {
                    Renderer.Write($"<dt>{Friendly}</dt><dd>{Val}</dd>");
                }
            }
            Renderer.Write("</dl>");
        }

        Renderer.Write("</aside>");
    }

    /// <summary>
    /// Converts a property key to a user-friendly display name.
    /// </summary>
    /// <param name="Key">The property key.</param>
    /// <returns>A friendly display name for the property.</returns>
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

/// <summary>
/// A Markdig extension that adds support for publication issue infoboxes.
/// </summary>
public class PublicationIssueInfoboxExtension : IMarkdownExtension
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
        if (Renderer is HtmlRenderer HtmlRenderer &&
            !HtmlRenderer.ObjectRenderers.Any(R => R is PublicationIssueInfoboxRenderer))
        {
            HtmlRenderer.ObjectRenderers.Add(new PublicationIssueInfoboxRenderer());
        }
    }

}
