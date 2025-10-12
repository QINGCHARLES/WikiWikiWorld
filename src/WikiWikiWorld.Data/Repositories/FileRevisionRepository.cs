namespace WikiWikiWorld.Data.Repositories;

public interface IFileRevisionRepository
{
	Task<long> InsertAsync(
		Guid? CanonicalFileId,
		FileType Type,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? Source,
		string RevisionReason,
		string SourceAndRevisionReasonCulture,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default);

	Task<FileRevision?> GetCurrentByCanonicalFileIdAsync(
		Guid CanonicalFileId,
		CancellationToken CancellationToken = default);
}

public sealed class FileRevisionRepository : IFileRevisionRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public FileRevisionRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<long> InsertAsync(
		Guid? CanonicalFileId,
		FileType Type,
		string Filename,
		string MimeType,
		long FileSizeBytes,
		string? Source,
		string RevisionReason,
		string SourceAndRevisionReasonCulture,
		Guid CreatedByUserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			CanonicalFileId ??= Guid.NewGuid();

			if (CanonicalFileId != null)
			{
				const string ResetCurrentSql = @"
UPDATE FileRevisions
SET IsCurrent = 0
WHERE CanonicalFileId = @CanonicalFileId
AND IsCurrent = 1
AND DateDeleted IS NULL;";

				CommandDefinition ResetCommand = new(
					ResetCurrentSql,
					new { CanonicalFileId },
					transaction: Transaction,
					cancellationToken: CancellationToken);

				await Connection.ExecuteAsync(ResetCommand).ConfigureAwait(false);
			}

			const string InsertSql = @"
INSERT INTO FileRevisions (
	CanonicalFileId,
	IsCurrent,
	Type,
	Filename,
	MimeType,
	FileSizeBytes,
	Source,
	RevisionReason,
	SourceAndRevisionReasonCulture,
	CreatedByUserId,
	DateCreated,
	DateDeleted
) VALUES (
	@CanonicalFileId,
	1, -- Always mark new entry as current
	@Type,
	@Filename,
	@MimeType,
	@FileSizeBytes,
	@Source,
	@RevisionReason,
	@SourceAndRevisionReasonCulture,
	@CreatedByUserId,
	@DateCreated,
	NULL
);
SELECT last_insert_rowid();";

			CommandDefinition InsertCommand = new(
				InsertSql,
				new
				{
					CanonicalFileId,
					Type = Type.ToString().ToUpperInvariant(),
					Filename,
					MimeType,
					FileSizeBytes,
					Source,
					RevisionReason,
					SourceAndRevisionReasonCulture,
					CreatedByUserId,
					DateCreated = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			return await Connection.ExecuteScalarAsync<long>(InsertCommand).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<FileRevision?> GetCurrentByCanonicalFileIdAsync(
		Guid CanonicalFileId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM FileRevisions
WHERE CanonicalFileId = @CanonicalFileId
  AND IsCurrent = 1
  AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { CanonicalFileId },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<FileRevision>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
