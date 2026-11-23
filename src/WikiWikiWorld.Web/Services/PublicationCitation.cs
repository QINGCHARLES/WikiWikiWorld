using System.Text.RegularExpressions;

namespace WikiWikiWorld.Web.Services;

/// <summary>A readable citation and a matching RIS import record for one bibliographic item.</summary>
/// <param name="Text">The citation ready to select or copy.</param>
/// <param name="Ris">The citation manager record with normalized field boundaries.</param>
public sealed partial record PublicationCitation(string Text, string Ris)
{
	/// <summary>Builds a magazine issue citation using the original printed designation when available.</summary>
	/// <param name="Head">The public page title and URL.</param>
	/// <param name="Facts">The issue and publication details from Markdown.</param>
	/// <returns>A readable magazine issue citation and a RIS magazine record.</returns>
	public static PublicationCitation ForIssue(PublicationHead Head, PublicationFacts Facts)
	{
		string? Publication = Facts.Single("Publication")?.Value;
		string? Designation = Facts.Single("Issue")?.Value;
		string? CoverDate = Facts.Single("CoverDate")?.Value;
		(string? Volume, string? Number) = PublicationHeadBuilder.GetIssueNumber(Facts);
		string? ReleaseDate = PublicationHeadBuilder.GetReleaseDate(Facts)?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		string Text = string.Join(" · ", new[] { Head.Title, Publication, Designation, CoverDate, Head.CanonicalUrl }
			.OfType<string>().Distinct(StringComparer.Ordinal));
		return new PublicationCitation(Text, BuildRis("MGZN", ("TI", Head.Title), ("JF", Publication), ("VL", Volume),
			("IS", Number), ("DA", ReleaseDate), ("N1", string.Join(" · ", new[] { Designation, CoverDate }.OfType<string>())),
			("UR", Head.CanonicalUrl)));
	}

	/// <summary>Builds a citation for a specific version of the Magazedia encyclopedia entry.</summary>
	/// <param name="Head">The display title and name of the wiki.</param>
	/// <param name="Revision">The revision the reader is citing.</param>
	/// <param name="Permalink">The URL that loads that revision.</param>
	/// <returns>A readable encyclopedia citation and a RIS webpage record.</returns>
	public static PublicationCitation ForPage(PublicationHead Head, ArticleRevision Revision, string Permalink)
	{
		DateTime Date = Revision.DateCreated.UtcDateTime;
		string Text = string.Join(" · ", Head.Title, Head.SiteName, Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Permalink);
		return new PublicationCitation(Text, BuildRis("WEB", ("TI", Head.Title), ("T2", Head.SiteName),
			("DA", Date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)), ("LA", Revision.Culture), ("UR", Permalink)));
	}

	/// <summary>Writes one RIS record using the standard tag prefix and explicit record terminator.</summary>
	/// <param name="Type">The RIS item type.</param>
	/// <param name="Fields">Supported field tags and their optional authored values.</param>
	/// <returns>One complete RIS record.</returns>
	private static string BuildRis(string Type, params (string Tag, string? Value)[] Fields)
	{
		StringBuilder Text = new();
		Text.Append("TY  - ").Append(Type).Append("\r\n");
		foreach ((string Tag, string? Value) in Fields)
		{
			if (string.IsNullOrWhiteSpace(Value)) continue;
			Text.Append(Tag).Append("  - ").Append(Whitespace().Replace(Value, " ").Trim()).Append("\r\n");
		}
		return Text.Append("ER  - \r\n").ToString();
	}

	/// <summary>Matches spacing that must stay inside a single RIS field line.</summary>
	/// <returns>A matcher for Unicode whitespace, including line endings.</returns>
	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();
}
