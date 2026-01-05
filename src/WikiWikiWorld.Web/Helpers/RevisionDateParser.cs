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
}
