using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Provides helpers for canonical article URL handling.
/// </summary>
public static class ArticleUrlHelper
{
	/// <summary>
	/// Gets the namespace prefix used for file article URLs.
	/// </summary>
	public const string FileNamespacePrefix = "file:";

	/// <summary>
	/// Gets a value indicating whether the supplied slug uses the file namespace prefix.
	/// </summary>
	/// <param name="UrlSlug">The requested article slug.</param>
	/// <returns><see langword="true"/> when the slug starts with <c>file:</c>; otherwise, <see langword="false"/>.</returns>
	public static bool HasFileNamespacePrefix(string? UrlSlug)
	{
		return !string.IsNullOrWhiteSpace(UrlSlug) &&
			UrlSlug.StartsWith(FileNamespacePrefix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Normalizes an incoming article slug for database lookups.
	/// </summary>
	/// <param name="UrlSlug">The requested article slug.</param>
	/// <returns>The bare database slug without the <c>file:</c> namespace prefix.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="UrlSlug"/> is null, empty, or whitespace.</exception>
	public static string NormalizeLookupSlug(string UrlSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(UrlSlug);

		return HasFileNamespacePrefix(UrlSlug)
			? UrlSlug[FileNamespacePrefix.Length..]
			: UrlSlug;
	}

	/// <summary>
	/// Builds the canonical slug for an article type.
	/// </summary>
	/// <param name="UrlSlug">The article slug.</param>
	/// <param name="Type">The article type.</param>
	/// <returns>The canonical slug, including <c>file:</c> for file articles.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="UrlSlug"/> is null, empty, or whitespace.</exception>
	public static string BuildCanonicalSlug(string UrlSlug, ArticleType Type)
	{
		string LookupUrlSlug = NormalizeLookupSlug(UrlSlug);
		return Type == ArticleType.File
			? string.Concat(FileNamespacePrefix, LookupUrlSlug)
			: LookupUrlSlug;
	}

	/// <summary>
	/// Builds the canonical path for an article type.
	/// </summary>
	/// <param name="UrlSlug">The article slug.</param>
	/// <param name="Type">The article type.</param>
	/// <returns>The canonical absolute path for the article.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="UrlSlug"/> is null, empty, or whitespace.</exception>
	public static string BuildArticlePath(string UrlSlug, ArticleType Type)
	{
		return $"/{BuildCanonicalSlug(UrlSlug, Type)}";
	}

	/// <summary>
	/// Builds the canonical path for an article revision.
	/// </summary>
	/// <param name="Revision">The article revision.</param>
	/// <returns>The canonical absolute path for the revision's article.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="Revision"/> is null.</exception>
	public static string BuildArticlePath(ArticleRevision Revision)
	{
		ArgumentNullException.ThrowIfNull(Revision);
		return BuildArticlePath(Revision.UrlSlug, Revision.Type);
	}

	/// <summary>
	/// Builds a canonical path for an article sub-route.
	/// </summary>
	/// <param name="UrlSlug">The article slug.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="RelativePath">The relative sub-route path.</param>
	/// <returns>The canonical absolute path for the sub-route.</returns>
	/// <exception cref="ArgumentException">Thrown when an input is null, empty, or whitespace.</exception>
	public static string BuildRelativePath(string UrlSlug, ArticleType Type, string RelativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(RelativePath);
		return $"{BuildArticlePath(UrlSlug, Type)}/{RelativePath.TrimStart('/')}";
	}

	/// <summary>
	/// Builds a canonical path for an article revision sub-route.
	/// </summary>
	/// <param name="Revision">The article revision.</param>
	/// <param name="RelativePath">The relative sub-route path.</param>
	/// <returns>The canonical absolute path for the sub-route.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="Revision"/> is null.</exception>
	/// <exception cref="ArgumentException">Thrown when <paramref name="RelativePath"/> is null, empty, or whitespace.</exception>
	public static string BuildRelativePath(ArticleRevision Revision, string RelativePath)
	{
		ArgumentNullException.ThrowIfNull(Revision);
		return BuildRelativePath(Revision.UrlSlug, Revision.Type, RelativePath);
	}

	/// <summary>
	/// Builds the canonical path for a specific article revision URL.
	/// </summary>
	/// <param name="Revision">The article revision.</param>
	/// <param name="RevisionTimestamp">The revision timestamp segment.</param>
	/// <returns>The canonical absolute revision path.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="Revision"/> is null.</exception>
	/// <exception cref="ArgumentException">Thrown when <paramref name="RevisionTimestamp"/> is null, empty, or whitespace.</exception>
	public static string BuildRevisionPath(ArticleRevision Revision, string RevisionTimestamp)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(RevisionTimestamp);
		return BuildRelativePath(Revision, $"revision/{RevisionTimestamp}");
	}

	/// <summary>
	/// Gets a value indicating whether a request slug should redirect to the article's canonical namespace.
	/// </summary>
	/// <param name="RequestedUrlSlug">The requested article slug from the route.</param>
	/// <param name="Type">The resolved article type.</param>
	/// <returns><see langword="true"/> when the request does not use the canonical namespace; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentException">Thrown when <paramref name="RequestedUrlSlug"/> is null, empty, or whitespace.</exception>
	public static bool RequiresCanonicalRedirect(string RequestedUrlSlug, ArticleType Type)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(RequestedUrlSlug);

		bool UsesFileNamespace = HasFileNamespacePrefix(RequestedUrlSlug);
		bool ShouldUseFileNamespace = Type == ArticleType.File;
		return UsesFileNamespace != ShouldUseFileNamespace;
	}
}
