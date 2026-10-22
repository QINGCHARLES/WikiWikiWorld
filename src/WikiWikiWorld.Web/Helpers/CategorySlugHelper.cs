using System.Globalization;
using System.Text;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Generates native-script category identities using Django's Unicode slugify approach.
/// </summary>
/// <remarks>
/// Adapted from Django 5.2 django/utils/text.py; see ThirdPartyNotices/Django-LICENSE.txt.
/// Deliberately preserves combining marks and uses .NET invariant casing and Unicode scalar values.
/// </remarks>
public static class CategorySlugHelper
{
	/// <summary>
	/// Normalizes compatibility forms, lowercases, removes punctuation, and collapses whitespace and hyphens.
	/// </summary>
	/// <param name="Label">The category label from which to derive an identity.</param>
	/// <returns>The derived slug, or an empty string when no usable characters remain.</returns>
	/// <exception cref="ArgumentNullException">The label is null.</exception>
	/// <exception cref="ArgumentException">The label contains invalid Unicode.</exception>
	public static string Generate(string Label)
	{
		ArgumentNullException.ThrowIfNull(Label);
		string Normalized = Label.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
		StringBuilder Result = new();
		bool PendingSeparator = false;
		foreach (Rune Character in Normalized.EnumerateRunes())
		{
			if (Rune.IsWhiteSpace(Character) || Character.Value == '-')
			{
				PendingSeparator = Result.Length > 0;
				continue;
			}

			UnicodeCategory Category = Rune.GetUnicodeCategory(Character);
			if (!Rune.IsLetterOrDigit(Character)
				&& Category is not (UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
					or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
				&& Character.Value != '_') continue;

			if (PendingSeparator) Result.Append('-');
			PendingSeparator = false;
			Result.Append(Character.ToString());
		}
		return Result.ToString().Trim('-', '_');
	}

	/// <summary>
	/// Checks an explicit slug without rewriting its spelling, casing, or Unicode representation.
	/// </summary>
	/// <param name="Slug">The already-trimmed slug without a namespace prefix.</param>
	/// <returns>Whether the value is a nonempty single URL path segment usable in the category directive.</returns>
	public static bool IsValidExplicitSlug(string Slug)
	{
		if (string.IsNullOrEmpty(Slug) || Slug is "." or "..") return false;
		foreach (Rune Character in Slug.EnumerateRunes())
		{
			if (Rune.IsWhiteSpace(Character) || Rune.IsControl(Character)
				|| Character == Rune.ReplacementChar
				|| "/\\?#%:|{}<>\"".Contains(Character.ToString(), StringComparison.Ordinal)) return false;
		}
		return true;
	}
}
