#nullable enable

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;

namespace WikiWikiWorld.Web;

/// <summary>
/// A pretty markup formatter that is aware of inline elements and avoids adding unnecessary whitespace around them.
/// </summary>
public sealed class InlineAwarePrettyFormatter : PrettyMarkupFormatter
{
	private enum ContainerMode
	{
		Block,
		InlineContainer,
		InlineElement
	}

	private static readonly HashSet<string> InlineElements = new(StringComparer.OrdinalIgnoreCase)
	{
		"a","abbr","b","bdi","bdo","br","cite","code","data","dfn","em","i","img","input",
		"kbd","label","mark","q","s","samp","small","span","strong","sub","sup","time","u",
		"var","wbr","button","select","textarea","svg","math"
	};

	private static readonly HashSet<string> PreformattedElements = new(StringComparer.OrdinalIgnoreCase)
	{
		"pre","textarea"
	};

	private static readonly IMarkupFormatter Plain = Instance;

	private readonly Stack<ContainerMode> ModeStack = new();

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
			return TrimLeadingNewLineAndIndentation(Pretty);
		}

		return base.CloseTag(Element, SelfClosing);
	}

	public override string Text(ICharacterData Text)
	{
		if (IsInPreformattedContext(Text))
		{
			return base.Text(Text);
		}

		ContainerMode Current = ModeStack.Count > 0 ? ModeStack.Peek() : ContainerMode.Block;

		if (Current is ContainerMode.InlineContainer or ContainerMode.InlineElement)
		{
			return string.IsNullOrWhiteSpace(Text.Data) ? string.Empty : Plain.Text(Text);
		}

		return base.Text(Text);
	}

	public override string LiteralText(ICharacterData Text)
	{
		ContainerMode Current = ModeStack.Count > 0 ? ModeStack.Peek() : ContainerMode.Block;
		return Current is ContainerMode.InlineContainer or ContainerMode.InlineElement
			? Plain.LiteralText(Text)
			: base.LiteralText(Text);
	}

	public override string Comment(IComment Comment)
	{
		ContainerMode Current = ModeStack.Count > 0 ? ModeStack.Peek() : ContainerMode.Block;
		return Current is ContainerMode.InlineElement
			? Plain.Comment(Comment)
			: base.Comment(Comment);
	}

	public override string Processing(IProcessingInstruction Instruction)
	{
		ContainerMode Current = ModeStack.Count > 0 ? ModeStack.Peek() : ContainerMode.Block;
		return Current is ContainerMode.InlineElement
			? Plain.Processing(Instruction)
			: base.Processing(Instruction);
	}

	private static bool IsInline(INode Node) =>
		Node is IElement Element && InlineElements.Contains(Element.NodeName);

	private static bool IsInPreformattedContext(INode Node)
	{
		IElement? Parent = Node.ParentElement;
		while (Parent is not null)
		{
			if (PreformattedElements.Contains(Parent.NodeName))
			{
				return true;
			}
			Parent = Parent.ParentElement;
		}
		return false;
	}

	private ContainerMode Classify(IElement Element)
	{
		if (IsInline(Element))
		{
			return ContainerMode.InlineElement;
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
		if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(NewLine))
		{
			return Value;
		}

		int Start = 0;

		if (Value.StartsWith(NewLine, StringComparison.Ordinal))
		{
			Start = NewLine.Length;
			while (Start < Value.Length && (Value[Start] is ' ' or '\t'))
			{
				Start++;
			}
		}

		return Start > 0 ? Value[Start..] : Value;
	}

	private string TrimTrailingNewLineAndIndentation(string Value)
	{
		if (string.IsNullOrEmpty(Value) || string.IsNullOrEmpty(NewLine))
		{
			return Value;
		}

		if (!Value.EndsWith(NewLine, StringComparison.Ordinal))
		{
			return Value;
		}

		int Cut = Value.Length - NewLine.Length;

		while (Cut > 0 && (Value[Cut - 1] is ' ' or '\t'))
		{
			Cut--;
		}

		return Value[..Cut];
	}
}
