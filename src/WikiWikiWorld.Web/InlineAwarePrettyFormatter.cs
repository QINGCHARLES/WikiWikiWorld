#nullable enable

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;

namespace WikiWikiWorld.Web;

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

    public override string Doctype(IDocumentType Doctype)
    {
        // Fix merged doctype/html tag
        return base.Doctype(Doctype) + NewLine;
    }

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

    private static bool IsInline(INode Node) =>
        Node is IElement Element && InlineElements.Contains(Element.NodeName);

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

    private string TrimTrailingNewLineAndIndentation(string Value)
    {
        if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(NewLine)) return Value;

        if (!Value.EndsWith(NewLine, StringComparison.Ordinal)) return Value;

        int Cut = Value.Length - NewLine.Length;
        while (Cut > 0 && (Value[Cut - 1] is ' ' or '\t')) Cut--;

        return Value[..Cut];
    }
}