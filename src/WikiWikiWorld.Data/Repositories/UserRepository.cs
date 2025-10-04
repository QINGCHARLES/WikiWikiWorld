namespace WikiWikiWorld.Data.Repositories;

public interface IUserRepository
{
	Task<Guid> InsertAsync(
		string UserName,
		string NormalizedUserName,
		string Email,
		string NormalizedEmail,
		bool EmailConfirmed,
		string PasswordHash,
		string SecurityStamp,
		string ConcurrencyStamp,
		bool TwoFactorEnabled,
		DateTimeOffset? LockoutEnd,
		bool LockoutEnabled,
		int AccessFailedCount,
		string? ProfilePicGuid,
		DateTimeOffset DateCreated,
		DateTimeOffset? DateDeleted,
		CancellationToken CancellationToken = default);

	Task<User?> GetByIdAsync(
		Guid UserId,
		CancellationToken CancellationToken = default);

	Task<User?> GetByEmailAsync(
		string NormalizedEmail,
		CancellationToken CancellationToken = default);

	Task<User?> GetByUserNameAsync(
		string NormalizedUserName,
		CancellationToken CancellationToken = default);

	Task<bool> UpdatePasswordAsync(
		Guid UserId,
		string NewPasswordHash,
		CancellationToken CancellationToken = default);

	Task<bool> SoftDeleteAsync(
		Guid UserId,
		CancellationToken CancellationToken = default);
}

public sealed class UserRepository : IUserRepository
{
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public UserRepository(IDatabaseConnectionFactory ConnectionFactory)
	{
		this.ConnectionFactory = ConnectionFactory ?? throw new ArgumentNullException(nameof(ConnectionFactory));
	}

	public async Task<Guid> InsertAsync(
		string UserName,
		string NormalizedUserName,
		string Email,
		string NormalizedEmail,
		bool EmailConfirmed,
		string PasswordHash,
		string SecurityStamp,
		string ConcurrencyStamp,
		bool TwoFactorEnabled,
		DateTimeOffset? LockoutEnd,
		bool LockoutEnabled,
		int AccessFailedCount,
		string? ProfilePicGuid,
		DateTimeOffset DateCreated,
		DateTimeOffset? DateDeleted,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string InsertSql = @"
INSERT INTO Users (
	Id,
	UserName,
	NormalizedUserName,
	Email,
	NormalizedEmail,
	EmailConfirmed,
	PasswordHash,
	SecurityStamp,
	ConcurrencyStamp,
	TwoFactorEnabled,
	LockoutEnd,
	LockoutEnabled,
	AccessFailedCount,
	ProfilePicGuid,
	DateCreated,
	DateDeleted
) VALUES (
	@Id,
	@UserName,
	@NormalizedUserName,
	@Email,
	@NormalizedEmail,
	@EmailConfirmed,
	@PasswordHash,
	@SecurityStamp,
	@ConcurrencyStamp,
	@TwoFactorEnabled,
	@LockoutEnd,
	@LockoutEnabled,
	@AccessFailedCount,
	@ProfilePicGuid,
	@DateCreated,
	@DateDeleted
);";

			Guid UserId = Guid.NewGuid();

			CommandDefinition Command = new(
				InsertSql,
				new
				{
					Id = UserId,
					UserName,
					NormalizedUserName,
					Email,
					NormalizedEmail,
					EmailConfirmed,
					PasswordHash,
					SecurityStamp,
					ConcurrencyStamp,
					TwoFactorEnabled,
					LockoutEnd,
					LockoutEnabled,
					AccessFailedCount,
					ProfilePicGuid,
					DateCreated,
					DateDeleted
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			if (RowsAffected is not 1)
			{
				throw new InvalidOperationException("Insert failed: unexpected number of rows affected.");
			}

			return UserId;
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<User?> GetByIdAsync(
		Guid UserId,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM Users
WHERE Id = @UserId AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { UserId },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<User>(Command).ConfigureAwait(false);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<User?> GetByEmailAsync(
		string NormalizedEmail,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM Users
WHERE NormalizedEmail = @NormalizedEmail AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { NormalizedEmail },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<User>(Command).ConfigureAwait(false);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<User?> GetByUserNameAsync(
		string NormalizedUserName,
		CancellationToken CancellationToken = default)
	{
		const string Query = @"
SELECT *
FROM Users
WHERE NormalizedUserName = @NormalizedUserName AND DateDeleted IS NULL;";

		return await ConnectionFactory.ExecuteWithRetryAsync(ConnectionMode.Read, async Connection =>
		{
			CommandDefinition Command = new(
				Query,
				new { NormalizedUserName },
				cancellationToken: CancellationToken);

			return await Connection.QuerySingleOrDefaultAsync<User>(Command).ConfigureAwait(false);
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> UpdatePasswordAsync(
		Guid UserId,
		string NewPasswordHash,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE Users
SET PasswordHash = @NewPasswordHash
WHERE Id = @UserId AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					UserId,
					NewPasswordHash
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, CancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> SoftDeleteAsync(
		Guid UserId,
		CancellationToken CancellationToken = default)
	{
		return await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
		{
			const string UpdateSql = @"
UPDATE Users
SET DateDeleted = @DateDeleted
WHERE Id = @UserId AND DateDeleted IS NULL;";

			CommandDefinition Command = new(
				UpdateSql,
				new
				{
					UserId,
					DateDeleted = DateTimeOffset.UtcNow
				},
				transaction: Transaction,
				cancellationToken: CancellationToken);

			int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
			return RowsAffected > 0;
		}, CancellationToken).ConfigureAwait(false);
	}
}
