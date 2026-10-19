namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Builds the canonical, revision-aware storage path and public URL for file images.
/// </summary>
/// <remarks>
/// Files are stored per revision so that uploading a new revision never overwrites
/// an older one. The layout is
/// <c>{SiteId}/images/{CanonicalFileId}/{RevisionId}{extension}</c>, where
/// <c>RevisionId</c> is the <see cref="WikiWikiWorld.Data.Models.FileRevision.Id"/>.
/// This is the single source of truth for that layout; both the on-disk path and the
/// served URL are derived from <see cref="BuildImageRelativePath"/>.
/// </remarks>
public static class ImageUrlHelper
{
	/// <summary>
	/// Builds the path of an image relative to a site's <c>images</c> directory,
	/// e.g. <c>{CanonicalFileId}/{RevisionId}.jpg</c>.
	/// </summary>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="RevisionId">The file revision identifier (<see cref="WikiWikiWorld.Data.Models.FileRevision.Id"/>).</param>
	/// <param name="Filename">The original filename, used only for its extension.</param>
	/// <returns>The forward-slash relative path under the site's <c>images</c> directory.</returns>
	public static string BuildImageRelativePath(Guid CanonicalFileId, int RevisionId, string Filename)
		=> $"{CanonicalFileId}/{RevisionId}{Path.GetExtension(Filename)}";

	/// <summary>
	/// Builds the site-relative URL for a specific file revision's image.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="RevisionId">The file revision identifier (<see cref="WikiWikiWorld.Data.Models.FileRevision.Id"/>).</param>
	/// <param name="Filename">The original filename, used only for its extension.</param>
	/// <returns>The absolute, site-relative URL (e.g. <c>/sitefiles/1/images/{guid}/42.jpg</c>).</returns>
	public static string BuildImageUrl(int SiteId, Guid CanonicalFileId, int RevisionId, string Filename)
		=> $"/sitefiles/{SiteId}/images/{BuildImageRelativePath(CanonicalFileId, RevisionId, Filename)}";
}
