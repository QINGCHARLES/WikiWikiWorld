#nullable enable

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;

namespace WikiWikiWorld.Web;

/// <summary>
/// A HTML markup formatter that is aware of inline and block elements to applying pretty formatting.
/// </summary>
public sealed class InlineAwarePrettyFormatter : PrettyMarkupFormatter
{
    private enum ContainerMode
    {
        Block,
        InlineContainer,
        InlineElement
    }

    // Expanded list including SVG and Form elements to prevent unwanted breaks
    private static readonly HashSet<string> InlineElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","abbr","b","bdi","bdo","br","cite","code","data","dfn","em","i","img","input",
        "kbd","label","mark","q","s","samp","small","span","strong","sub","sup","time","u",
        "var","wbr","button","select","textarea","option",
        "svg","path","circle","rect","line","polyline","polygon","text","g","defs","symbol","use"
    };

    private static readonly HashSet<string> PreformattedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "textarea", "script", "style", "template"
    };

    private static readonly IMarkupFormatter Plain = HtmlMarkupFormatter.Instance;
    private readonly Stack<ContainerMode> ModeStack = new();

    /// <inheritdoc/>
    public override string Doctype(IDocumentType Doctype)
    {
        // Fix merged doctype/html tag
        return base.Doctype(Doctype) + NewLine;
    }

    /// <inheritdoc/>
    public override string OpenTag(IElement Element, bool SelfClosing)
    {
        ContainerMode Mode = Classify(Element);
        ModeStack.Push(Mode);

        if (Mode is ContainerMode.InlineElement)
        {
            return Plain.OpenTag(Element, SelfClosing);
        }

        if (Mode is ContainerMode.InlineContainer)
        {
            string Pretty = base.OpenTag(Element, SelfClosing);
            // Indent the tag, but DON'T put a newline after it.
            return TrimTrailingNewLineAndIndentation(Pretty);
        }

        return base.OpenTag(Element, SelfClosing);
    }

    /// <inheritdoc/>
    public override string CloseTag(IElement Element, bool SelfClosing)
    {
        ContainerMode Mode = ModeStack.Count > 0 ? ModeStack.Pop() : ContainerMode.Block;

        if (Mode is ContainerMode.InlineElement)
        {
            return Plain.CloseTag(Element, SelfClosing);
        }

        if (Mode is ContainerMode.InlineContainer)
        {
            string Pretty = base.CloseTag(Element, SelfClosing);
            // Don't indent the closing tag; let it hug the text content.
            return TrimLeadingNewLineAndIndentation(Pretty);
        }

        return base.CloseTag(Element, SelfClosing);
    }

    /// <inheritdoc/>
    public override string Text(ICharacterData Text)
    {
        // Use Plain formatter for preserved text (textarea, pre) to avoid accidental re-indentation
        if (IsInPreformattedContext(Text))
        {
            return Plain.Text(Text);
        }

        ContainerMode Current = ModeStack.Count > 0 ? ModeStack.Peek() : ContainerMode.Block;

        if (Current is ContainerMode.InlineContainer or ContainerMode.InlineElement)
        {
            return Plain.Text(Text);
        }

        return base.Text(Text);
    }

    /// <summary>
    /// Determines if the node is an inline HTML element.
    /// </summary>
    /// <param name="Node">The node to check.</param>
    /// <returns>True if the node is an inline element; otherwise, false.</returns>
    private static bool IsInline(INode Node) =>
        Node is IElement Element && InlineElements.Contains(Element.NodeName);

    /// <summary>
    /// Determines if the node is within a preformatted context (pre, textarea, script, style, or contenteditable).
    /// </summary>
    /// <param name="Node">The node to check.</param>
    /// <returns>True if in a preformatted context; otherwise, false.</returns>
    private static bool IsInPreformattedContext(INode Node)
    {
        var current = Node as IElement ?? Node.ParentElement;
        while (current != null)
        {
            if (PreformattedElements.Contains(current.LocalName)) return true;
            
            // Check contenteditable
            var attr = current.GetAttribute("contenteditable");
            if (attr != null && !attr.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            current = current.ParentElement;
        }
        return false;
    }

    /// <summary>
    /// Classifies an element as Block, InlineContainer, or InlineElement.
    /// </summary>
    /// <param name="Element">The element to classify.</param>
    /// <returns>The container mode for the element.</returns>
    private ContainerMode Classify(IElement Element)
    {
        if (IsInline(Element)) return ContainerMode.InlineElement;

        // Force script/style/pre to be Block so they get proper lines
        if (PreformattedElements.Contains(Element.NodeName))
        {
            return ContainerMode.Block;
        }

        return !IsInPreformattedContext(Element) && HasOnlyInlineContent(Element)
            ? ContainerMode.InlineContainer
            : ContainerMode.Block;
    }

    /// <summary>
    /// Determines if the element contains only inline content (text and inline elements).
    /// </summary>
    /// <param name="Element">The element to check.</param>
    /// <returns>True if all children are inline content; otherwise, false.</returns>
    private static bool HasOnlyInlineContent(IElement Element)
    {
        INode? Current = Element.FirstChild;
        while (Current is not null)
        {
            if (Current is IText)
            {
                Current = Current.NextSibling;
                continue;
            }
            if (Current is IElement ChildElement)
            {
                if (IsInline(ChildElement))
                {
                    Current = Current.NextSibling;
                    continue;
                }
                return false;
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// Removes leading newline and indentation from a string.
    /// </summary>
    /// <param name="Value">The string to trim.</param>
    /// <returns>The trimmed string.</returns>
    private string TrimLeadingNewLineAndIndentation(string Value)
    {
        if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(NewLine)) return Value;
        
        int Start = 0;
        if (Value.StartsWith(NewLine, StringComparison.Ordinal))
        {
            Start = NewLine.Length;
            while (Start < Value.Length && (Value[Start] is ' ' or '\t')) Start++;
        }
        return Start > 0 ? Value[Start..] : Value;
    }

    /// <summary>
    /// Removes trailing newline and indentation from a string.
    /// </summary>
    /// <param name="Value">The string to trim.</param>
    /// <returns>The trimmed string.</returns>
    private string TrimTrailingNewLineAndIndentation(string Value)
    {
        if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(NewLine)) return Value;

        if (!Value.EndsWith(NewLine, StringComparison.Ordinal)) return Value;

        int Cut = Value.Length - NewLine.Length;
        while (Cut > 0 && (Value[Cut - 1] is ' ' or '\t')) Cut--;

        return Value[..Cut];
    }
}