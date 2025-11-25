using System.Text.RegularExpressions;

namespace WikiWikiWorld.Web.Infrastructure;

public static partial class SlugHelper
{
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

    [GeneratedRegex(@"[^a-z0-9\s-]", RegexOptions.Compiled)]
    private static partial Regex RemoveInvalidCharsRegex();

    [GeneratedRegex(@"-+", RegexOptions.Compiled)]
    private static partial Regex CollapseHyphensRegex();
}
