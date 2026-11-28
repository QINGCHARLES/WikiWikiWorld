using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Parses and renders article <c>DisplayTitle</c> values, supporting embedded SVG/image logo
/// tokens using <c>[[Fallback Text|#|file-slug]]</c>, unmasked multi-color logos with <c>|#|color</c>,
/// and separate light/dark logos separated by <c>|~|</c> or <c>,</c>.
/// </summary>
public static partial class DisplayTitleHelper
{
	/// <summary>
	/// Separators accepted between light and dark file slugs inside the logo-file segment of a token.
	/// </summary>
	private static readonly string[] ThemeSlugSeparators = ["|~|", ","];

	/// <summary>
	/// Matches <c>[[FallbackText|#|Options]]</c> tokens inside a display title.
	/// </summary>
	[GeneratedRegex(@"\[\[(?<fallback>[^\]|]+?)\|#\|(?<options>[^\]]+?)\]\]", RegexOptions.CultureInvariant)]
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
	/// Builds the inline custom properties that let the header shrink to fit its widest word. CSS keeps
	/// the normal heading size unless that word would be wider than the text column on a narrow screen.
	/// </summary>
	/// <remarks>
	/// The width is estimated from the characters, because the heading font is <c>system-ui</c> and differs
	/// by platform. Each character takes the wider of its Segoe UI and Arial/Helvetica advance at weight 800,
	/// uppercase, so the estimate errs towards a slightly smaller heading. A word is any run that the browser
	/// cannot break: spaces, slashes (which get <c>&lt;wbr&gt;</c>), dashes before non-digits and CJK characters
	/// all end one. Logo tokens are left out, because they normally render as a graphic sized separately.
	/// </remarks>
	/// <param name="DisplayTitle">The optional display title containing logo tokens or line breaks.</param>
	/// <param name="FallbackTitle">The canonical article title used when <paramref name="DisplayTitle"/> is empty.</param>
	/// <returns>
	/// A <c>style</c> attribute value such as <c>--title-word-em: 11.57; --title-word-chars: 15</c>,
	/// or <see langword="null"/> when the title has no text words.
	/// </returns>
	public static string? BuildFitStyle(string? DisplayTitle, string? FallbackTitle)
	{
		string Source = string.IsNullOrWhiteSpace(DisplayTitle)
			? (FallbackTitle ?? string.Empty)
			: DisplayTitle;

		string Text = LogoTokenRegex().Replace(Source, " ");
		double WidestWordEm = 0;
		int WidestWordCharacters = 0;
		double WordEm = 0;
		int WordCharacters = 0;

		for (int i = 0; i < Text.Length; i++)
		{
			char Character = Text[i];
			bool EndsBeforeCharacter = char.IsWhiteSpace(Character) || IsCjkCharacter(Character);

			if (!EndsBeforeCharacter)
			{
				WordEm += EstimateGlyphWidthEm(Character);
				WordCharacters++;
			}

			bool NextIsDigit = i + 1 < Text.Length && char.IsAsciiDigit(Text[i + 1]);
			bool EndsAfterCharacter = Character switch
			{
				'/' or '–' or '—' => true,
				'-' or '‐' => !NextIsDigit,
				_ => false
			};

			if (EndsBeforeCharacter || EndsAfterCharacter || i == Text.Length - 1)
			{
				if (WordEm > WidestWordEm)
				{
					WidestWordEm = WordEm;
					WidestWordCharacters = WordCharacters;
				}

				WordEm = 0;
				WordCharacters = 0;
			}
		}

		return WidestWordCharacters == 0
			? null
			: string.Create(CultureInfo.InvariantCulture, $"--title-word-em: {WidestWordEm:0.##}; --title-word-chars: {WidestWordCharacters}");
	}

	/// <summary>
	/// Estimates one character's uppercase advance in the heading font, taking the wider of Segoe UI
	/// (Windows) and Arial/Helvetica (close to Apple's and Android's heavy sans) at weight 800.
	/// Arabic letters are joined and narrow (about 0.34em in Segoe UI, 0.56em in Tahoma); other
	/// scripts such as Cyrillic and accented Latin share the general capital width.
	/// </summary>
	/// <param name="Character">The authored character; the heading shows it uppercase.</param>
	/// <returns>The estimated advance in em, before letter-spacing.</returns>
	private static double EstimateGlyphWidthEm(char Character) => char.ToUpperInvariant(Character) switch
	{
		'I' => 0.40,
		'.' or ',' or ':' or ';' or '!' or '\'' or '’' or '|' => 0.37,
		'-' or '‐' => 0.41,
		'?' => 0.61,
		>= '0' and <= '9' => 0.67,
		'E' or 'F' or 'J' or 'L' or 'P' or 'S' or 'T' or 'Z' => 0.72,
		'&' => 0.89,
		'M' => 0.99,
		'W' => 1.05,
		(>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿') => 0.60,
		_ => 0.83
	};

	/// <summary>
	/// Identifies characters the browser may break between without a space: kana, CJK ideographs,
	/// hangul, CJK punctuation and full-width forms.
	/// </summary>
	/// <param name="Character">The character to classify.</param>
	/// <returns><see langword="true"/> when the character never extends an unbreakable word.</returns>
	private static bool IsCjkCharacter(char Character) => Character switch
	{
		>= '　' and <= 'ヿ' => true,
		>= '㐀' and <= '䶿' => true,
		>= '一' and <= '鿿' => true,
		>= '가' and <= '힯' => true,
		>= '豈' and <= '﫿' => true,
		>= '＀' and <= '￯' => true,
		_ => false
	};

	/// <summary>
	/// Determines whether any valid logo token in <paramref name="DisplayTitle"/> references
	/// <paramref name="FileSlug"/> (either as its primary/light file or as its dark-mode file).
	/// </summary>
	/// <param name="DisplayTitle">The optional display title text to inspect.</param>
	/// <param name="FileSlug">The file article slug to match, with or without a <c>file:</c> prefix.</param>
	/// <returns><see langword="true"/> when a valid logo token references <paramref name="FileSlug"/>; otherwise, <see langword="false"/>.</returns>
	public static bool ReferencesFileSlug(string? DisplayTitle, string? FileSlug)
	{
		if (string.IsNullOrWhiteSpace(DisplayTitle) || string.IsNullOrWhiteSpace(FileSlug))
		{
			return false;
		}

		string TargetSlug = ArticleUrlHelper.NormalizeLookupSlug(FileSlug.Trim());
		if (string.IsNullOrWhiteSpace(TargetSlug))
		{
			return false;
		}

		foreach (Match TokenMatch in LogoTokenRegex().Matches(DisplayTitle))
		{
			if (!TryParseLogoOptions(TokenMatch.Groups["options"].Value, out ParsedLogoOptions Options))
			{
				continue;
			}

			if (string.Equals(Options.LightSlug, TargetSlug, StringComparison.OrdinalIgnoreCase)
				|| (Options.DarkSlug is not null && string.Equals(Options.DarkSlug, TargetSlug, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Renders a display title as safe HTML for an <c>&lt;h1&gt;</c> heading, resolving any
	/// <c>[[FallbackText|#|...]]</c> logo tokens against current file articles in the database.
	/// Unresolved or malformed tokens gracefully fall back to the HTML-encoded <c>FallbackText</c>.
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

		Dictionary<string, ResolvedLogoFile> ResolvedLogos = await LoadLogosAsync([Source], Context, SiteId, Culture, CancellationToken);
		return RenderHtml(DisplayTitle, FallbackTitle, ResolvedLogos);
	}

	/// <summary>Resolves all logo tokens for a set of titles with one batch per table.</summary>
	/// <param name="Titles">The authored titles whose logo tokens should be resolved.</param>
	/// <param name="Context">The database context used to resolve file slugs.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="Culture">The current culture code.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A lookup by normalized file slug, excluding missing and deleted files.</returns>
	internal static async Task<Dictionary<string, ResolvedLogoFile>> LoadLogosAsync(
		IEnumerable<string?> Titles, WikiWikiWorldDbContext Context, int SiteId, string Culture, CancellationToken CancellationToken)
	{
		HashSet<string> LookupSlugs = [with(StringComparer.OrdinalIgnoreCase)];
		foreach (string? Title in Titles)
		{
			if (string.IsNullOrWhiteSpace(Title)) continue;
			foreach (Match TokenMatch in LogoTokenRegex().Matches(Title))
			{
				if (TryParseLogoOptions(TokenMatch.Groups["options"].Value, out ParsedLogoOptions Options))
				{
					LookupSlugs.Add(Options.LightSlug);
					if (Options.DarkSlug is not null)
					{
						LookupSlugs.Add(Options.DarkSlug);
					}
				}
			}

		}

		Dictionary<string, ResolvedLogoFile> ResolvedLogos = new(StringComparer.OrdinalIgnoreCase);
		if (LookupSlugs.Count == 0) return ResolvedLogos;

		List<string> SlugList = [.. LookupSlugs];
		var FileArticles = await Context.ArticleRevisions
			.AsNoTracking()
			.Where(A => A.SiteId == SiteId && A.Culture == Culture && A.IsCurrent && A.DateDeleted == null
				&& A.Type == ArticleType.File && A.CanonicalFileId != null && SlugList.Contains(A.UrlSlug))
			.Select(A => new { A.UrlSlug, CanonicalFileId = A.CanonicalFileId!.Value })
			.ToListAsync(CancellationToken);
		if (FileArticles.Count == 0) return ResolvedLogos;

		List<Guid> CanonicalFileIds = [.. FileArticles.Select(A => A.CanonicalFileId).Distinct()];
		var CurrentFiles = await Context.FileRevisions
			.AsNoTracking()
			.Where(F => CanonicalFileIds.Contains(F.CanonicalFileId) && F.IsCurrent == true && F.DateDeleted == null)
			.Select(F => new { F.CanonicalFileId, F.Id, F.Filename, F.ImageWidthPixels, F.ImageHeightPixels })
			.ToListAsync(CancellationToken);

		Dictionary<Guid, ResolvedLogoFile> FilesByCanonicalId = [];
		foreach (var File in CurrentFiles)
		{
			int WidthPx = File.ImageWidthPixels is > 0 ? File.ImageWidthPixels.Value : 1;
			int HeightPx = File.ImageHeightPixels is > 0 ? File.ImageHeightPixels.Value : 1;
			string ImageUrl = ImageUrlHelper.BuildImageUrl(SiteId, File.CanonicalFileId, File.Id, File.Filename);
			FilesByCanonicalId[File.CanonicalFileId] = new ResolvedLogoFile(ImageUrl, WidthPx, HeightPx);
		}

		foreach (var Article in FileArticles)
		{
			if (FilesByCanonicalId.TryGetValue(Article.CanonicalFileId, out ResolvedLogoFile? File))
			{
				ResolvedLogos[Article.UrlSlug] = File;
			}
		}
		return ResolvedLogos;
	}

	/// <summary>Renders an authored title with logos from an existing lookup, encoding all visible text.</summary>
	/// <param name="DisplayTitle">The optional title containing logo tokens or line breaks.</param>
	/// <param name="FallbackTitle">The canonical title used when the display title is empty.</param>
	/// <param name="ResolvedLogos">The existing lookup of files referenced by logo tokens.</param>
	/// <returns>Safe title markup with fallback text for unresolved or malformed logo tokens.</returns>
	internal static string RenderHtml(string? DisplayTitle, string? FallbackTitle, Dictionary<string, ResolvedLogoFile> ResolvedLogos)
	{
		string Source = string.IsNullOrWhiteSpace(DisplayTitle) ? (FallbackTitle ?? string.Empty) : DisplayTitle;
		if (string.IsNullOrWhiteSpace(Source)) return string.Empty;

		string[] Lines = Source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
		StringBuilder Builder = new();
		bool PreviousWasInlineText = false;

		for (int i = 0; i < Lines.Length; i++)
		{
			string Line = Lines[i];
			string TrimmedLine = Line.Trim();

			// Check if the entire line is a single [[Fallback|#|...]] token that resolves to a logo
			Match SingleTokenMatch = LogoTokenRegex().Match(TrimmedLine);
			if (SingleTokenMatch.Success && SingleTokenMatch.Index == 0 && SingleTokenMatch.Length == TrimmedLine.Length)
			{
				string FallbackText = SingleTokenMatch.Groups["fallback"].Value.Trim();
				string RawOptions = SingleTokenMatch.Groups["options"].Value;

				if (TryBuildResolvedLogoHtml(FallbackText, RawOptions, ResolvedLogos, out string LogoHtml))
				{
					Builder.Append(LogoHtml);
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
	/// Renders text with optional slash breaks and resolves inline <c>[[FallbackText|#|...]]</c> tokens.
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
			string RawOptions = TokenMatch.Groups["options"].Value;

			if (TryBuildResolvedLogoHtml(FallbackText, RawOptions, ResolvedLogos, out string LogoHtml))
			{
				LineBuilder.Append(LogoHtml);
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
	/// Parses the <c>|#|</c>-delimited options portion of a logo token into normalized file slugs and a render mode.
	/// Single-slug tokens default to <see cref="LogoRenderMode.Mask"/>; light/dark pairs separated by <c>|~|</c>
	/// or <c>,</c> default to <see cref="LogoRenderMode.Color"/>.
	/// </summary>
	/// <param name="OptionsText">The raw token text following the first <c>|#|</c> separator.</param>
	/// <param name="Options">Receives the parsed slugs and render mode when valid.</param>
	/// <returns><see langword="true"/> when the options segment is syntactically valid; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseLogoOptions(string OptionsText, out ParsedLogoOptions Options)
	{
		Options = default;
		if (string.IsNullOrWhiteSpace(OptionsText))
		{
			return false;
		}

		string[] OptionSegments = OptionsText.Split("|#|", StringSplitOptions.None);
		string[] RawSlugs = OptionSegments[0].Split(ThemeSlugSeparators, StringSplitOptions.None);

		string LightSlug;
		string? DarkSlug;
		LogoRenderMode Mode;

		if (RawSlugs.Length == 1)
		{
			if (!TryNormalizeNonEmptySlug(RawSlugs[0], out LightSlug))
			{
				return false;
			}

			DarkSlug = null;
			Mode = LogoRenderMode.Mask;
		}
		else if (RawSlugs.Length == 2)
		{
			if (!TryNormalizeNonEmptySlug(RawSlugs[0], out LightSlug)
				|| !TryNormalizeNonEmptySlug(RawSlugs[1], out string ParsedDarkSlug))
			{
				return false;
			}

			DarkSlug = ParsedDarkSlug;
			Mode = LogoRenderMode.Color;
		}
		else
		{
			return false;
		}

		for (int i = 1; i < OptionSegments.Length; i++)
		{
			string Option = OptionSegments[i].Trim();
			if (Option.Equals("color", StringComparison.OrdinalIgnoreCase)
				|| Option.Equals("mode=color", StringComparison.OrdinalIgnoreCase))
			{
				Mode = LogoRenderMode.Color;
			}
			else if (Option.Equals("mask", StringComparison.OrdinalIgnoreCase)
				|| Option.Equals("mode=mask", StringComparison.OrdinalIgnoreCase))
			{
				Mode = LogoRenderMode.Mask;
			}
			else
			{
				return false;
			}
		}

		if (DarkSlug is not null && string.Equals(LightSlug, DarkSlug, StringComparison.OrdinalIgnoreCase))
		{
			DarkSlug = null;
		}

		Options = new ParsedLogoOptions(LightSlug, DarkSlug, Mode);
		return true;
	}

	/// <summary>
	/// Trims a raw file slug, strips any <c>file:</c> prefix, and ensures a non-empty lookup slug remains.
	/// </summary>
	/// <param name="RawSlug">The authored file slug segment.</param>
	/// <param name="NormalizedSlug">Receives the normalized database lookup slug.</param>
	/// <returns><see langword="true"/> when a non-empty slug remains after normalization; otherwise, <see langword="false"/>.</returns>
	private static bool TryNormalizeNonEmptySlug(string RawSlug, out string NormalizedSlug)
	{
		NormalizedSlug = string.Empty;
		string Trimmed = RawSlug.Trim();
		if (string.IsNullOrWhiteSpace(Trimmed))
		{
			return false;
		}

		string Candidate = ArticleUrlHelper.NormalizeLookupSlug(Trimmed);
		if (string.IsNullOrWhiteSpace(Candidate))
		{
			return false;
		}

		NormalizedSlug = Candidate;
		return true;
	}

	/// <summary>
	/// Resolves parsed logo file slugs against loaded <see cref="ResolvedLogoFile"/> entries and builds the HTML fragment.
	/// When a token specifies both light and dark slugs, both files must exist for the logo graphic to render.
	/// </summary>
	/// <param name="FallbackText">The accessible fallback name of the publication.</param>
	/// <param name="OptionsText">The raw token options segment following <c>FallbackText|#|</c>.</param>
	/// <param name="ResolvedLogos">The dictionary of resolved logo files keyed by normalized slug.</param>
	/// <param name="LogoHtml">Receives the rendered HTML when all referenced files resolve.</param>
	/// <returns><see langword="true"/> when the token is valid and its referenced files exist; otherwise, <see langword="false"/>.</returns>
	private static bool TryBuildResolvedLogoHtml(
		string FallbackText,
		string OptionsText,
		Dictionary<string, ResolvedLogoFile> ResolvedLogos,
		out string LogoHtml)
	{
		LogoHtml = string.Empty;
		if (!TryParseLogoOptions(OptionsText, out ParsedLogoOptions Options)
			|| !ResolvedLogos.TryGetValue(Options.LightSlug, out ResolvedLogoFile? LightLogo))
		{
			return false;
		}

		ResolvedLogoFile? DarkLogo = null;
		if (Options.DarkSlug is not null && !ResolvedLogos.TryGetValue(Options.DarkSlug, out DarkLogo))
		{
			return false;
		}

		LogoHtml = BuildLogoHtml(FallbackText, LightLogo, DarkLogo, Options.RenderMode);
		return true;
	}

	/// <summary>
	/// Builds the accessible HTML markup for a resolved single or light/dark title logo graphic.
	/// </summary>
	/// <param name="FallbackText">The accessible fallback name of the publication.</param>
	/// <param name="LightLogo">The primary or light-theme logo file metadata.</param>
	/// <param name="DarkLogo">The optional dark-theme logo file metadata.</param>
	/// <param name="Mode">Whether the logo renders as a monochrome CSS mask or an unmasked image.</param>
	/// <returns>The HTML markup for the logo span.</returns>
	private static string BuildLogoHtml(
		string FallbackText,
		ResolvedLogoFile LightLogo,
		ResolvedLogoFile? DarkLogo,
		LogoRenderMode Mode)
	{
		string EncodedFallback = WebUtility.HtmlEncode(FallbackText);
		if (DarkLogo is null)
		{
			string GraphicHtml = BuildSingleGraphicHtml(LightLogo, VariantClass: null, Mode);
			return $"<span class=\"title-logo\">{GraphicHtml}<span class=\"visually-hidden\">{EncodedFallback} </span></span>";
		}

		string LightHtml = BuildSingleGraphicHtml(LightLogo, "title-logo__light", Mode);
		string DarkHtml = BuildSingleGraphicHtml(DarkLogo, "title-logo__dark", Mode);
		return $"<span class=\"title-logo title-logo--themed\">{LightHtml}{DarkHtml}<span class=\"visually-hidden\">{EncodedFallback} </span></span>";
	}

	/// <summary>
	/// Builds the decorative element for one logo variant as either a CSS-masked <c>&lt;span&gt;</c> or an unmasked <c>&lt;img&gt;</c>.
	/// </summary>
	/// <param name="Logo">The resolved logo file metadata.</param>
	/// <param name="VariantClass">An optional theme variant CSS class (<c>title-logo__light</c> or <c>title-logo__dark</c>).</param>
	/// <param name="Mode">Whether to emit a CSS-masked span or an unmasked image element.</param>
	/// <returns>The HTML string for the decorative graphic element.</returns>
	private static string BuildSingleGraphicHtml(ResolvedLogoFile Logo, string? VariantClass, LogoRenderMode Mode)
	{
		string SafeUrl = WebUtility.HtmlEncode(Logo.ImageUrl);
		if (Mode == LogoRenderMode.Color)
		{
			string ImageClass = VariantClass is null ? "title-logo__img" : $"title-logo__img {VariantClass}";
			return $"<img class=\"{ImageClass}\" src=\"{SafeUrl}\" alt=\"\" width=\"{Logo.WidthPx}\" height=\"{Logo.HeightPx}\" style=\"--logo-width: {Logo.WidthPx}; --logo-height: {Logo.HeightPx};\" aria-hidden=\"true\" />";
		}

		string SpanClass = VariantClass is null ? "title-logo__graphic" : $"title-logo__graphic {VariantClass}";
		return $"<span class=\"{SpanClass}\" style=\"--logo-url: url('{SafeUrl}'); --logo-width: {Logo.WidthPx}; --logo-height: {Logo.HeightPx};\" aria-hidden=\"true\"></span>";
	}

	/// <summary>
	/// Specifies how a resolved title logo is painted in the heading.
	/// </summary>
	private enum LogoRenderMode
	{
		/// <summary>Paints a monochrome silhouette in <c>currentColor</c> using CSS <c>mask</c>.</summary>
		Mask,

		/// <summary>Renders an unmasked <c>&lt;img&gt;</c> preserving multi-color fills, shading, and SVG <c>color-scheme</c> styles.</summary>
		Color
	}

	/// <summary>
	/// Holds the normalized file slugs and rendering mode parsed from a logo token.
	/// </summary>
	/// <param name="LightSlug">The primary or light-mode file slug without any <c>file:</c> prefix.</param>
	/// <param name="DarkSlug">The optional distinct dark-mode file slug without any <c>file:</c> prefix.</param>
	/// <param name="RenderMode">The rendering mode for the logo element(s).</param>
	private readonly record struct ParsedLogoOptions(string LightSlug, string? DarkSlug, LogoRenderMode RenderMode);

	/// <summary>
	/// Represents a resolved file revision for a display title logo.
	/// </summary>
	/// <param name="ImageUrl">The public URL to the SVG/image file.</param>
	/// <param name="WidthPx">The intrinsic width in pixels.</param>
	/// <param name="HeightPx">The intrinsic height in pixels.</param>
	internal sealed record ResolvedLogoFile(string ImageUrl, int WidthPx, int HeightPx);
}
