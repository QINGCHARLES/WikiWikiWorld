using System.Linq;

namespace WikiWikiWorld.Data.Repositories;

public interface IArticleTalkSubjectPostRepository
{
	Task<long> InsertAsync(
		long ArticleTalkSubjectId,
		long? ParentTalkSubjectPostId,
		string Text,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<ArticleTalkSubjectPost?> GetByIdAsync(
		long Id,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleTalkSubjectPost>> GetBySubjectIdAsync(
		long ArticleTalkSubjectId,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleTalkSubjectPost>> GetByParentIdAsync(
		long ParentTalkSubjectPostId,
		CancellationToken CancellationToken = default);

	Task<IReadOnlyList<ArticleTalkSubjectPost>> GetTopLevelPostsBySubjectIdAsync(
		long ArticleTalkSubjectId,
		CancellationToken CancellationToken = default);

	Task<bool> UpdateTextAsync(
		long Id,
		string Text,
		CancellationToken CancellationToken = default);

	Task<bool> MarkAsEditedAsync(
		long Id,
		CancellationToken CancellationToken = default);

	Task<bool> DeleteAsync(
		long Id,
		CancellationToken CancellationToken = default);
}

public sealed class ArticleTalkSubjectPostRepository : IArticleTalkSubjectPostRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public ArticleTalkSubjectPostRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<long> InsertAsync(
		long ArticleTalkSubjectId,
		long? ParentTalkSubjectPostId,
		string Text,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO ArticleTalkSubjectPosts (
	ArticleTalkSubjectId,
	ParentTalkSubjectPostId,
	Text,
	HasBeenEdited,
	CreatedByUserId,
	DateCreated,
	DateModified,
	DateDeleted
) VALUES (
	@ArticleTalkSubjectId,
	@ParentTalkSubjectPostId,
	@Text,
	0,
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
					ArticleTalkSubjectId,
					ParentTalkSubjectPostId,
					Text,
					CreatedByUserId = CreatedByUserId.ToString(),
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			return await Connection.ExecuteScalarAsync<long>(Command).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<ArticleTalkSubjectPost?> GetByIdAsync(
		long Id,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjectPosts
WHERE Id = @Id
  AND DateDeleted IS NULL
LIMIT 1;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { Id },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<ArticleTalkSubjectPost>(Command).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleTalkSubjectPost>> GetBySubjectIdAsync(
		long ArticleTalkSubjectId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjectPosts
WHERE ArticleTalkSubjectId = @ArticleTalkSubjectId
  AND DateDeleted IS NULL
ORDER BY DateCreated ASC;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleTalkSubjectPost>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { ArticleTalkSubjectId },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleTalkSubjectPost> Posts = await Connection.QueryAsync<ArticleTalkSubjectPost>(Command).ConfigureAwait(false);
			return [.. Posts];
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleTalkSubjectPost>> GetByParentIdAsync(
		long ParentTalkSubjectPostId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjectPosts
WHERE ParentTalkSubjectPostId = @ParentTalkSubjectPostId
  AND DateDeleted IS NULL
ORDER BY DateCreated ASC;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleTalkSubjectPost>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { ParentTalkSubjectPostId },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleTalkSubjectPost> Posts = await Connection.QueryAsync<ArticleTalkSubjectPost>(Command).ConfigureAwait(false);
			return [.. Posts];
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ArticleTalkSubjectPost>> GetTopLevelPostsBySubjectIdAsync(
		long ArticleTalkSubjectId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM ArticleTalkSubjectPosts
WHERE ArticleTalkSubjectId = @ArticleTalkSubjectId
  AND ParentTalkSubjectPostId IS NULL
  AND DateDeleted IS NULL
ORDER BY DateCreated ASC;";

		return await ConnectionFactory.ExecuteWithRetryAsync<IReadOnlyList<ArticleTalkSubjectPost>>(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { ArticleTalkSubjectId },
				cancellationToken: CancellationToken);

			IEnumerable<ArticleTalkSubjectPost> Posts = await Connection.QueryAsync<ArticleTalkSubjectPost>(Command).ConfigureAwait(false);
			return [.. Posts];
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> UpdateTextAsync(
		long Id,
		string Text,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjectPosts
SET Text = @Text,
	HasBeenEdited = 1,
	DateModified = @DateModified
WHERE Id = @Id
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					Id,
					Text,
					DateModified = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> MarkAsEditedAsync(
		long Id,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjectPosts
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
		long Id,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE ArticleTalkSubjectPosts
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
