#nullable enable

using System.Linq;

namespace WikiWikiWorld.Data.Repositories;

public interface IArticleRevisionRepository
{
	Task<long> InsertAsync(
		Guid? CanonicalArticleId,
		int SiteId,
		string Culture,
		string Title,
		string? DisplayTitle,
		string UrlSlug,
		ArticleType Type,
		Guid? CanonicalFileId,
		string Text,
		string RevisionReason,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<ArticleRevision?> GetCurrentBySiteIdCultureAndUrlSlugAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		CancellationToken CancellationToken = default);

	Task<(ArticleRevision? Current, ArticleRevision? Specific)> GetRevisionBySiteIdCultureUrlSlugAndDateAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		DateTimeOffset DateCreated,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleRevision>> GetAllRevisionsBySiteIdCultureAndUrlSlugAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleRevision>> GetRecentRevisionsAsync(
		int SiteId,
		string Culture,
		int Limit = 100,
		CancellationToken CancellationToken = default);

	Task<bool> DeleteByCanonicalIdAsync(
		int SiteId,
		string Culture,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleAuthor>> GetRecentAuthorsForArticleAsync(
		Guid CanonicalArticleId,
		DateTimeOffset? MaxRevisionDate = null,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleSitemapEntry>> GetAllCurrentArticlesForSitemapAsync(
		int SiteId,
		string Culture,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleRevision>> GetLatestArticlesWithMagazineInfoboxAsync(
		int SiteId,
		string Culture,
		int Limit = 100,
		CancellationToken CancellationToken = default);
}

public sealed class ArticleRevisionRepository : IArticleRevisionRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public ArticleRevisionRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<long> InsertAsync(
		Guid? CanonicalArticleId,
		int SiteId,
		string Culture,
		string Title,
		string? DisplayTitle,
		string UrlSlug,
		ArticleType Type,
		Guid? CanonicalFileId,
		string Text,
		string RevisionReason,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			Guid EffectiveCanonicalArticleId = CanonicalArticleId ?? Guid.NewGuid();

			const string ResetCurrentSql = @"
UPDATE ArticleRevisions
SET IsCurrent = 0
WHERE CanonicalArticleId = @CanonicalArticleId
  AND IsCurrent = 1
  AND DateDeleted IS NULL;";

			CommandDefinition ResetCommand = new(
				ResetCurrentSql,
				new { CanonicalArticleId = EffectiveCanonicalArticleId },
				transaction: Transaction,
				cancellationToken: CancellationToken);

			await Connection.ExecuteAsync(ResetCommand).ConfigureAwait(false);

			const string InsertSql = @"
INSERT INTO ArticleRevisions (
	CanonicalArticleId, SiteId, Culture, Title, DisplayTitle,
	UrlSlug, IsCurrent, Type, Text, CanonicalFileId,
	RevisionReason, CreatedByUserId, DateCreated, DateDeleted
) VALUES (
	@CanonicalArticleId, @SiteId, @Culture, @Title, @DisplayTitle,
	@UrlSlug, 1, @Type, @Text, @CanonicalFileId,
	@RevisionReason, @CreatedByUserId, @DateCreated, NULL
);
SELECT last_insert_rowid();";

			CommandDefinition InsertCommand = new(
				InsertSql,
				new
				{
					CanonicalArticleId = EffectiveCanonicalArticleId,
					SiteId,
					Culture,
					Title,
					DisplayTitle,
					UrlSlug,
					Type = Type.ToString().ToUpperInvariant(),
					Text,
					CanonicalFileId,
					RevisionReason,
					CreatedByUserId,
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			return await Connection.ExecuteScalarAsync<long>(InsertCommand).ConfigureAwait(false);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<ArticleRevision?> GetCurrentBySiteIdCultureAndUrlSlugAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND UrlSlug = @UrlSlug
  AND IsCurrent = 1
  AND DateDeleted IS NULL
LIMIT 1;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, Culture, UrlSlug },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<ArticleRevision>(Command).ConfigureAwait(false);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<(ArticleRevision? Current, ArticleRevision? Specific)> GetRevisionBySiteIdCultureUrlSlugAndDateAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		DateTimeOffset DateCreated,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			const string CurrentQuery = @"
SELECT *
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND UrlSlug = @UrlSlug
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC
LIMIT 1;";

			CommandDefinition CurrentCommand = new(
				CurrentQuery,
				new { SiteId, Culture, UrlSlug },
				cancellationToken: CancellationToken);

			ArticleRevision? Current = await Connection.QuerySingleOrDefaultAsync<ArticleRevision>(CurrentCommand).ConfigureAwait(false);
			if (Current is null)
			{
				return default;
			}

			const string RevisionQuery = @"
SELECT *
FROM ArticleRevisions
WHERE CanonicalArticleId = @CanonicalArticleId
  AND DateCreated = @DateCreated
  AND DateDeleted IS NULL
LIMIT 1;";

			CommandDefinition RevisionCommand = new(
				RevisionQuery,
				new { Current.CanonicalArticleId, DateCreated },
				cancellationToken: CancellationToken);

			ArticleRevision? Specific = await Connection.QuerySingleOrDefaultAsync<ArticleRevision>(RevisionCommand).ConfigureAwait(false);

			return (Current, Specific);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleRevision>> GetAllRevisionsBySiteIdCultureAndUrlSlugAsync(
		int SiteId,
		string Culture,
		string UrlSlug,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			const string CanonicalIdQuery = @"
SELECT CanonicalArticleId
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND UrlSlug = @UrlSlug
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC
LIMIT 1;";

			CommandDefinition CanonicalIdCommand = new(
				CanonicalIdQuery,
				new { SiteId, Culture, UrlSlug },
				cancellationToken: CancellationToken);

			Guid? CanonicalArticleId = await Connection.QuerySingleOrDefaultAsync<Guid?>(CanonicalIdCommand).ConfigureAwait(false);
			if (CanonicalArticleId is null)
			{
				return (IReadOnlyList<ArticleRevision>)Array.Empty<ArticleRevision>();
			}

			const string RevisionsQuery = @"
SELECT *
FROM ArticleRevisions
WHERE CanonicalArticleId = @CanonicalArticleId
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC;";

			CommandDefinition RevisionsCommand = new(
				RevisionsQuery,
				new { CanonicalArticleId },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleRevision> Revisions = await Connection.QueryAsync<ArticleRevision>(RevisionsCommand).ConfigureAwait(false);
			IReadOnlyList<ArticleRevision> Result = [.. Revisions];
			return Result;
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleRevision>> GetRecentRevisionsAsync(
		int SiteId,
		string Culture,
		int Limit = 100,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC
LIMIT @Limit;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleRevision>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, Culture, Limit },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleRevision> Revisions = await Connection.QueryAsync<ArticleRevision>(Command).ConfigureAwait(false);
			return [.. Revisions];
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> DeleteByCanonicalIdAsync(
		int SiteId,
		string Culture,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleRevisions
SET DateDeleted = @DateDeleted
WHERE CanonicalArticleId = @CanonicalArticleId
  AND SiteId = @SiteId
  AND Culture = @Culture
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new { CanonicalArticleId, SiteId, Culture, DateDeleted = DateTimeOffset.UtcNow },
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleAuthor>> GetRecentAuthorsForArticleAsync(
		Guid CanonicalArticleId,
		DateTimeOffset? MaxRevisionDate = null,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT u.UserName, u.ProfilePicGuid
FROM ArticleRevisions ar
INNER JOIN Users u ON ar.CreatedByUserId = u.Id
WHERE ar.CanonicalArticleId = @CanonicalArticleId
  AND ar.DateDeleted IS NULL
  AND (@MaxRevisionDate IS NULL OR ar.DateCreated <= @MaxRevisionDate)
GROUP BY u.Id, u.UserName, u.ProfilePicGuid
ORDER BY MAX(ar.DateCreated) DESC
LIMIT 10;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleAuthor>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { CanonicalArticleId, MaxRevisionDate },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleAuthor> Authors = await Connection.QueryAsync<ArticleAuthor>(Command).ConfigureAwait(false);
			return [.. Authors];
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleSitemapEntry>> GetAllCurrentArticlesForSitemapAsync(
		int SiteId,
		string Culture,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT UrlSlug, DateCreated as LastUpdated
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND IsCurrent = 1
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleSitemapEntry>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, Culture },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleSitemapEntry> Articles = await Connection.QueryAsync<ArticleSitemapEntry>(Command).ConfigureAwait(false);
			return [.. Articles];
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleRevision>> GetLatestArticlesWithMagazineInfoboxAsync(
		int SiteId,
		string Culture,
		int Limit = 100,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleRevisions
WHERE SiteId = @SiteId
  AND Culture = @Culture
  AND IsCurrent = 1
  AND DateDeleted IS NULL
  AND Text LIKE '%{{MagazineInfobox%CoverImage=%'
ORDER BY DateCreated DESC
LIMIT @Limit;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleRevision>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, Culture, Limit },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleRevision> Articles = await Connection.QueryAsync<ArticleRevision>(Command).ConfigureAwait(false);
			return [.. Articles];
		}, CancellationToken).ConfigureAwait(false);
	}
}

public sealed record ArticleAuthor(string UserName, string ProfilePicGuid);

public sealed record ArticleSitemapEntry(string UrlSlug, DateTimeOffset LastUpdated);


