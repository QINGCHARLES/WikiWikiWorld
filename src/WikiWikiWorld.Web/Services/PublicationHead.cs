using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>Metadata for a public Magazedia article, ready to render in the document head.</summary>
/// <param name="CanonicalUrl">The permanent URL of this language edition.</param>
/// <param name="Title">The plain encyclopedia article title.</param>
/// <param name="Description">The plain summary shared by search and link previews.</param>
/// <param name="SiteName">The encyclopedia name for previews.</param>
/// <param name="Image">The selected social image and its description.</param>
/// <param name="JsonLd">The HTML-safe serialized page and publication records.</param>
public sealed record PublicationHead(string CanonicalUrl, string Title, string? Description, string SiteName,
	PublicationShareImage Image, string JsonLd);

/// <summary>An image for use in a social link preview.</summary>
/// <param name="Url">An absolute HTTPS image URL on the wiki.</param>
/// <param name="AltText">A description of the image shown in the card.</param>
/// <param name="WidthPixels">The known width of a prepared social image.</param>
/// <param name="HeightPixels">The known height of a prepared social image.</param>
public sealed record PublicationShareImage(string Url, string AltText, int? WidthPixels = null, int? HeightPixels = null);

/// <summary>A canonical alternate language edition of a publication article.</summary>
/// <param name="Culture">The alternate article's language tag.</param>
/// <param name="Url">The alternate article's canonical URL.</param>
public sealed record PublicationAlternate(string Culture, string Url);

/// <summary>Builds publication metadata from parsed wiki facts and current public links.</summary>
/// <param name="Context">The current site's article context.</param>
/// <param name="PipelineFactory">The wiki Markdown parser.</param>
/// <param name="InfoboxSchemas">The site's declared infobox types and field schemas.</param>
/// <param name="LanguageService">The current article translation groups.</param>
/// <param name="FileStorage">The configured uploaded-image storage.</param>
public sealed partial class PublicationHeadBuilder(WikiWikiWorldDbContext Context, IMarkdownPipelineFactory PipelineFactory,
	InfoboxSchemaCatalog InfoboxSchemas, ArticleLanguageService LanguageService, IOptions<FileStorageOptions> FileStorage)
{
	/// <summary>Builds the head metadata for a current article with a publication subject.</summary>
	/// <param name="Revision">The current article revision.</param>
	/// <param name="Rendered">Facts and artwork from the existing article render.</param>
	/// <param name="Facts">The publication subject and its validated field candidates.</param>
	/// <param name="Site">The configured site serving the article.</param>
	/// <param name="Request">The request's resolved host, scheme and port.</param>
	/// <param name="CancellationToken">Cancels article and translation queries.</param>
	/// <returns>Metadata for the encyclopedia page and its subject.</returns>
	public async Task<PublicationHead> BuildAsync(ArticleRevision Revision, RenderedArticle Rendered, PublicationFacts Facts,
		SiteInfo Site, HttpRequest Request, CancellationToken CancellationToken)
	{
		string RootDomain = SiteCultureHelper.GetRootDomain(Site, Request.Host.Host);
		string Origin = SiteCultureHelper.BuildCultureOrigin(Site, RootDomain, Request.Scheme, Request.Host.Port, Revision.Culture);
		Uri Canonical = new(Origin + ArticleUrlHelper.BuildArticlePath(Revision));
		string Title = DisplayTitleHelper.ToPlainText(null, Revision.Title);
		string? Description = string.IsNullOrWhiteSpace(Rendered.MetaDescription)
			? Facts.Lead ?? DescribeIssue(Facts)
			: Rendered.MetaDescription.Trim();
		string? Parent = await ResolveParentAsync(Revision, Facts, Canonical, CancellationToken);
		PublicationShareImage Image = SelectImage(Canonical, Rendered, Facts, Title, Site.SiteName);
		string JsonLd = BuildJsonLd(Revision, Facts, Canonical, Title, Description, Site.SiteName, Parent);
		return new PublicationHead(Canonical.AbsoluteUri, Title, Description, Site.SiteName, Image, JsonLd);
	}

	/// <summary>Summarizes a sparse issue using the publication name and printed designation in the article.</summary>
	/// <param name="Facts">The issue's visible fields, which may use the article's language.</param>
	/// <returns>A short description when the article supplies both a publication and a designation.</returns>
	private static string? DescribeIssue(PublicationFacts Facts)
	{
		if (!Facts.IsIssue || Facts.Single("Publication")?.Value is not { } Publication) return null;
		string? Designation = Facts.Single("CoverDate")?.Value ?? Facts.Single("Issue")?.Value;
		return Designation is null ? null : $"{Publication} · {Designation}";
	}

	/// <summary>Resolves an issue's explicit parent link within the same site and culture.</summary>
	/// <param name="Revision">The current issue revision.</param>
	/// <param name="Facts">The source infobox facts.</param>
	/// <param name="Canonical">The issue's absolute canonical address.</param>
	/// <param name="CancellationToken">Cancels the parent query.</param>
	/// <returns>The parent periodical ID only if the destination describes a current publication.</returns>
	private async Task<string?> ResolveParentAsync(ArticleRevision Revision, PublicationFacts Facts, Uri Canonical,
		CancellationToken CancellationToken)
	{
		if (!Facts.IsIssue || Facts.Single("Publication")?.Link is not { } Link
			|| !Uri.TryCreate(Canonical, Link, out Uri? Target)
			|| Target.GetLeftPart(UriPartial.Authority) != Canonical.GetLeftPart(UriPartial.Authority)
			|| Target.UserInfo.Length > 0 || Target.Query.Length > 0 || Target.Fragment.Length > 0)
		{
			return null;
		}
		string Slug = Uri.UnescapeDataString(Target.AbsolutePath.Trim('/'));
		if (Slug.Length == 0 || Slug.Contains('/') || Slug.Contains('\\') || ArticleUrlHelper.HasFileNamespacePrefix(Slug))
			return null;

		var Parent = await Context.ArticleRevisions.AsNoTracking()
			.Where(Item => Item.SiteId == Revision.SiteId && Item.Culture == Revision.Culture && Item.IsCurrent
				&& Item.Type == ArticleType.Article && Item.UrlSlug == Slug && Item.CanonicalArticleId != Revision.CanonicalArticleId)
			.Select(Item => new { Item.UrlSlug, Item.Text }).FirstOrDefaultAsync(CancellationToken);
		if (Parent is null) return null;

		MarkdownDocument Document = Markdown.Parse(Parent.Text, PipelineFactory.GetPipeline());
		EyebrowExtension.Enrich(Document);
		InfoboxExtension.Enrich(Document, InfoboxSchemas, Revision.SiteId, Revision.Culture);
		return PublicationFacts.Extract(Document) is { IsIssue: false }
			? new Uri(Canonical, ArticleUrlHelper.BuildArticlePath(Parent.UrlSlug, ArticleType.Article) + "#periodical").AbsoluteUri : null;
	}

	/// <summary>Produces reciprocal links from the article's active translation group.</summary>
	/// <param name="Revision">The current article.</param>
	/// <param name="Site">The site's configured culture origins.</param>
	/// <param name="Request">The scheme and port to apply to those origins.</param>
	/// <param name="CancellationToken">Cancels the translation lookup.</param>
	/// <returns>Public language alternates, or an empty list where no translations qualify.</returns>
	public async Task<IReadOnlyList<PublicationAlternate>> GetAlternatesAsync(ArticleRevision Revision, SiteInfo Site,
		HttpRequest Request, CancellationToken CancellationToken)
	{
		if (Revision.Culture.StartsWith("xx-", StringComparison.OrdinalIgnoreCase)) return [];
		string RootDomain = SiteCultureHelper.GetRootDomain(Site, Request.Host.Host);
		IReadOnlyList<string> Cultures = SiteCultureHelper.GetCultures(Site, RootDomain);
		IReadOnlyList<ArticleLanguageEntry> Entries = await LanguageService.GetLanguagesAsync(Revision.CanonicalArticleId, CancellationToken);
		List<PublicationAlternate> Alternates = [.. Entries.Where(Entry => Entry.Type == ArticleType.Article
			&& Cultures.Contains(Entry.Culture, StringComparer.OrdinalIgnoreCase)
			&& !Entry.Culture.StartsWith("xx-", StringComparison.OrdinalIgnoreCase))
			.Select(Entry => new PublicationAlternate(Entry.Culture,
				new Uri(SiteCultureHelper.BuildCultureOrigin(Site, RootDomain, Request.Scheme, Request.Host.Port, Entry.Culture)
					+ ArticleUrlHelper.BuildArticlePath(Entry.UrlSlug, Entry.Type)).AbsoluteUri))];
		if (Alternates.Count == 0) return [];
		string Origin = SiteCultureHelper.BuildCultureOrigin(Site, RootDomain, Request.Scheme, Request.Host.Port, Revision.Culture);
		Alternates.Add(new PublicationAlternate(Revision.Culture, new Uri(Origin + ArticleUrlHelper.BuildArticlePath(Revision)).AbsoluteUri));
		return [.. Alternates.OrderBy(Alternate => Alternate.Culture, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>Chooses the first available JPEG rendition from the cover and article header.</summary>
	/// <param name="Canonical">The article's URL, used to expand asset paths.</param>
	/// <param name="Rendered">The resolved header and its descriptions.</param>
	/// <param name="Facts">The optional issue cover.</param>
	/// <param name="Title">The article title used in fallback descriptions.</param>
	/// <param name="SiteName">The branding displayed in the fallback image.</param>
	/// <returns>A crawler-accessible JPEG card.</returns>
	private PublicationShareImage SelectImage(Uri Canonical, RenderedArticle Rendered, PublicationFacts Facts, string Title, string SiteName)
	{
		string? HeaderAlt = string.IsNullOrWhiteSpace(Rendered.HeaderImageAltText)
			? Rendered.HeaderImageFileDescription : Rendered.HeaderImageAltText;
		return GetShareImage(Canonical, Facts.Cover?.ImageUrl, Facts.Cover?.AltText ?? Title, IsCover: true)
			?? GetShareImage(Canonical, Rendered.HeaderImage, HeaderAlt ?? Title, IsCover: false)
			?? new PublicationShareImage(new Uri(Canonical, "/images/magazedia-share-v1.jpg").AbsoluteUri, SiteName, 1200, 630);
	}

	/// <summary>Uses a revision-specific prepared image, with original JPEG covers as a secondary option.</summary>
	/// <param name="Canonical">The canonical article URL.</param>
	/// <param name="ImagePath">The app-generated revision image path.</param>
	/// <param name="AltText">The description to publish for that image.</param>
	/// <param name="IsCover">Whether the composition should show the whole cover.</param>
	/// <returns>A JPEG reference when the selected rendition is available.</returns>
	private PublicationShareImage? GetShareImage(Uri Canonical, string? ImagePath, string AltText, bool IsCover)
	{
		const string Prefix = "/sitefiles/";
		if (ImagePath is null || !ImagePath.StartsWith(Prefix, StringComparison.Ordinal)) return null;
		string Suffix = IsCover ? ".cover-social-v1.jpg" : ".header-social-v1.jpg";
		string Relative = Uri.UnescapeDataString(ImagePath[Prefix.Length..]);
		if (Relative.Split('/') is not [string Site, "images", string FileId, string Name]
			|| !int.TryParse(Site, CultureInfo.InvariantCulture, out _) || !Guid.TryParse(FileId, out _)
			|| Path.GetFileName(Name) != Name || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			return null;
		}
		string SourcePath = Path.Combine(FileStorage.Value.SiteFilesPath, Site, "images", FileId, Name);
		if (File.Exists(SourcePath + Suffix))
			return new PublicationShareImage(new Uri(Canonical, ImagePath + Suffix).AbsoluteUri, AltText, 1200, 630);
		return Path.GetExtension(Name).ToLowerInvariant() is ".jpg" or ".jpeg" && File.Exists(SourcePath)
			? new PublicationShareImage(new Uri(Canonical, ImagePath).AbsoluteUri, AltText) : null;
	}

	/// <summary>Serializes the wiki page and its primary subject as independent Schema.org records.</summary>
	/// <param name="Revision">The wiki revision, whose timestamp describes the page.</param>
	/// <param name="Facts">The bibliographic facts describing the subject.</param>
	/// <param name="Canonical">The canonical URL of the wiki page.</param>
	/// <param name="Title">The plain title displayed for the article and subject.</param>
	/// <param name="Description">The page's search summary.</param>
	/// <param name="SiteName">The publisher of the encyclopedia page.</param>
	/// <param name="Parent">The verified parent periodical ID, if known.</param>
	/// <returns>HTML-safe JSON-LD for a script data block.</returns>
	internal static string BuildJsonLd(ArticleRevision Revision, PublicationFacts Facts, Uri Canonical, string Title,
		string? Description, string SiteName, string? Parent)
	{
		string PageId = Canonical.AbsoluteUri;
		string SubjectId = PageId + (Facts.IsIssue ? "#issue" : "#periodical");
		Dictionary<string, object> Page = new()
		{
			["@type"] = "WebPage", ["@id"] = PageId, ["url"] = PageId, ["name"] = Title,
			["inLanguage"] = Revision.Culture, ["dateModified"] = Revision.DateCreated.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
			["mainEntity"] = new Dictionary<string, string> { ["@id"] = SubjectId },
			["publisher"] = new Dictionary<string, string> { ["@type"] = "Organization", ["name"] = SiteName }
		};
		if (Description is not null) Page["description"] = Description;
		Dictionary<string, object> Subject = new()
		{
			["@type"] = Facts.IsIssue ? "PublicationIssue" : "Periodical", ["@id"] = SubjectId, ["name"] = Title
		};
		if (Facts.IsIssue)
		{
			if (Facts.Cover is { } Cover) Subject["image"] = new Uri(Canonical, Cover.ImageUrl).AbsoluteUri;
			(string? Volume, string? Number) = GetIssueNumber(Facts);
			if (Number is not null) Subject["issueNumber"] = Number;
			if (Parent is not null)
			{
				Subject["isPartOf"] = Volume is not null
					? new Dictionary<string, object>
					{
						["@type"] = "PublicationVolume", ["volumeNumber"] = Volume,
						["isPartOf"] = new Dictionary<string, string> { ["@id"] = Parent }
					}
					: new Dictionary<string, string> { ["@id"] = Parent };
			}
			if (GetReleaseDate(Facts) is { } Date)
			{
				Subject["datePublished"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}
		}
		else
		{
			string[] Issns = [.. Facts.Fields.Where(Fact => Fact.Key.Equals("ISSN", StringComparison.OrdinalIgnoreCase))
				.Select(Fact => Issn().Match(Fact.Value)).Where(Match => Match.Success)
				.Select(Match => Match.Groups[1].Value.ToUpperInvariant()).Where(IsValidIssn).Distinct(StringComparer.Ordinal)];
			if (Issns.Length > 0) Subject["issn"] = Issns;
			string[] Languages = [.. Facts.Fields.Where(Fact => Fact.Key.Equals("Language", StringComparison.OrdinalIgnoreCase))
				.Select(Fact => FindLanguage(Fact.Value)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
			if (Languages.Length > 0) Subject["inLanguage"] = Languages;
			if (Uri.TryCreate(Facts.Single("Website")?.Link, UriKind.Absolute, out Uri? Website)
				&& Website.Scheme is "https" or "http" && Website.Host.Length > 0 && Website.UserInfo.Length == 0)
			{
				Subject["url"] = Website.AbsoluteUri;
			}
			if (Facts.Single("Publisher")?.Value is { } Publisher)
			{
				Publisher = PublisherYears().Replace(Publisher, string.Empty).Trim();
				if (Publisher.Length > 0 && !Publisher.Contains('('))
					Subject["publisher"] = new Dictionary<string, string> { ["@type"] = "Organization", ["name"] = Publisher };
			}
		}
		if (Facts.Single("Editor")?.Value is { } Editor && Editor.IndexOfAny(['(', ';', ',', '/']) < 0)
			Subject["editor"] = new Dictionary<string, string> { ["@type"] = "Person", ["name"] = Editor };

		return JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["@context"] = "https://schema.org", ["@graph"] = new[] { Page, Subject }
		});
	}

	/// <summary>Separates a printed volume and issue where the designation can be read unambiguously.</summary>
	/// <param name="Facts">The issue's visible facts.</param>
	/// <returns>The volume and issue numbers, with nulls for absent or ambiguous values.</returns>
	internal static (string? Volume, string? Number) GetIssueNumber(PublicationFacts Facts)
	{
		if (Facts.Single("Issue")?.Value is not { } Designation || IssueNumber().Match(Designation) is not { Success: true } Match)
			return (null, null);
		return (Match.Groups["volume"].Success ? Match.Groups["volume"].Value : null, Match.Groups["issue"].Value);
	}

	/// <summary>Gets an independently entered, precise release date shared by the documented formats.</summary>
	/// <param name="Facts">The issue's visible facts.</param>
	/// <returns>The full release date, or null for absent, imprecise or differing dates.</returns>
	internal static DateOnly? GetReleaseDate(PublicationFacts Facts)
	{
		string? Print = Facts.Single("PrintPublicationDate")?.Value;
		string? Digital = Facts.Single("ElectronicPublicationDate")?.Value;
		string? Release = Print ?? Digital;
		return (Print is null || Digital is null || Print == Digital)
			&& DateOnly.TryParseExact(Release, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly Date)
			? Date : null;
	}

	/// <summary>Converts a known language name or code without inferring the magazine language from the wiki.</summary>
	/// <param name="Value">The authored language name.</param>
	/// <returns>A recognized neutral language code or null.</returns>
	private static string? FindLanguage(string Value) =>
		CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(Culture => Culture.Name.Length > 0
			&& (Culture.Name.Equals(Value, StringComparison.OrdinalIgnoreCase)
				|| Culture.EnglishName.Equals(Value, StringComparison.OrdinalIgnoreCase)
				|| Culture.NativeName.Equals(Value, StringComparison.OrdinalIgnoreCase)))?.Name;

	/// <summary>Checks the modulo-eleven ISSN checksum, including check digit X.</summary>
	/// <param name="Value">An ISSN in canonical hyphenated form.</param>
	/// <returns>Whether the identifier passes the check-digit calculation.</returns>
	private static bool IsValidIssn(string Value)
	{
		string Digits = Value.Replace("-", string.Empty, StringComparison.Ordinal);
		int Sum = 0;
		for (int i = 0; i < Digits.Length; i++)
		{
			Sum += (Digits[i] == 'X' ? 10 : Digits[i] - '0') * (8 - i);
		}
		return Sum % 11 == 0;
	}

	/// <summary>Recognizes a printed issue identifier and its optional volume.</summary>
	/// <returns>A matcher for numeric, combined and explicitly labelled issue numbers.</returns>
	[GeneratedRegex(@"^(?:(?:vol(?:ume)?\.?\s+)(?<volume>[0-9]+)\s*(?:[,–-]\s*)?)?(?:(?:issue|no\.?|number|№|#)\s*)?(?<issue>[0-9]+(?:\s*[-–/]\s*[0-9]+)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex IssueNumber();

	/// <summary>Captures an ISSN without its optional medium label.</summary>
	/// <returns>A matcher for one hyphenated ISSN with an optional parenthetical qualifier.</returns>
	[GeneratedRegex(@"^([0-9]{4}-[0-9]{3}[0-9Xx])(?:\s+\([^()]+\))?$")]
	private static partial Regex Issn();

	/// <summary>Identifies a publisher's trailing range of active years.</summary>
	/// <returns>A matcher for a date suffix outside the publisher's name.</returns>
	[GeneratedRegex(@"\s+\([0-9]{4}(?:[–-](?:[0-9]{4}|present))?\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex PublisherYears();
}
