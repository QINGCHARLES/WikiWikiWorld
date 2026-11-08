using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Response headers for user-uploaded files served from <c>/sitefiles</c>. They keep uploads inert:
/// an uploaded file must never become an executable document with the application's origin and the
/// viewer's credentials.
/// </summary>
public static class UploadedFileResponseHeaders
{
	/// <summary>
	/// Cache lifetime for uploaded files. Revision-keyed paths make the content at a URL immutable.
	/// </summary>
	public const string CacheControl = "public, max-age=2592000";

	/// <summary>
	/// Policy for SVG responses. <c>sandbox</c> gives a directly opened SVG an opaque origin with
	/// scripts disabled, so it cannot read the application's pages or cookies. <c>default-src 'none'</c>
	/// additionally blocks scripts and every network fetch. Inline styles and <c>data:</c> images stay
	/// allowed so ordinary vector graphics still render, and framing stays forbidden like the rest
	/// of the site.
	/// </summary>
	public const string SvgContentSecurityPolicy =
		"sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'; frame-ancestors 'none'";

	/// <summary>
	/// Applies the cache, MIME-sniffing and SVG isolation headers to a static-file response. Runs for
	/// full, ranged, HEAD and 304 responses because static-file middleware invokes it after it has set
	/// the content type.
	/// </summary>
	/// <remarks>
	/// The site-wide security-header middleware leaves an existing Content-Security-Policy in place, so
	/// the stricter SVG policy set here is not overwritten when the response starts.
	/// </remarks>
	/// <param name="FileContext">The static-file response being prepared.</param>
	public static void Apply(StaticFileResponseContext FileContext)
	{
		ArgumentNullException.ThrowIfNull(FileContext);

		IHeaderDictionary Headers = FileContext.Context.Response.Headers;
		Headers[HeaderNames.CacheControl] = CacheControl;
		Headers[HeaderNames.XContentTypeOptions] = "nosniff";

		if (IsSvg(FileContext.Context.Response.ContentType, FileContext.File.Name))
		{
			Headers[HeaderNames.ContentSecurityPolicy] = SvgContentSecurityPolicy;
		}
	}

	/// <summary>
	/// Decides whether a response is an SVG document from its content type, or from its file name
	/// when no content type has been set.
	/// </summary>
	/// <param name="ContentType">The response content type, which may include parameters.</param>
	/// <param name="FileName">The served file's name.</param>
	/// <returns><see langword="true"/> for <c>image/svg+xml</c> or an <c>.svg</c>/<c>.svgz</c> file.</returns>
	private static bool IsSvg(string? ContentType, string FileName) =>
		(ContentType is not null && ContentType.StartsWith("image/svg+xml", StringComparison.OrdinalIgnoreCase))
		|| FileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
		|| FileName.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
}
