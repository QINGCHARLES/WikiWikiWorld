namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Small text formatters used by the profile pages.
/// </summary>
public static class ProfileFormatting
{
	/// <summary>
	/// Roman numeral symbols paired with their values, largest first, including the subtractive pairs.
	/// </summary>
	private static readonly (int Value, string Symbol)[] RomanSymbols =
	[
		(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
		(100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
		(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
	];

	/// <summary>
	/// Formats a positive number as an upper-case Roman numeral, as printed on a magazine masthead volume.
	/// </summary>
	/// <param name="Number">A number from 1 to 3999.</param>
	/// <returns>The Roman numeral, such as <c>VII</c> for 7.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Number"/> is outside 1 to 3999.</exception>
	public static string ToRomanNumeral(int Number)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Number);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(Number, 3999);

		StringBuilder Builder = new();
		foreach ((int Value, string Symbol) in RomanSymbols)
		{
			while (Number >= Value)
			{
				Builder.Append(Symbol);
				Number -= Value;
			}
		}

		return Builder.ToString();
	}

	/// <summary>
	/// Describes how long ago an instant was, such as <c>just over 2 hours ago</c>, reusing the site's
	/// <see cref="TimeAgo"/> wording without its "Updated" prefix. Instants in the future (clock skew) read as now.
	/// </summary>
	/// <param name="When">The past instant.</param>
	/// <param name="Now">The current instant.</param>
	/// <returns>A lower-case relative phrase.</returns>
	public static string ToRelativeTime(DateTimeOffset When, DateTimeOffset Now)
	{
		const string Prefix = "Updated ";
		string Text = TimeAgo.GetDetailedTimeAgoString(Now, When > Now ? Now : When);
		return Text.StartsWith(Prefix, StringComparison.Ordinal) ? Text[Prefix.Length..] : Text;
	}
}
