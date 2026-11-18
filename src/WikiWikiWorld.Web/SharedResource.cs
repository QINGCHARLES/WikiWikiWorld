using System.Resources;

namespace WikiWikiWorld.Web;

/// <summary>
/// Marker type for the site's shared UI text resources (<c>Resources/SharedResource.*.resx</c>), and a static
/// accessor for code that cannot take an injected localizer, such as Markdig extensions and static helpers.
/// </summary>
/// <remarks>
/// <para>
/// Keys are the English text itself, so English needs no resource file and a missing translation falls back to
/// English. Views use the injected <c>L</c> (<see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/>)
/// and <c>HL</c> (<see cref="Microsoft.AspNetCore.Mvc.Localization.IHtmlLocalizer{T}"/>) over the same resources.
/// </para>
/// <para>
/// Synthetic keys that are not English sentences (plural forms such as <c>TimeAgo.Minute|few</c>) have English
/// values in the neutral <c>SharedResource.resx</c>. Every lookup uses <see cref="CultureInfo.CurrentUICulture"/>,
/// which <see cref="Infrastructure.UiCultureMiddleware"/> sets from the request's site culture.
/// </para>
/// </remarks>
public sealed class SharedResource
{
	/// <summary>
	/// Reads the compiled shared resources; the same manifest the injected localizers use.
	/// </summary>
	private static readonly ResourceManager Resources = new("WikiWikiWorld.Web.Resources.SharedResource", typeof(SharedResource).Assembly);

	/// <summary>
	/// Prevents instances; the type exists to name the resource set.
	/// </summary>
	private SharedResource()
	{
	}

	/// <summary>
	/// Gets the current UI culture's text for a key.
	/// </summary>
	/// <param name="Key">The English text, or a synthetic key with an English neutral value.</param>
	/// <returns>The translation, or <paramref name="Key"/> itself when no resource defines it.</returns>
	public static string Text(string Key) => Resources.GetString(Key, CultureInfo.CurrentUICulture) ?? Key;

	/// <summary>
	/// Gets the current UI culture's text for a key and fills its numbered placeholders.
	/// </summary>
	/// <param name="Key">The English format string, such as <c>Updated {0}</c>.</param>
	/// <param name="Arguments">The placeholder values, formatted with the current (formatting) culture.</param>
	/// <returns>The formatted translation, or the formatted key when no resource defines it.</returns>
	public static string Format(string Key, params object?[] Arguments) =>
		string.Format(CultureInfo.CurrentCulture, Text(Key), Arguments);

	/// <summary>
	/// Gets the current UI culture's text for a key as HTML and fills its placeholders with HTML fragments, for
	/// renderers that write markup directly (Markdig extensions).
	/// </summary>
	/// <param name="Key">The English format string; its text is HTML-encoded, so a translation cannot inject markup.</param>
	/// <param name="HtmlArguments">Already-encoded HTML for each placeholder, such as a link the caller built.</param>
	/// <returns>The encoded sentence with the fragments inserted.</returns>
	public static string FormatHtml(string Key, params string[] HtmlArguments) =>
		string.Format(CultureInfo.CurrentCulture, System.Net.WebUtility.HtmlEncode(Text(Key)), HtmlArguments);

	/// <summary>
	/// Gets the plural form of a counted phrase for the current UI culture and fills in the count.
	/// </summary>
	/// <param name="BaseKey">The key stem; each form is stored as <c>{BaseKey}|{category}</c>.</param>
	/// <param name="Count">The count that selects the form and replaces <c>{0}</c>.</param>
	/// <returns>The formatted phrase. A missing category falls back to the <c>other</c> form.</returns>
	public static string Plural(string BaseKey, int Count)
	{
		string Category = PluralCategory(CultureInfo.CurrentUICulture, Count);
		string? Pattern = Resources.GetString($"{BaseKey}|{Category}", CultureInfo.CurrentUICulture)
			?? Resources.GetString($"{BaseKey}|other", CultureInfo.CurrentUICulture)
			?? BaseKey;
		return string.Format(CultureInfo.CurrentCulture, Pattern, Count);
	}

	/// <summary>
	/// Selects the CLDR cardinal plural category for a whole-number count.
	/// </summary>
	/// <param name="Culture">The UI culture whose rules apply; its neutral language is used.</param>
	/// <param name="Count">The non-negative count.</param>
	/// <returns>
	/// One of <c>zero</c>, <c>one</c>, <c>two</c>, <c>few</c>, <c>many</c> or <c>other</c>. Arabic uses all six
	/// (n%100 in 3–10 is few, in 11–99 many); Japanese has only <c>other</c>; every other language, including
	/// English, uses <c>one</c> for 1 and <c>other</c> otherwise.
	/// </returns>
	public static string PluralCategory(CultureInfo Culture, int Count)
	{
		ArgumentNullException.ThrowIfNull(Culture);

		string Language = Culture.TwoLetterISOLanguageName;
		int Hundreds = Count % 100;

		return Language switch
		{
			"ar" => Count switch
			{
				0 => "zero",
				1 => "one",
				2 => "two",
				_ when Hundreds is >= 3 and <= 10 => "few",
				_ when Hundreds is >= 11 and <= 99 => "many",
				_ => "other"
			},
			"ja" => "other",
			_ => Count == 1 ? "one" : "other"
		};
	}
}
