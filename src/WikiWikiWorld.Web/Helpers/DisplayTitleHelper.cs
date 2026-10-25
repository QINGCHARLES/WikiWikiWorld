using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Parses and renders article <c>DisplayTitle</c> values, supporting embedded SVG logo
/// tokens using the <c>[[Fallback Text|#|file-slug]]</c> syntax.
/// </summary>
public static partial class DisplayTitleHelper
{
	/// <summary>
	/// Matches <c>[[FallbackText|#|FileSlug]]</c> tokens inside a display title.
	/// </summary>
	[GeneratedRegex(@"\[\[(?<fallback>[^\]|]+?)\|#\|(?<slug>[^\]]+?)\]\]", RegexOptions.CultureInvariant)]
	private static partial Regex LogoTokenRegex();

	/// <summary>
	/// Converts a display title into a clean, single-line plain-text string suitable for
	/// <c>&lt;title&gt;</c> elements, <c>alt</c> attributes, and inline text references.
	/// Replaces any <c>[[FallbackText|#|file-slug]]</c> tokens with <c>FallbackText</c>
	/// and collapses line breaks into spaces.
	/// </summary>
	/// <param name="DisplayTitle">The optional display title containing markup or line breaks.</param>
	/// <param name="FallbackTitle">The canonical article title used when <paramref name="DisplayTitle"/> is empty.</param>
	/// <returns>The plain-text title string.</returns>
	public static string ToPlainText(string? DisplayTitle, string? FallbackTitle)
	{
		string Source = string.IsNullOrWhiteSpace(DisplayTitle)
			? (FallbackTitle ?? string.Empty)
			: DisplayTitle;

		if (string.IsNullOrWhiteSpace(Source))
		{
			return string.Empty;
		}

		string WithoutTokens = LogoTokenRegex().Replace(Source, Match => Match.Groups["fallback"].Value.Trim());
		string[] Lines = WithoutTokens.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return string.Join(" ", Lines);
	}

	/// <summary>
	/// Renders a display title as safe HTML for an <c>&lt;h1&gt;</c> heading, resolving any
	/// <c>[[FallbackText|#|file-slug]]</c> tokens against current file articles in the database.
	/// Unresolved file slugs gracefully fall back to the HTML-encoded <c>FallbackText</c>.
	/// Visible text gains optional line breaks after slashes without changing stored titles or logo URLs.
	/// </summary>
	/// <param name="DisplayTitle">The optional display title containing markup or line breaks.</param>
	/// <param name="FallbackTitle">The canonical article title used when <paramref name="DisplayTitle"/> is empty.</param>
	/// <param name="Context">The database context used to resolve file slugs.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The rendered HTML string safe for <c>@Html.Raw(...)</c> inside <c>&lt;h1&gt;</c>.</returns>
	public static async Task<string> RenderHtmlAsync(
		string? DisplayTitle,
		string? FallbackTitle,
		WikiWikiWorldDbContext Context,
		int SiteId,
		string Culture,
		CancellationToken CancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(Context);

		string Source = string.IsNullOrWhiteSpace(DisplayTitle)
			? (FallbackTitle ?? string.Empty)
			: DisplayTitle;

		if (string.IsNullOrWhiteSpace(Source))
		{
			return string.Empty;
		}

		MatchCollection Matches = LogoTokenRegex().Matches(Source);
		Dictionary<string, ResolvedLogoFile> ResolvedLogos = new(StringComparer.OrdinalIgnoreCase);

		if (Matches.Count > 0)
		{
			HashSet<string> LookupSlugs = new(StringComparer.OrdinalIgnoreCase);
			foreach (Match TokenMatch in Matches)
			{
				string RawSlug = TokenMatch.Groups["slug"].Value.Trim();
				if (!string.IsNullOrWhiteSpace(RawSlug))
				{
					LookupSlugs.Add(ArticleUrlHelper.NormalizeLookupSlug(RawSlug));
				}
			}

			if (LookupSlugs.Count > 0)
			{
				List<string> SlugList = [.. LookupSlugs];
				var FileArticles = await Context.ArticleRevisions
					.AsNoTracking()
					.Where(A => A.SiteId == SiteId
						&& A.Culture == Culture
						&& A.IsCurrent
						&& A.DateDeleted == null
						&& A.Type == ArticleType.File
						&& A.CanonicalFileId != null
						&& SlugList.Contains(A.UrlSlug))
					.Select(A => new
					{
						A.UrlSlug,
						CanonicalFileId = A.CanonicalFileId!.Value
					})
					.ToListAsync(CancellationToken);

				if (FileArticles.Count > 0)
				{
					List<Guid> CanonicalFileIds = [.. FileArticles.Select(A => A.CanonicalFileId).Distinct()];
					var CurrentFiles = await Context.FileRevisions
						.AsNoTracking()
						.Where(F => CanonicalFileIds.Contains(F.CanonicalFileId)
							&& F.IsCurrent == true
							&& F.DateDeleted == null)
						.Select(F => new
						{
							F.CanonicalFileId,
							F.Id,
							F.Filename,
							F.ImageWidthPixels,
							F.ImageHeightPixels
						})
						.ToListAsync(CancellationToken);

					Dictionary<Guid, (int Id, string Filename, int WidthPx, int HeightPx)> FilesByCanonicalId = [];
					foreach (var FileItem in CurrentFiles)
					{
						int WidthPx = FileItem.ImageWidthPixels is > 0 ? FileItem.ImageWidthPixels.Value : 1;
						int HeightPx = FileItem.ImageHeightPixels is > 0 ? FileItem.ImageHeightPixels.Value : 1;
						FilesByCanonicalId[FileItem.CanonicalFileId] = (FileItem.Id, FileItem.Filename, WidthPx, HeightPx);
					}

					foreach (var ArticleItem in FileArticles)
					{
						if (FilesByCanonicalId.TryGetValue(ArticleItem.CanonicalFileId, out (int Id, string Filename, int WidthPx, int HeightPx) FileData))
						{
							string ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, ArticleItem.CanonicalFileId, FileData.Id, FileData.Filename);
							ResolvedLogos[ArticleItem.UrlSlug] = new ResolvedLogoFile(ImageUrl, FileData.WidthPx, FileData.HeightPx);
						}
					}
				}
			}
		}

		string[] Lines = Source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
		StringBuilder Builder = new();
		bool PreviousWasInlineText = false;

		for (int i = 0; i < Lines.Length; i++)
		{
			string Line = Lines[i];
			string TrimmedLine = Line.Trim();

			// Check if the entire line is a single [[Fallback|#|slug]] token that resolves to a logo
			Match SingleTokenMatch = LogoTokenRegex().Match(TrimmedLine);
			if (SingleTokenMatch.Success && SingleTokenMatch.Index == 0 && SingleTokenMatch.Length == TrimmedLine.Length)
			{
				string FallbackText = SingleTokenMatch.Groups["fallback"].Value.Trim();
				string RawSlug = SingleTokenMatch.Groups["slug"].Value.Trim();
				string NormalizedSlug = string.IsNullOrWhiteSpace(RawSlug)
					? string.Empty
					: ArticleUrlHelper.NormalizeLookupSlug(RawSlug);

				if (ResolvedLogos.TryGetValue(NormalizedSlug, out ResolvedLogoFile? Logo))
				{
					Builder.Append(BuildLogoHtml(FallbackText, Logo));
					PreviousWasInlineText = false;
					continue;
				}
			}

			if (PreviousWasInlineText)
			{
				Builder.Append("<br />");
			}

			Builder.Append(RenderLineWithInlineTokens(Line, ResolvedLogos));
			PreviousWasInlineText = true;
		}

		return Builder.ToString();
	}

	/// <summary>
	/// Renders text with optional slash breaks and resolves inline <c>[[FallbackText|#|file-slug]]</c> tokens.
	/// </summary>
	/// <param name="Line">The raw line text.</param>
	/// <param name="ResolvedLogos">The dictionary of resolved logo files keyed by normalized slug.</param>
	/// <returns>Safe HTML with optional text breaks and any resolved logos or fallback text.</returns>
	private static string RenderLineWithInlineTokens(string Line, Dictionary<string, ResolvedLogoFile> ResolvedLogos)
	{
		MatchCollection Matches = LogoTokenRegex().Matches(Line);
		if (Matches.Count == 0)
		{
			return RenderText(Line);
		}

		StringBuilder LineBuilder = new();
		int LastIndex = 0;

		foreach (Match TokenMatch in Matches)
		{
			if (TokenMatch.Index > LastIndex)
			{
				LineBuilder.Append(RenderText(Line[LastIndex..TokenMatch.Index]));
			}

			string FallbackText = TokenMatch.Groups["fallback"].Value.Trim();
			string RawSlug = TokenMatch.Groups["slug"].Value.Trim();
			string NormalizedSlug = string.IsNullOrWhiteSpace(RawSlug)
				? string.Empty
				: ArticleUrlHelper.NormalizeLookupSlug(RawSlug);

			if (ResolvedLogos.TryGetValue(NormalizedSlug, out ResolvedLogoFile? Logo))
			{
				LineBuilder.Append(BuildLogoHtml(FallbackText, Logo));
			}
			else
			{
				LineBuilder.Append(RenderText(FallbackText));
			}

			LastIndex = TokenMatch.Index + TokenMatch.Length;
		}

		if (LastIndex < Line.Length)
		{
			LineBuilder.Append(RenderText(Line[LastIndex..]));
		}

		return LineBuilder.ToString();
	}

	/// <summary>
	/// Encodes authored text before adding optional breaks after slashes, keeping words intact.
	/// </summary>
	/// <param name="Text">Visible title text, excluding generated logo markup and attributes.</param>
	/// <returns>HTML-encoded text with a <c>&lt;wbr&gt;</c> after each slash.</returns>
	private static string RenderText(string Text) => WebUtility.HtmlEncode(Text).Replace("/", "/<wbr>", StringComparison.Ordinal);

	/// <summary>
	/// Builds the accessible HTML markup for a resolved title logo graphic.
	/// </summary>
	/// <param name="FallbackText">The accessible fallback name of the publication.</param>
	/// <param name="Logo">The resolved logo file metadata.</param>
	/// <returns>The HTML markup for the logo span.</returns>
	private static string BuildLogoHtml(string FallbackText, ResolvedLogoFile Logo)
	{
		string EncodedFallback = WebUtility.HtmlEncode(FallbackText);
		string SafeUrl = WebUtility.HtmlEncode(Logo.ImageUrl);
		return $"<span class=\"title-logo\"><span class=\"title-logo__graphic\" style=\"--logo-url: url('{SafeUrl}'); --logo-width: {Logo.WidthPx}; --logo-height: {Logo.HeightPx};\" aria-hidden=\"true\"></span><span class=\"visually-hidden\">{EncodedFallback} </span></span>";
	}

	/// <summary>
	/// Represents a resolved file revision for a display title logo.
	/// </summary>
	/// <param name="ImageUrl">The public URL to the SVG/image file.</param>
	/// <param name="WidthPx">The intrinsic width in pixels.</param>
	/// <param name="HeightPx">The intrinsic height in pixels.</param>
	private sealed record ResolvedLogoFile(string ImageUrl, int WidthPx, int HeightPx);
}
