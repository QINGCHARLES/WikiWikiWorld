namespace WikiWikiWorld.Web.Services;

/// <summary>
/// A year of daily edit counts laid out as Monday-first week columns, oldest week first, for the profile heatmap.
/// Days are UTC calendar days so every viewer sees the same grid.
/// </summary>
public sealed record class ProfileActivityCalendar
{
	/// <summary>
	/// The number of week columns: 52 full weeks before the current, possibly partial, week.
	/// </summary>
	public const int WeekCount = 53;

	/// <summary>
	/// Gets the week columns, oldest first. Every week has seven days, Monday to Sunday.
	/// </summary>
	public required IReadOnlyList<ProfileActivityWeek> Weeks { get; init; }

	/// <summary>
	/// Gets the first day shown, always a Monday.
	/// </summary>
	public required DateOnly FirstDay { get; init; }

	/// <summary>
	/// Gets the last day that can hold activity (today in UTC); later days in the final week are marked as future.
	/// </summary>
	public required DateOnly LastDay { get; init; }

	/// <summary>
	/// Gets the number of edits inside the calendar window.
	/// </summary>
	public required int TotalCount { get; init; }

	/// <summary>
	/// Gets the number of days inside the window with at least one edit.
	/// </summary>
	public required int ActiveDayCount { get; init; }

	/// <summary>
	/// Gets the longest run of consecutive active days inside the window.
	/// </summary>
	public required int LongestStreakDays { get; init; }

	/// <summary>
	/// Gets the run of consecutive active days ending today, or ending yesterday when today has no edits yet.
	/// </summary>
	public required int CurrentStreakDays { get; init; }

	/// <summary>
	/// Returns the Monday that starts the calendar window ending in the week of <paramref name="Today"/>.
	/// </summary>
	/// <param name="Today">The current UTC date.</param>
	/// <returns>The first day of the oldest week column.</returns>
	public static DateOnly GetFirstDay(DateOnly Today)
	{
		int DaysSinceMonday = ((int)Today.DayOfWeek + 6) % 7;
		return Today.AddDays(-DaysSinceMonday - ((WeekCount - 1) * 7));
	}

	/// <summary>
	/// Maps a day's edit count to one of five intensity levels. Fixed thresholds keep a level meaning the same thing
	/// on every profile, unlike quantiles, which would paint a single edit at full strength for a quiet user.
	/// </summary>
	/// <param name="Count">The number of edits on the day.</param>
	/// <returns>0 for no edits, 1 for one, 2 for two or three, 3 for four to seven, and 4 for eight or more.</returns>
	public static int GetLevel(int Count) => Count switch
	{
		<= 0 => 0,
		1 => 1,
		<= 3 => 2,
		<= 7 => 3,
		_ => 4
	};

	/// <summary>
	/// Buckets edit timestamps into UTC days and lays them out as week columns with streak totals.
	/// </summary>
	/// <param name="Timestamps">Edit timestamps; values outside the window, including future ones, are ignored.</param>
	/// <param name="Today">The current UTC date, which ends the window.</param>
	/// <returns>The calendar for the 53 weeks ending in the week of <paramref name="Today"/>.</returns>
	public static ProfileActivityCalendar Build(IEnumerable<DateTimeOffset> Timestamps, DateOnly Today)
	{
		ArgumentNullException.ThrowIfNull(Timestamps);

		DateOnly FirstDay = GetFirstDay(Today);
		Dictionary<DateOnly, int> Counts = [];
		foreach (DateTimeOffset Timestamp in Timestamps)
		{
			DateOnly Day = DateOnly.FromDateTime(Timestamp.UtcDateTime);
			if (Day < FirstDay || Day > Today)
			{
				continue;
			}

			Counts[Day] = Counts.GetValueOrDefault(Day) + 1;
		}

		List<ProfileActivityWeek> Weeks = new(WeekCount);
		for (int w = 0; w < WeekCount; w++)
		{
			DateOnly WeekStart = FirstDay.AddDays(w * 7);
			ProfileActivityDay[] Days = new ProfileActivityDay[7];
			DateOnly? MonthStart = null;
			for (int d = 0; d < 7; d++)
			{
				DateOnly Day = WeekStart.AddDays(d);
				int Count = Counts.GetValueOrDefault(Day);
				Days[d] = new ProfileActivityDay(Day, Count, GetLevel(Count), Day > Today);
				if (Day.Day == 1)
				{
					MonthStart = Day;
				}
			}

			Weeks.Add(new ProfileActivityWeek(Days, MonthStart));
		}

		int LongestStreak = 0;
		int RunningStreak = 0;
		for (DateOnly Day = FirstDay; Day <= Today; Day = Day.AddDays(1))
		{
			RunningStreak = Counts.ContainsKey(Day) ? RunningStreak + 1 : 0;
			LongestStreak = Math.Max(LongestStreak, RunningStreak);
		}

		int CurrentStreak = 0;
		DateOnly StreakDay = Counts.ContainsKey(Today) ? Today : Today.AddDays(-1);
		while (StreakDay >= FirstDay && Counts.ContainsKey(StreakDay))
		{
			CurrentStreak++;
			StreakDay = StreakDay.AddDays(-1);
		}

		return new ProfileActivityCalendar
		{
			Weeks = Weeks,
			FirstDay = FirstDay,
			LastDay = Today,
			TotalCount = Counts.Values.Sum(),
			ActiveDayCount = Counts.Count,
			LongestStreakDays = LongestStreak,
			CurrentStreakDays = CurrentStreak
		};
	}
}

/// <summary>
/// One Monday-to-Sunday column of the activity calendar.
/// </summary>
/// <param name="Days">The seven days of the week, Monday first.</param>
/// <param name="MonthStart">The first day of a month that falls in this week, used to label the column; otherwise <see langword="null"/>.</param>
public sealed record class ProfileActivityWeek(IReadOnlyList<ProfileActivityDay> Days, DateOnly? MonthStart);

/// <summary>
/// One cell of the activity calendar.
/// </summary>
/// <param name="Date">The UTC calendar day.</param>
/// <param name="Count">The number of edits saved that day.</param>
/// <param name="Level">The intensity level from <see cref="ProfileActivityCalendar.GetLevel"/>.</param>
/// <param name="IsFuture">Whether the day is after today and so cannot hold activity yet.</param>
public readonly record struct ProfileActivityDay(DateOnly Date, int Count, int Level, bool IsFuture);
