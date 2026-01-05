namespace WikiWikiWorld.Web.Helpers
{
	/// <summary>
	/// Helper class for calculating relative time strings.
	/// </summary>
	public static class TimeAgo
	{
		/// <summary>
		/// Calculates a detailed human-readable time difference string between two DateTimeOffset values.
		/// Includes multi-unit precision and appropriate qualifiers based on progress toward the next unit.
		/// </summary>
		/// <param name="CurrentTime">The current time (typically DateTimeOffset.Now)</param>
		/// <param name="UpdateTime">The time when the update occurred</param>
		/// <returns>A string in the format "last updated X unit, Y unit ago" or with qualifiers</returns>
		public static string GetDetailedTimeAgoString(DateTimeOffset CurrentTime, DateTimeOffset UpdateTime)
		{
			// Ensure CurrentTime is later than UpdateTime
			ArgumentOutOfRangeException.ThrowIfLessThan(CurrentTime, UpdateTime, nameof(CurrentTime));

			TimeSpan Difference = CurrentTime - UpdateTime;

			// If less than a minute, return a simple string
			if (Difference.TotalMinutes < 1)
			{
				return "Updated less than a minute ago";
			}

			// Calculate all time units
			TimeUnit[] TimeUnits = CalculateTimeUnits(CurrentTime, UpdateTime, Difference);

			// Find primary unit (largest non-zero unit)
			int PrimaryUnitIndex = Array.FindIndex(TimeUnits, u => u.Value > 0);

			if (PrimaryUnitIndex == -1)
			{
				return "Updated less than a minute ago";
			}

			TimeUnit PrimaryUnit = TimeUnits[PrimaryUnitIndex];

			// Find secondary unit (next largest non-zero unit)
			int SecondaryUnitIndex = -1;
			for (int i = PrimaryUnitIndex + 1; i < TimeUnits.Length; i++)
			{
				if (TimeUnits[i].Value > 0)
				{
					// Skip seconds if primary unit is minutes (per requirements)
					if (!(PrimaryUnit.Name == "minute" && TimeUnits[i].Name == "second"))
					{
						SecondaryUnitIndex = i;
						break;
					}
				}
			}

			// Calculate percentage toward the next primary unit
			double PercentToNext = 0;
			if (SecondaryUnitIndex != -1 && PrimaryUnit.MaxValue > 0)
			{
				TimeUnit SecondaryUnit = TimeUnits[SecondaryUnitIndex];
				PercentToNext = SecondaryUnit.Value / PrimaryUnit.MaxValue;
			}

			// Format the string based on the percentage
			return PercentToNext switch
			{
				>= 0.75 => FormatAlmostNextUnit(PrimaryUnit),
				>= 0.25 => FormatOverCurrentUnit(PrimaryUnit),
				> 0 => FormatJustOverCurrentUnit(PrimaryUnit),
				_ when SecondaryUnitIndex != -1 => FormatMultiUnitPrecision(PrimaryUnit, TimeUnits[SecondaryUnitIndex]),
				_ => FormatSingleUnit(PrimaryUnit)
			};
		}

		/// <summary>
		/// Format "almost" next unit message
		/// </summary>
		private static string FormatAlmostNextUnit(TimeUnit PrimaryUnit)
		{
			int NextValue = PrimaryUnit.Value + 1;
			string ValueText = NextValue < 10 ? NumberToWord(NextValue) : NextValue.ToString();
			return $"Updated almost {ValueText} {PrimaryUnit.Name}{(NextValue != 1 ? "s" : "")} ago";
		}

		/// <summary>
		/// Format "over" current unit message
		/// </summary>
		private static string FormatOverCurrentUnit(TimeUnit PrimaryUnit)
		{
			string ValueText = PrimaryUnit.Value < 10 ? NumberToWord(PrimaryUnit.Value) : PrimaryUnit.Value.ToString();
			return $"Updated over {ValueText} {PrimaryUnit.Name}{(PrimaryUnit.Value != 1 ? "s" : "")} ago";
		}

		/// <summary>
		/// Format "just over" current unit message
		/// </summary>
		private static string FormatJustOverCurrentUnit(TimeUnit PrimaryUnit)
		{
			string ValueText = PrimaryUnit.Value < 10 ? NumberToWord(PrimaryUnit.Value) : PrimaryUnit.Value.ToString();
			return $"Updated just over {ValueText} {PrimaryUnit.Name}{(PrimaryUnit.Value != 1 ? "s" : "")} ago";
		}

		/// <summary>
		/// Format multi-unit precision message
		/// </summary>
		private static string FormatMultiUnitPrecision(TimeUnit PrimaryUnit, TimeUnit SecondaryUnit)
		{
			string PrimaryValueText = PrimaryUnit.Value < 10 ? NumberToWord(PrimaryUnit.Value) : PrimaryUnit.Value.ToString();
			string SecondaryValueText = SecondaryUnit.Value < 10 ? NumberToWord(SecondaryUnit.Value) : SecondaryUnit.Value.ToString();
			return $"Updated {PrimaryValueText} {PrimaryUnit.Name}{(PrimaryUnit.Value != 1 ? "s" : "")}, {SecondaryValueText} {SecondaryUnit.Name}{(SecondaryUnit.Value != 1 ? "s" : "")} ago";
		}

		/// <summary>
		/// Format single unit message
		/// </summary>
		private static string FormatSingleUnit(TimeUnit PrimaryUnit)
		{
			string ValueText = PrimaryUnit.Value < 10 ? NumberToWord(PrimaryUnit.Value) : PrimaryUnit.Value.ToString();
			return $"Updated {ValueText} {PrimaryUnit.Name}{(PrimaryUnit.Value != 1 ? "s" : "")} ago";
		}

		/// <summary>
		/// Represents a time unit with its name, value, and maximum value
		/// </summary>
		private readonly record struct TimeUnit(string Name, int Value, double MaxValue);

		/// <summary>
		/// Calculates all time units from a time difference
		/// </summary>
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
		/// Calculates the number of months between two dates, accounting for partial months
		/// </summary>
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
		/// Converts a number to its word representation for values less than 10
		/// </summary>
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
			_ => Number.ToString()
		};
	}
}
