namespace WikiWikiWorld.Data.Repositories;

public interface IArticleTalkSubjectRepository
{
	Task<Guid> InsertAsync(
		int SiteId,
		Guid CanonicalArticleId,
		string Subject,
		string UrlSlug,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<ArticleTalkSubject?> GetByIdAsync(
		Guid Id,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleTalkSubject>> GetByArticleIdAsync(
		int SiteId,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default);

	Task<ArticleTalkSubject?> GetByUrlSlugAsync(
		int SiteId,
		Guid CanonicalArticleId,
		string UrlSlug,
		CancellationToken CancellationToken = default);

	Task<bool> UpdateAsync(
		Guid Id,
		string Subject,
		CancellationToken CancellationToken = default);

	Task<bool> MarkAsEditedAsync(
		Guid Id,
		CancellationToken CancellationToken = default);

	Task<bool> DeleteAsync(
		Guid Id,
		CancellationToken CancellationToken = default);
}

public sealed class ArticleTalkSubjectRepository : IArticleTalkSubjectRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public ArticleTalkSubjectRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<Guid> InsertAsync(
		int SiteId,
		Guid CanonicalArticleId,
		string Subject,
		string UrlSlug,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			Guid Id = Guid.NewGuid();

			const string InsertSql = @"
INSERT INTO ArticleTalkSubjects (
	Id,
	SiteId,
	CanonicalArticleId,
	Subject,
	UrlSlug,
	HasBeenEdited,
	CreatedByUserId,
	DateCreated,
	DateModified,
	DateDeleted
) VALUES (
	@Id,
	@SiteId,
	@CanonicalArticleId,
	@Subject,
	@UrlSlug,
	0,
	@CreatedByUserId,
	@DateCreated,
	NULL,
	NULL
);";

			CommandDefinition Command = new(
				InsertSql,
				new
				{
					Id,
					SiteId,
					CanonicalArticleId,
					Subject,
					UrlSlug,
					CreatedByUserId,
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			if (RowsAffected is not 1)
			{
				throw new InvalidOperationException("Insert failed: unexpected number of rows affected.");
			}

			return Id;
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<ArticleTalkSubject?> GetByIdAsync(
		Guid Id,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjects
WHERE Id = @Id
  AND DateDeleted IS NULL
LIMIT 1;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { Id },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<ArticleTalkSubject>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleTalkSubject>> GetByArticleIdAsync(
		int SiteId,
		Guid CanonicalArticleId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjects
WHERE SiteId = @SiteId
  AND CanonicalArticleId = @CanonicalArticleId
  AND DateDeleted IS NULL
ORDER BY DateCreated DESC;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, CanonicalArticleId },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleTalkSubject> Subjects = await Connection.QueryAsync<ArticleTalkSubject>(Command).ConfigureAwait(false);
			return Subjects.AsList();
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<ArticleTalkSubject?> GetByUrlSlugAsync(
		int SiteId,
		Guid CanonicalArticleId,
		string UrlSlug,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjects
WHERE SiteId = @SiteId
  AND CanonicalArticleId = @CanonicalArticleId
  AND UrlSlug = @UrlSlug
  AND DateDeleted IS NULL
LIMIT 1;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, CanonicalArticleId, UrlSlug },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<ArticleTalkSubject>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> UpdateAsync(
		Guid Id,
		string Subject,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjects
SET Subject = @Subject,
	HasBeenEdited = 1,
	DateModified = @DateModified
WHERE Id = @Id
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					Id,
					Subject,
					DateModified = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> MarkAsEditedAsync(
		Guid Id,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjects
SET HasBeenEdited = 1,
	DateModified = @DateModified
WHERE Id = @Id
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					Id,
					DateModified = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> DeleteAsync(
		Guid Id,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjects
SET DateDeleted = @DateDeleted
WHERE Id = @Id
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					Id,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
