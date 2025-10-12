namespace WikiWikiWorld.Data.Repositories;

public interface IUserRoleRepository
{
	Task AssignRoleAsync(Guid UserId, Guid RoleId, DateTimeOffset DateCreated, CancellationToken CancellationToken = default);

	Task<bool> RemoveRoleAsync(Guid UserId, Guid RoleId, CancellationToken CancellationToken = default);

	Task<IEnumerable<Guid>> GetUserRolesAsync(Guid UserId, CancellationToken CancellationToken = default);

	Task<IEnumerable<Guid>> GetUsersInRoleAsync(Guid RoleId, CancellationToken CancellationToken = default);
}

public sealed class UserRoleRepository : IUserRoleRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public UserRoleRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task AssignRoleAsync(
		Guid UserId,
		Guid RoleId,
		DateTimeOffset DateCreated,
		CancellationToken CancellationToken = default)
	{
		await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO UserRoles (UserId, RoleId, DateCreated, DateDeleted)
VALUES (@UserId, @RoleId, @DateCreated, NULL);";

			CommandDefinition Command = new(
				InsertSql,
				new
				{
					UserId,
					RoleId,
					DateCreated
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			await Connection.ExecuteAsync(Command).ConfigureAwait(false);
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> RemoveRoleAsync(
		Guid UserId,
		Guid RoleId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE UserRoles
SET DateDeleted = @DateDeleted
WHERE UserId = @UserId AND RoleId = @RoleId AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					UserId,
					RoleId,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, Durability: WriteDurability.High, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IEnumerable<Guid>> GetUserRolesAsync(
		Guid UserId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT RoleId
FROM UserRoles
WHERE UserId = @UserId AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { UserId },
				cancellationToken: CancellationToken);

			return await Connection.QueryAsync<Guid>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}

	public async Task<IEnumerable<Guid>> GetUsersInRoleAsync(
		Guid RoleId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT UserId
FROM UserRoles
WHERE RoleId = @RoleId AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { RoleId },
				cancellationToken: CancellationToken);

			return await Connection.QueryAsync<Guid>(Command).ConfigureAwait(false);
		}, CancellationToken: CancellationToken).ConfigureAwait(false);
	}
}
