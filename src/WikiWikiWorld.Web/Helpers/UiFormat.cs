using Microsoft.AspNetCore.Html;

namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Formatting helpers for translated UI text: encoded markup fragments to pass as placeholders to
/// <c>HL[...]</c>, and dates written in the page's language.
/// </summary>
public static class UiFormat
{
	/// <summary>
	/// Wraps text in <c>&lt;strong&gt;</c> for use as a translated sentence's placeholder.
	/// </summary>
	/// <param name="Text">The text; it is HTML-encoded.</param>
	/// <returns>The encoded fragment.</returns>
	public static IHtmlContent Strong(string? Text) =>
		new HtmlContentBuilder().AppendHtml("<strong>").Append(Text ?? string.Empty).AppendHtml("</strong>");

	/// <summary>
	/// Builds a link for use as a translated sentence's placeholder.
	/// </summary>
	/// <param name="Href">The link target; it is HTML-encoded as an attribute value.</param>
	/// <param name="Text">The link text; it is HTML-encoded.</param>
	/// <param name="Rel">An optional <c>rel</c> value.</param>
	/// <returns>The encoded fragment.</returns>
	public static IHtmlContent Link(string Href, string Text, string? Rel = null)
	{
		HtmlContentBuilder Builder = new();
		Builder.AppendHtml("<a href=\"").Append(Href).AppendHtml("\"");
		if (!string.IsNullOrEmpty(Rel))
			Builder.AppendHtml(" rel=\"").Append(Rel).AppendHtml("\"");
		return Builder.AppendHtml(">").Append(Text).AppendHtml("</a>");
	}

	/// <summary>
	/// Writes a date for display in the page's language.
	/// </summary>
	/// <param name="Value">The instant, shown as its own offset's local time, as before.</param>
	/// <param name="EnglishFormat">The custom format English pages have always used, such as <c>dd MMMM yyyy HH:mm</c>.</param>
	/// <param name="StandardFormat">
	/// The standard pattern for other languages: <c>f</c> (long date and short time) by default, or <c>D</c> for a date alone.
	/// </param>
	/// <returns>
	/// English output exactly as before (current formatting culture and <paramref name="EnglishFormat"/>); otherwise the
	/// UI culture's own pattern on the Gregorian calendar, such as "2026年3月5日木曜日 14:07".
	/// </returns>
	public static string Date(DateTimeOffset Value, string EnglishFormat, string StandardFormat = "f")
	{
		CultureInfo Ui = CultureInfo.CurrentUICulture;
		if (Ui.TwoLetterISOLanguageName is "en" or "iv")
			return Value.ToString(EnglishFormat, CultureInfo.CurrentCulture);

		return Value.ToString(StandardFormat, Gregorian(Ui));
	}

	/// <summary>
	/// Formats a value with one format for English pages and another for other languages.
	/// </summary>
	/// <param name="Value">The date or number to format.</param>
	/// <param name="EnglishFormat">The format English pages have always used.</param>
	/// <param name="EnglishProvider">The provider English pages have always used, such as <see cref="CultureInfo.InvariantCulture"/>.</param>
	/// <param name="OtherFormat">The format for other UI cultures, such as <c>d</c> or <c>dddd</c>.</param>
	/// <returns>English output exactly as before; otherwise the value in the UI culture, on the Gregorian calendar.</returns>
	public static string Format(IFormattable Value, string EnglishFormat, IFormatProvider EnglishProvider, string OtherFormat)
	{
		ArgumentNullException.ThrowIfNull(Value);
		CultureInfo Ui = CultureInfo.CurrentUICulture;
		return Ui.TwoLetterISOLanguageName is "en" or "iv"
			? Value.ToString(EnglishFormat, EnglishProvider)
			: Value.ToString(OtherFormat, Gregorian(Ui));
	}

	/// <summary>
	/// Gets a culture's name for display in the UI language: the English name on English pages, the language's own
	/// name elsewhere (so an Arabic page says "العربية", not "Arabic").
	/// </summary>
	/// <param name="CultureCode">The culture code.</param>
	/// <returns>The name, or the code itself when it is not a real culture.</returns>
	public static string LanguageName(string CultureCode)
	{
		if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is not ("en" or "iv"))
			return SiteCultureHelper.GetDisplayName(CultureCode);

		try
		{
			return CultureInfo.GetCultureInfo(CultureCode).EnglishName;
		}
		catch (CultureNotFoundException)
		{
			return CultureCode;
		}
	}

	/// <summary>
	/// Returns the culture itself when it already uses the Gregorian calendar, otherwise a copy switched to it.
	/// </summary>
	/// <param name="Culture">The UI culture.</param>
	/// <returns>A culture whose date format uses the Gregorian calendar.</returns>
	private static CultureInfo Gregorian(CultureInfo Culture)
	{
		if (Culture.DateTimeFormat.Calendar is GregorianCalendar)
			return Culture;

		CultureInfo Copy = (CultureInfo)Culture.Clone();
		Copy.DateTimeFormat.Calendar = new GregorianCalendar();
		return Copy;
	}
}
