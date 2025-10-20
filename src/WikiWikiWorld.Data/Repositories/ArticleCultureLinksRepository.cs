namespace WikiWikiWorld.Data.Repositories;

public interface IArticleCultureLinkRepository
{
	Task<long> InsertAsync(
		int SiteId,
		Guid CanonicalArticleId,
		Guid ArticleCultureLinkGroupId,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleCultureLink>> GetByCanonicalArticleIdAsync(
		int SiteId,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleCultureLink>> GetByGroupIdAsync(
		int SiteId,
		Guid ArticleCultureLinkGroupId,
		CancellationToken CancellationToken = default);

	Task<bool> DeleteAsync(
		int SiteId,
		Guid CanonicalArticleId,
		Guid DeletedByUserId,
		CancellationToken CancellationToken = default);
}

public sealed class ArticleCultureLinkRepository : IArticleCultureLinkRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public ArticleCultureLinkRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<long> InsertAsync(
		int SiteId,
		Guid CanonicalArticleId,
		Guid ArticleCultureLinkGroupId,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO ArticleCultureLinks (
	SiteId,
	CanonicalArticleId,
	ArticleCultureLinkGroupId,
	CreatedByUserId,
	DateCreated,
	DeletedByUserId,
	DateDeleted
) VALUES (
	@SiteId,
	@CanonicalArticleId,
	@ArticleCultureLinkGroupId,
	@CreatedByUserId,
	@DateCreated,
	NULL,
	NULL
);
SELECT last_insert_rowid();";

			CommandDefinition Command = new(
				InsertSql,
				new
				{
					SiteId,
					CanonicalArticleId,
					ArticleCultureLinkGroupId,
					CreatedByUserId,
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			return await Connection.ExecuteScalarAsync<long>(Command).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleCultureLink>> GetByCanonicalArticleIdAsync(
		int SiteId,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleCultureLinks
WHERE SiteId = @SiteId
  AND CanonicalArticleId = @CanonicalArticleId
  AND DateDeleted IS NULL;";

	return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
	{
		CommandDefinition Command = new(
			Query,
			new { SiteId, CanonicalArticleId },
			cancellationToken: CancellationToken);

		IEnumerable<ArticleCultureLink> Links = await Connection.QueryAsync<ArticleCultureLink>(Command).ConfigureAwait(false);
		return (IReadOnlyList<ArticleCultureLink>)[.. Links];
	}, CancellationToken: CancellationToken).ConfigureAwait(false);
}

public async Task<IReadOnlyList<ArticleCultureLink>> GetByGroupIdAsync(
		int SiteId,
		Guid ArticleCultureLinkGroupId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleCultureLinks
WHERE SiteId = @SiteId
  AND ArticleCultureLinkGroupId = @ArticleCultureLinkGroupId
  AND DateDeleted IS NULL;";

	return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
	{
		CommandDefinition Command = new(
			Query,
			new { SiteId, ArticleCultureLinkGroupId },
			cancellationToken: CancellationToken);

		IEnumerable<ArticleCultureLink> Links = await Connection.QueryAsync<ArticleCultureLink>(Command).ConfigureAwait(false);
		return (IReadOnlyList<ArticleCultureLink>)[.. Links];
	}, CancellationToken: CancellationToken).ConfigureAwait(false);
}

public async Task<bool> DeleteAsync(
		int SiteId,
		Guid CanonicalArticleId,
		Guid DeletedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleCultureLinks
SET DeletedByUserId = @DeletedByUserId,
	DateDeleted = @DateDeleted
WHERE SiteId = @SiteId
  AND CanonicalArticleId = @CanonicalArticleId
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					SiteId,
					CanonicalArticleId,
					DeletedByUserId,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
