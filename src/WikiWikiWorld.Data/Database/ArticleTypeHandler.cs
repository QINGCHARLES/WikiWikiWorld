namespace WikiWikiWorld.Data.TypeHandlers;

public sealed class ArticleTypeHandler : SqlMapper.TypeHandler<ArticleType>
{
	public override ArticleType Parse(object value)
	{
		if (value is not string articleTypeString)
		{
			throw new InvalidOperationException($"Unexpected NULL or non-string ArticleType value in database: {value}");
		}

		if (Enum.TryParse<ArticleType>(articleTypeString, ignoreCase: true, out ArticleType parsedType))
		{
			return parsedType;
		}

		throw new InvalidOperationException($"Invalid ArticleType value in database: {articleTypeString}");
	}

	public override void SetValue(IDbDataParameter parameter, ArticleType value)
	{
		parameter.Value = value.ToString().ToUpperInvariant();
	}
}
