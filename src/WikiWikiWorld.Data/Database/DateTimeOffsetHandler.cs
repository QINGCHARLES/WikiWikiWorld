using System.Globalization;

namespace WikiWikiWorld.Data.TypeHandlers;
public sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
	public override DateTimeOffset Parse(object Value)
	{
		if (Value is not string DateTimeString)
		{
			throw new InvalidOperationException($"Unexpected NULL or non-string DateTime value in database: {Value}");
		}

		// Allow both 6 and 7 digit precision
		string[] Formats =
		[
			"yyyy-MM-ddTHH:mm:ss.ffffffK", // 6-digit precision (SQLite default)
			"yyyy-MM-ddTHH:mm:ss.fffffffK" // 7-digit precision (for consistency)
		];

		if (DateTimeOffset.TryParseExact(DateTimeString, Formats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset ParsedDateTime))
		{
			return ParsedDateTime;
		}

		throw new InvalidOperationException($"Invalid DateTimeOffset value in database: {DateTimeString}");
	}


	public override void SetValue(IDbDataParameter Parameter, DateTimeOffset Value)
	{
		// Store DateTimeOffset as a UTC ISO 8601 string with explicit +00:00 offset and 7-digit precision
		Parameter.Value = Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffff+00:00");
	}
}
