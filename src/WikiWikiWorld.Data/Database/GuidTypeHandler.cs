namespace WikiWikiWorld.Data.TypeHandlers;

public sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
	public override void SetValue(IDbDataParameter Parameter, Guid Value)
	{
		// Store GUIDs as lowercase "D" format
		Parameter.Value = Value.ToString("D").ToLowerInvariant();
	}

	public override Guid Parse(object Value)
	{
		// Convert database TEXT back into a GUID
		return Guid.TryParse(Value.ToString(), out Guid ParsedGuid)
			? ParsedGuid
			: throw new FormatException($"Invalid GUID format: {Value}");
	}
}

public sealed class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
{
	public override void SetValue(IDbDataParameter Parameter, Guid? Value)
	{
		// Store NULL as NULL, otherwise use lowercase "D" format
		Parameter.Value = Value.HasValue ? Value.Value.ToString("D").ToLowerInvariant() : DBNull.Value;
	}

	public override Guid? Parse(object Value)
	{
		// Convert database TEXT back into a nullable GUID
		return Value is null || Value is DBNull
			? (Guid?)null
			: Guid.TryParse(Value.ToString(), out Guid ParsedGuid)
				? ParsedGuid
				: throw new FormatException($"Invalid GUID format: {Value}");
	}
}
