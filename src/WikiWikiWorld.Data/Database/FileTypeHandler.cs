namespace WikiWikiWorld.Data.TypeHandlers;

public sealed class FileTypeHandler : SqlMapper.TypeHandler<FileType>
{
	public override FileType Parse(object Value)
	{
		if (Value is not string FileTypeString)
		{
			throw new InvalidOperationException($"Unexpected NULL or non-string FileType value in database: {Value}");
		}

		if (Enum.TryParse<FileType>(FileTypeString, ignoreCase: true, out FileType ParsedType))
		{
			return ParsedType;
		}

		throw new InvalidOperationException($"Invalid FileType value in database: {FileTypeString}");
	}

	public override void SetValue(IDbDataParameter Parameter, FileType Value)
	{
		Parameter.Value = Value.ToString().ToUpperInvariant();
	}
}
