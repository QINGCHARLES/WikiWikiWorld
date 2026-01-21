using System.Text.RegularExpressions;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Helper for generating URL slugs.
/// </summary>
public static partial class SlugHelper
{
    /// <summary>
    /// Generates a URL-friendly slug from the input string.
    /// </summary>
    /// <param name="input">The input string to slugify.</param>
    /// <returns>A URL-friendly slug.</returns>
    public static string GenerateSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // 1. Convert to lowercase
        string slug = input.ToLowerInvariant();

        // 2. Remove invalid characters (keep only alphanumeric and hyphens)
        // Note: The debug output showed "Cover of GQ Style (USA) - Summer 2017" -> "cover-of-gq-style-usa-summer-2017"
        // Parentheses were removed.
        slug = RemoveInvalidCharsRegex().Replace(slug, "");

        // 3. Replace spaces with hyphens
        slug = slug.Replace(" ", "-");

        // 4. Collapse multiple hyphens
        slug = CollapseHyphensRegex().Replace(slug, "-");

        // 5. Trim hyphens from start and end
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Gets a compiled regex that matches invalid characters for slugs.
    /// </summary>
    /// <returns>A regex that matches non-alphanumeric, non-space, non-hyphen characters.</returns>
    [GeneratedRegex(@"[^a-z0-9\s-]", RegexOptions.Compiled)]
    private static partial Regex RemoveInvalidCharsRegex();

    /// <summary>
    /// Gets a compiled regex that matches multiple consecutive hyphens.
    /// </summary>
    /// <returns>A regex that matches one or more consecutive hyphens.</returns>
    [GeneratedRegex(@"-+", RegexOptions.Compiled)]
    private static partial Regex CollapseHyphensRegex();
}
