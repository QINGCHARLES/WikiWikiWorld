namespace WikiWikiWorld.Data.Repositories;

public interface IDownloadUrlsRepository
{
	Task<long> InsertAsync(
		int SiteId,
		string HashSha256,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? DownloadUrls,
		int? Quality,
		bool? NeedsOcr,
		bool? IsComplete,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<DownloadUrl?> GetByHashAsync(
		int SiteId,
		string HashSha256,
		CancellationToken CancellationToken = default);

	Task<bool> UpdateAsync(
		long Id,
		int SiteId,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? DownloadUrls = null,
		int? Quality = null,
		bool? NeedsOcr = null,
		bool? IsComplete = null,
		CancellationToken CancellationToken = default);

	Task<bool> DeleteAsync(
		int SiteId,
		string HashSha256,
		CancellationToken CancellationToken = default);
}

public sealed class DownloadUrlsRepository : IDownloadUrlsRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public DownloadUrlsRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<long> InsertAsync(
		int SiteId,
		string HashSha256,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? DownloadUrls,
		int? Quality,
		bool? NeedsOcr,
		bool? IsComplete,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO DownloadUrls (
	SiteId,
	HashSha256,
	Filename,
	MimeType,
	FileSizeBytes,
	DownloadUrls,
	Quality,
	NeedsOcr,
	IsComplete,
	CreatedByUserId,
	DateCreated,
	DateModified,
	DateDeleted
) VALUES (
	@SiteId,
	@HashSha256,
	@Filename,
	@MimeType,
	@FileSizeBytes,
	@DownloadUrls,
	@Quality,
	@NeedsOcr,
	@IsComplete,
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
					HashSha256,
					Filename,
					MimeType,
					FileSizeBytes,
					DownloadUrls,
					Quality,
					NeedsOcr = NeedsOcr.HasValue ? (NeedsOcr.Value ? 1 : 0) : (int?)null,
					IsComplete = IsComplete.HasValue ? (IsComplete.Value ? 1 : 0) : (int?)null,
					CreatedByUserId,
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			return await Connection.ExecuteScalarAsync<long>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<DownloadUrl?> GetByHashAsync(
	int SiteId,
	string HashSha256,
	CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT
	Id,
	SiteId,
	HashSha256,
	Filename,
	MimeType,
	FileSizeBytes,
	DownloadUrls,
	Quality,
	NeedsOcr,
	IsComplete,
	CreatedByUserId,
	DateCreated,
	DateModified,
	DateDeleted
FROM DownloadUrls
WHERE SiteId = @SiteId
  AND HashSha256 = @HashSha256
  AND DateDeleted IS NULL
LIMIT 1;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { SiteId, HashSha256 },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<DownloadUrl>(Command).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> UpdateAsync(
		long Id,
		int SiteId,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? DownloadUrls = null,
		int? Quality = null,
		bool? NeedsOcr = null,
		bool? IsComplete = null,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE DownloadUrls
SET Filename = @Filename,
	MimeType = @MimeType,
	FileSizeBytes = @FileSizeBytes,
	DownloadUrls = @DownloadUrls,
	Quality = @Quality,
	NeedsOcr = @NeedsOcr,
	IsComplete = @IsComplete,
	DateModified = @DateModified
WHERE Id = @Id
  AND SiteId = @SiteId
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					Id,
					SiteId,
					Filename,
					MimeType,
					FileSizeBytes,
					DownloadUrls,
					Quality,
					NeedsOcr = NeedsOcr.HasValue ? (NeedsOcr.Value ? 1 : 0) : (int?)null,
					IsComplete = IsComplete.HasValue ? (IsComplete.Value ? 1 : 0) : (int?)null,
					DateModified = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> DeleteAsync(
		int SiteId,
		string HashSha256,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE DownloadUrls
SET DateDeleted = @DateDeleted
WHERE SiteId = @SiteId
  AND HashSha256 = @HashSha256
  AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					SiteId,
					HashSha256,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
