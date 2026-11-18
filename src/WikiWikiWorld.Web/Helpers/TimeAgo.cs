namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Helper class for calculating relative time strings, in the request's UI language.
/// </summary>
/// <remarks>
/// English keeps its original wording, with number words below ten ("Updated just over two hours ago").
/// Other languages build the same phrase from <see cref="SharedResource"/> templates and plural-aware unit
/// phrases (<c>TimeAgo.Hour|two</c> and so on), because word order and plural forms differ.
/// </remarks>
public static class TimeAgo
{
	/// <summary>
	/// How the relative time is qualified, decided by how far the secondary unit is towards the next primary unit.
	/// </summary>
	private enum Qualifier
	{
		/// <summary>Under a minute.</summary>
		LessThanMinute,

		/// <summary>Exactly one unit count, with no secondary unit.</summary>
		Single,

		/// <summary>A primary and a secondary unit, such as "two hours, five minutes".</summary>
		Pair,

		/// <summary>Up to a quarter of the way to the next primary unit.</summary>
		JustOver,

		/// <summary>A quarter to three quarters of the way to the next primary unit.</summary>
		Over,

		/// <summary>At least three quarters of the way to the next primary unit.</summary>
		Almost
	}

	/// <summary>
	/// Calculates a detailed human-readable time difference string between two DateTimeOffset values.
	/// Includes multi-unit precision and appropriate qualifiers based on progress toward the next unit.
	/// </summary>
	/// <param name="CurrentTime">The current time (typically DateTimeOffset.Now)</param>
	/// <param name="UpdateTime">The time when the update occurred</param>
	/// <returns>
	/// A sentence such as "Updated just over two hours ago" in English, or its translation for the current UI culture.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="CurrentTime"/> is before <paramref name="UpdateTime"/>.</exception>
	public static string GetDetailedTimeAgoString(DateTimeOffset CurrentTime, DateTimeOffset UpdateTime)
	{
		(Qualifier Kind, TimeUnit Primary, TimeUnit Secondary) = Describe(CurrentTime, UpdateTime);
		return IsEnglish()
			? "Updated " + FormatEnglish(Kind, Primary, Secondary)
			: SharedResource.Format("Updated {0}", FormatLocalized(Kind, Primary, Secondary));
	}

	/// <summary>
	/// Describes how long ago an instant was without the "Updated" prefix, such as "just over two hours ago".
	/// </summary>
	/// <param name="CurrentTime">The current time.</param>
	/// <param name="UpdateTime">The earlier instant.</param>
	/// <returns>The relative phrase in the current UI culture; English is lower case.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="CurrentTime"/> is before <paramref name="UpdateTime"/>.</exception>
	public static string GetRelativeTime(DateTimeOffset CurrentTime, DateTimeOffset UpdateTime)
	{
		(Qualifier Kind, TimeUnit Primary, TimeUnit Secondary) = Describe(CurrentTime, UpdateTime);
		return IsEnglish() ? FormatEnglish(Kind, Primary, Secondary) : FormatLocalized(Kind, Primary, Secondary);
	}

	/// <summary>
	/// Determines whether the current UI culture uses the built-in English wording.
	/// </summary>
	/// <returns><see langword="true"/> for English and the invariant culture.</returns>
	private static bool IsEnglish() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "en" or "iv";

	/// <summary>
	/// Chooses the units and qualifier that describe the gap between two instants.
	/// </summary>
	/// <param name="CurrentTime">The current time.</param>
	/// <param name="UpdateTime">The earlier instant.</param>
	/// <returns>The qualifier, the largest non-zero unit and the next non-zero unit (default when there is none).</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="CurrentTime"/> is before <paramref name="UpdateTime"/>.</exception>
	private static (Qualifier Kind, TimeUnit Primary, TimeUnit Secondary) Describe(DateTimeOffset CurrentTime, DateTimeOffset UpdateTime)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(CurrentTime, UpdateTime, nameof(CurrentTime));

		TimeSpan Difference = CurrentTime - UpdateTime;
		if (Difference.TotalMinutes < 1)
			return (Qualifier.LessThanMinute, default, default);

		TimeUnit[] TimeUnits = CalculateTimeUnits(CurrentTime, UpdateTime, Difference);

		// Primary unit: the largest non-zero unit.
		int PrimaryUnitIndex = Array.FindIndex(TimeUnits, u => u.Value > 0);
		if (PrimaryUnitIndex == -1)
			return (Qualifier.LessThanMinute, default, default);

		TimeUnit PrimaryUnit = TimeUnits[PrimaryUnitIndex];

		// Secondary unit: the next largest non-zero unit, never seconds under minutes.
		int SecondaryUnitIndex = -1;
		for (int i = PrimaryUnitIndex + 1; i < TimeUnits.Length; i++)
		{
			if (TimeUnits[i].Value > 0 && !(PrimaryUnit.Name == "minute" && TimeUnits[i].Name == "second"))
			{
				SecondaryUnitIndex = i;
				break;
			}
		}

		TimeUnit SecondaryUnit = SecondaryUnitIndex == -1 ? default : TimeUnits[SecondaryUnitIndex];
		double PercentToNext = SecondaryUnitIndex != -1 && PrimaryUnit.MaxValue > 0
			? SecondaryUnit.Value / PrimaryUnit.MaxValue
			: 0;

		Qualifier Kind = PercentToNext switch
		{
			>= 0.75 => Qualifier.Almost,
			>= 0.25 => Qualifier.Over,
			> 0 => Qualifier.JustOver,
			_ when SecondaryUnitIndex != -1 => Qualifier.Pair,
			_ => Qualifier.Single
		};

		return (Kind, PrimaryUnit, SecondaryUnit);
	}

	/// <summary>
	/// Writes the original English phrase, without the "Updated" prefix.
	/// </summary>
	/// <param name="Kind">The qualifier.</param>
	/// <param name="Primary">The largest unit.</param>
	/// <param name="Secondary">The second unit, used only by <see cref="Qualifier.Pair"/>.</param>
	/// <returns>A phrase such as "almost three hours ago" or "two hours, five minutes ago".</returns>
	private static string FormatEnglish(Qualifier Kind, TimeUnit Primary, TimeUnit Secondary) => Kind switch
	{
		Qualifier.LessThanMinute => "less than a minute ago",
		Qualifier.Almost => $"almost {EnglishUnit(Primary with { Value = Primary.Value + 1 })} ago",
		Qualifier.Over => $"over {EnglishUnit(Primary)} ago",
		Qualifier.JustOver => $"just over {EnglishUnit(Primary)} ago",
		Qualifier.Pair => $"{EnglishUnit(Primary)}, {EnglishUnit(Secondary)} ago",
		Qualifier.Single => $"{EnglishUnit(Primary)} ago",
		_ => throw new ArgumentOutOfRangeException(nameof(Kind))
	};

	/// <summary>
	/// Writes an English count and unit, spelling out numbers below ten.
	/// </summary>
	/// <param name="Unit">The unit and its count.</param>
	/// <returns>Text such as "two hours" or "12 days".</returns>
	private static string EnglishUnit(TimeUnit Unit)
	{
		string ValueText = Unit.Value < 10 ? NumberToWord(Unit.Value) : Unit.Value.ToString(CultureInfo.InvariantCulture);
		return $"{ValueText} {Unit.Name}{(Unit.Value != 1 ? "s" : "")}";
	}

	/// <summary>
	/// Writes the phrase for a non-English UI culture from shared resources.
	/// </summary>
	/// <param name="Kind">The qualifier.</param>
	/// <param name="Primary">The largest unit.</param>
	/// <param name="Secondary">The second unit, used only by <see cref="Qualifier.Pair"/>.</param>
	/// <returns>The translated phrase, such as "منذ ساعتين".</returns>
	private static string FormatLocalized(Qualifier Kind, TimeUnit Primary, TimeUnit Secondary) => Kind switch
	{
		Qualifier.LessThanMinute => SharedResource.Text("less than a minute ago"),
		Qualifier.Almost => SharedResource.Format("almost {0} ago", LocalizedUnit(Primary with { Value = Primary.Value + 1 })),
		Qualifier.Over => SharedResource.Format("over {0} ago", LocalizedUnit(Primary)),
		Qualifier.JustOver => SharedResource.Format("just over {0} ago", LocalizedUnit(Primary)),
		Qualifier.Pair => SharedResource.Format("{0} ago", SharedResource.Format("{0}, {1}", LocalizedUnit(Primary), LocalizedUnit(Secondary))),
		Qualifier.Single => SharedResource.Format("{0} ago", LocalizedUnit(Primary)),
		_ => throw new ArgumentOutOfRangeException(nameof(Kind))
	};

	/// <summary>
	/// Writes a count and unit in the current UI culture with the right plural form.
	/// </summary>
	/// <param name="Unit">The unit and its count.</param>
	/// <returns>Text such as "3 ساعات" or "3時間".</returns>
	private static string LocalizedUnit(TimeUnit Unit) => Unit.Name switch
	{
		"year" => SharedResource.Plural("TimeAgo.Year", Unit.Value),
		"month" => SharedResource.Plural("TimeAgo.Month", Unit.Value),
		"week" => SharedResource.Plural("TimeAgo.Week", Unit.Value),
		"day" => SharedResource.Plural("TimeAgo.Day", Unit.Value),
		"hour" => SharedResource.Plural("TimeAgo.Hour", Unit.Value),
		"minute" => SharedResource.Plural("TimeAgo.Minute", Unit.Value),
		_ => SharedResource.Plural("TimeAgo.Second", Unit.Value)
	};

	/// <summary>
	/// Represents a time unit with its name, value, and maximum value
	/// </summary>
	private readonly record struct TimeUnit(string Name, int Value, double MaxValue);

	/// <summary>
	/// Calculates all time units from a time difference.
	/// </summary>
	/// <param name="CurrentTime">The current time.</param>
	/// <param name="UpdateTime">The time when the update occurred.</param>
	/// <param name="Difference">The time span difference.</param>
	/// <returns>An array of TimeUnit values for years, months, weeks, days, hours, minutes, and seconds.</returns>
	private static TimeUnit[] CalculateTimeUnits(DateTimeOffset CurrentTime, DateTimeOffset UpdateTime, TimeSpan Difference)
	{
		// Calculate accurate month difference
		int TotalMonthsDifference = CalculateMonthsDifference(CurrentTime.DateTime, UpdateTime.DateTime);
		int YearsDifference = TotalMonthsDifference / 12;
		int RemainingMonths = TotalMonthsDifference % 12;

		return [
			new("year", YearsDifference, 12.0),
			new("month", RemainingMonths, 30.0),
			new("week", (int)(Difference.TotalDays / 7) % 4, 7.0), // Approx 4 weeks in a month
			new("day", (int)Difference.TotalDays % 7, 24.0),
			new("hour", (int)Difference.TotalHours % 24, 60.0),
			new("minute", (int)Difference.TotalMinutes % 60, 60.0),
			new("second", (int)Difference.TotalSeconds % 60, 0.0)  // End of granularity
		];
	}

	/// <summary>
	/// Calculates the number of months between two dates, accounting for partial months.
	/// </summary>
	/// <param name="CurrentDateTime">The current date.</param>
	/// <param name="UpdateDateTime">The update date.</param>
	/// <returns>The number of complete months between the dates.</returns>
	private static int CalculateMonthsDifference(DateTime CurrentDateTime, DateTime UpdateDateTime)
	{
		int MonthsDifference = ((CurrentDateTime.Year - UpdateDateTime.Year) * 12) +
							  (CurrentDateTime.Month - UpdateDateTime.Month);

		// Adjust for day of month to handle partial months correctly
		if (CurrentDateTime.Day < UpdateDateTime.Day)
		{
			MonthsDifference--;
		}

		return Math.Max(0, MonthsDifference);
	}

	/// <summary>
	/// Converts a number to its English word for values less than 10.
	/// </summary>
	/// <param name="Number">The number to convert.</param>
	/// <returns>The word representation, or the number as a string for values 10 or greater.</returns>
	private static string NumberToWord(int Number) => Number switch
	{
		0 => "zero",
		1 => "one",
		2 => "two",
		3 => "three",
		4 => "four",
		5 => "five",
		6 => "six",
		7 => "seven",
		8 => "eight",
		9 => "nine",
		_ => Number.ToString(CultureInfo.InvariantCulture)
	};
}
