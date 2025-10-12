namespace WikiWikiWorld.Data.Repositories;

public interface IRoleRepository
{
	Task<Guid> InsertAsync(
		string Name,
		string NormalizedName,
		string ConcurrencyStamp,
		DateTimeOffset DateCreated,
		CancellationToken CancellationToken = default);

	Task<Role?> GetByIdAsync(Guid RoleId, CancellationToken CancellationToken = default);

	Task<Role?> GetByNameAsync(string NormalizedName, CancellationToken CancellationToken = default);

	Task<bool> SoftDeleteAsync(Guid RoleId, CancellationToken CancellationToken = default);
}

public sealed class RoleRepository : IRoleRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public RoleRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<Guid> InsertAsync(
		string Name,
		string NormalizedName,
		string ConcurrencyStamp,
		DateTimeOffset DateCreated,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO Roles (
	Id,
	Name,
	NormalizedName,
	ConcurrencyStamp,
	DateCreated,
	DateDeleted
) VALUES (
	@Id,
	@Name,
	@NormalizedName,
	@ConcurrencyStamp,
	@DateCreated,
	NULL
);";

			Guid RoleId = Guid.NewGuid();

			CommandDefinition Command = new(
				InsertSql,
				new
				{
					Id = RoleId,
					Name,
					NormalizedName,
					ConcurrencyStamp,
					DateCreated
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			if (RowsAffected is not 1)
			{
				throw new InvalidOperationException("Insert failed: unexpected number of rows affected.");
			}

			return RoleId;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<Role?> GetByIdAsync(
		Guid RoleId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM Roles
WHERE Id = @RoleId AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { RoleId },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<Role>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<Role?> GetByNameAsync(
		string NormalizedName,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM Roles
WHERE NormalizedName = @NormalizedName AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { NormalizedName },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<Role>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> SoftDeleteAsync(
		Guid RoleId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE Roles
SET DateDeleted = @DateDeleted
WHERE Id = @RoleId AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					RoleId,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
