namespace WikiWikiWorld.Web.Helpers;

/// <summary>
/// Helper class for parsing article revision dates.
/// </summary>
public static class RevisionDateParser
{
	/// <summary>
	/// Tries to parse a revision date string into a DateTimeOffset.
	/// </summary>
	/// <param name="Revision">The revision string (timestamp).</param>
	/// <param name="DateTime">The parsed DateTimeOffset.</param>
	/// <returns>True if parsing was successful; otherwise, false.</returns>
	public static bool TryParseRevisionDate(string Revision, out DateTimeOffset DateTime)
	{
		DateTime = default;

		if (Revision.Length == 14)
		{
			return DateTimeOffset.TryParseExact(
				Revision, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal, out DateTime);
		}
		else if (Revision.Length >= 15 && Revision.Length <= 21)
		{
			string NormalizedRevision = Revision.PadRight(21, '0'); // Ensure proper format

			return DateTimeOffset.TryParseExact(
				NormalizedRevision, "yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal, out DateTime);
		}

		return false;
	}

	/// <summary>
	/// Parses a revision timestamp into the span of instants it names, so a lookup can match a stored revision
	/// whose creation time has more precision than the timestamp. Fourteen digits (<c>yyyyMMddHHmmss</c>) name a whole
	/// second; each further digit, up to seven fractional digits, narrows the span tenfold, so the full 21-digit form
	/// names a single tick.
	/// </summary>
	/// <param name="Revision">The revision timestamp: 14 to 21 digits, read as UTC.</param>
	/// <param name="From">The first instant the timestamp covers (inclusive).</param>
	/// <param name="Until">The first instant after the span (exclusive).</param>
	/// <returns>True if the timestamp parsed; otherwise, false.</returns>
	public static bool TryParseRevisionRange(string Revision, out DateTimeOffset From, out DateTimeOffset Until)
	{
		Until = default;
		if (!TryParseRevisionDate(Revision, out From))
			return false;

		int FractionDigits = Revision.Length - 14;
		long SpanTicks = TimeSpan.TicksPerSecond;
		for (int i = 0; i < FractionDigits; i++)
			SpanTicks /= 10;

		Until = From.AddTicks(SpanTicks);
		return true;
	}
}
