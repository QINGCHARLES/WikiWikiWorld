using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Routes otherwise-unmatched request paths into the article page space: article views,
/// per-article actions expressed as URL suffixes (/edit, /history, /talk, /revision, /links),
/// and the 404: namespace. Registered as a dynamic catch-all, so every conventional page,
/// controller, and static file route takes precedence automatically.
/// Suffix parsing is greedy so article slugs containing "/" are supported.
/// </summary>
public sealed class ArticleRouteTransformer : DynamicRouteValueTransformer
{
	/// <summary>
	/// The route value name captured by the catch-all pattern.
	/// </summary>
	public const string SlugRouteValueName = "slug";

	private const string NotFoundNamespacePrefix = "404:";

	/// <summary>
	/// Static-asset extensions that never resolve to an article, so requests for missing
	/// assets fall through to the status-code 404 page instead of the article pipeline.
	/// </summary>
	private static readonly HashSet<string> StaticAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".gif", ".png", ".webp", ".avif", ".heic", ".svg",
		".css", ".js", ".ico", ".txt", ".webmanifest",
		".woff", ".woff2", ".ttf", ".otf", ".eot"
	};

	/// <summary>
	/// Transforms the captured catch-all path into page route values.
	/// </summary>
	/// <param name="HttpContext">The HTTP context.</param>
	/// <param name="Values">The route values produced by the catch-all pattern.</param>
	/// <returns>Route values selecting the target page, or <see langword="null"/> when no page applies.</returns>
	public override ValueTask<RouteValueDictionary> TransformAsync(HttpContext HttpContext, RouteValueDictionary Values)
	{
		if (!Values.TryGetValue(SlugRouteValueName, out object? SlugValue) ||
			SlugValue is not string Slug ||
			string.IsNullOrEmpty(Slug))
		{
			return NoMatch();
		}

		Slug = Slug.TrimEnd('/');
		if (Slug.Length == 0 || Slug.StartsWith("sitefiles/", StringComparison.OrdinalIgnoreCase))
		{
			return NoMatch();
		}

		// "create:" lives here rather than as a page route convention: CreateEdit is also the
		// target of the /edit suffix, and a page reached through this transformer must have a
		// single conventional route or dynamic matches become ambiguous.
		if (Slug.Equals("create:", StringComparison.OrdinalIgnoreCase))
		{
			return Page("/Article/CreateEdit", new RouteValueDictionary());
		}

		if (Slug.StartsWith(NotFoundNamespacePrefix, StringComparison.OrdinalIgnoreCase))
		{
			return Page("/NotFound", new RouteValueDictionary { ["UrlSlug"] = Slug[NotFoundNamespacePrefix.Length..] });
		}

		if (TryStripSuffix(Slug, "/edit", out string? EditSlug))
		{
			return Page("/Article/CreateEdit", new RouteValueDictionary { ["UrlSlug"] = EditSlug });
		}

		if (TryStripSuffix(Slug, "/history", out string? HistorySlug))
		{
			return Page("/Article/History", new RouteValueDictionary { ["UrlSlug"] = HistorySlug });
		}

		if (TrySplitOnLastSegment(Slug, "/talk/", out string? TalkArticleSlug, out string? SubjectSlug))
		{
			return Page("/Article/TalkSubject", new RouteValueDictionary
			{
				["ArticleSlug"] = TalkArticleSlug,
				["SubjectSlug"] = SubjectSlug
			});
		}

		if (TryStripSuffix(Slug, "/talk", out string? TalkSlug))
		{
			return Page("/Article/TalkSubjects", new RouteValueDictionary { ["ArticleSlug"] = TalkSlug });
		}

		if (TrySplitOnLastSegment(Slug, "/revision/", out string? RevisionArticleSlug, out string? Revision))
		{
			return Page("/Article/View", new RouteValueDictionary
			{
				["UrlSlug"] = RevisionArticleSlug,
				["Revision"] = Revision
			});
		}

		if (TryStripSuffix(Slug, "/links", out string? LinksSlug))
		{
			return Page("/Article/Links", new RouteValueDictionary { ["UrlSlug"] = LinksSlug });
		}

		// Requests for missing static assets never resolve to an article; fall through so the
		// status-code pipeline serves a plain 404.
		int LastDotIndex = Slug.LastIndexOf('.');
		if (LastDotIndex >= 0 && StaticAssetExtensions.Contains(Slug[LastDotIndex..]))
		{
			return NoMatch();
		}

		return Page("/Article/View", new RouteValueDictionary { ["UrlSlug"] = Slug });
	}

	private static ValueTask<RouteValueDictionary> Page(string PagePath, RouteValueDictionary Values)
	{
		Values["page"] = PagePath;
		return ValueTask.FromResult(Values);
	}

	private static ValueTask<RouteValueDictionary> NoMatch()
	{
		return ValueTask.FromResult<RouteValueDictionary>(null!);
	}

	private static bool TryStripSuffix(string Slug, string Suffix, out string? Prefix)
	{
		if (Slug.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) && Slug.Length > Suffix.Length)
		{
			Prefix = Slug[..^Suffix.Length];
			return true;
		}

		Prefix = null;
		return false;
	}

	private static bool TrySplitOnLastSegment(string Slug, string Separator, out string? Prefix, out string? Rest)
	{
		int SeparatorIndex = Slug.LastIndexOf(Separator, StringComparison.OrdinalIgnoreCase);
		if (SeparatorIndex > 0 && SeparatorIndex + Separator.Length < Slug.Length)
		{
			Prefix = Slug[..SeparatorIndex];
			Rest = Slug[(SeparatorIndex + Separator.Length)..];
			return true;
		}

		Prefix = null;
		Rest = null;
		return false;
	}
}
