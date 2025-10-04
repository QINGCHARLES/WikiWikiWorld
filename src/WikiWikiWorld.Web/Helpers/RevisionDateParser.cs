using System.Globalization;

namespace WikiWikiWorld.Web.Helpers;

public static class RevisionDateParser
{
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
