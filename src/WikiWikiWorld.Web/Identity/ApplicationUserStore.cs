using Dapper;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Database;

namespace WikiWikiWorld.Identity;

public sealed class ApplicationUserStore : IUserStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>
{
	private readonly IUserRepository UserRepository;
	private readonly IDatabaseConnectionFactory ConnectionFactory;

	public ApplicationUserStore(IUserRepository UserRepository, IDatabaseConnectionFactory ConnectionFactory)
	{
		this.UserRepository = UserRepository;
		this.ConnectionFactory = ConnectionFactory;
	}

	public async Task<IdentityResult> CreateAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		Guid UserId = await UserRepository.InsertAsync(
			user.UserName ?? string.Empty,
			user.NormalizedUserName ?? string.Empty,
			user.Email ?? string.Empty,
			user.NormalizedEmail ?? string.Empty,
			user.EmailConfirmed,
			user.PasswordHash ?? string.Empty,
			user.SecurityStamp ?? string.Empty,
			user.ConcurrencyStamp ?? Guid.NewGuid().ToString(),
			user.TwoFactorEnabled,
			user.LockoutEnd,
			user.LockoutEnabled,
			user.AccessFailedCount,
			user.ProfilePicGuid,
			user.DateCreated,
			user.DateDeleted,
			cancellationToken);

		user.Id = UserId;
		return IdentityResult.Success;
	}

	public async Task<IdentityResult> UpdateAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		try
		{
			bool Success = await ConnectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
			{
				const string UpdateSql = @"
UPDATE Users
SET UserName = @UserName,
	NormalizedUserName = @NormalizedUserName,
	Email = @Email,
	NormalizedEmail = @NormalizedEmail,
	EmailConfirmed = @EmailConfirmed,
	PasswordHash = @PasswordHash,
	SecurityStamp = @SecurityStamp,
	ConcurrencyStamp = @ConcurrencyStamp,
	TwoFactorEnabled = @TwoFactorEnabled,
	LockoutEnd = @LockoutEnd,
	LockoutEnabled = @LockoutEnabled,
	AccessFailedCount = @AccessFailedCount,
	ProfilePicGuid = @ProfilePicGuid
WHERE Id = @Id AND DateDeleted IS NULL;";

				CommandDefinition Command = new(
					UpdateSql,
					new
					{
						user.Id,
						user.UserName,
						user.NormalizedUserName,
						user.Email,
						user.NormalizedEmail,
						user.EmailConfirmed,
						user.PasswordHash,
						user.SecurityStamp,
						user.ConcurrencyStamp,
						user.TwoFactorEnabled,
						user.LockoutEnd,
						user.LockoutEnabled,
						user.AccessFailedCount,
						user.ProfilePicGuid
					},
					transaction: Transaction,
					cancellationToken: cancellationToken);

				int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
				return RowsAffected > 0;
			}, Durability: WriteDurability.High, CancellationToken: cancellationToken).ConfigureAwait(false);

			return Success
				? IdentityResult.Success
				: IdentityResult.Failed(new IdentityError { Description = "User update failed." });
		}
		catch (Exception Ex)
		{
			return IdentityResult.Failed(new IdentityError { Description = $"An error occurred while updating the user: {Ex.Message}" });
		}
	}

	public async Task<IdentityResult> DeleteAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		bool Success = await UserRepository.SoftDeleteAsync(user.Id, cancellationToken);
		return Success
			? IdentityResult.Success
			: IdentityResult.Failed(new IdentityError { Description = "User delete failed." });
	}

	public async Task<ApplicationUser?> FindByIdAsync(
		string userId,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(userId, out Guid ParsedUserId))
		{
			return null;
		}

		User? User = await UserRepository.GetByIdAsync(ParsedUserId, cancellationToken);
		return User is null ? null : MapToApplicationUser(User);
	}

	public async Task<ApplicationUser?> FindByNameAsync(
		string normalizedUserName,
		CancellationToken cancellationToken)
	{
		User? User = await UserRepository.GetByUserNameAsync(normalizedUserName, cancellationToken);
		return User is null ? null : MapToApplicationUser(User);
	}

	public Task<string> GetUserIdAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(user.Id.ToString());
	}

	public Task<string?> GetUserNameAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(user.UserName);
	}

	public Task SetUserNameAsync(
		ApplicationUser user,
		string? userName,
		CancellationToken cancellationToken)
	{
		user.UserName = userName;
		return Task.CompletedTask;
	}

	public Task<string?> GetNormalizedUserNameAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(user.NormalizedUserName);
	}

	public Task SetNormalizedUserNameAsync(
		ApplicationUser user,
		string? normalizedName,
		CancellationToken cancellationToken)
	{
		user.NormalizedUserName = normalizedName;
		return Task.CompletedTask;
	}

	public Task<string?> GetPasswordHashAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(user.PasswordHash);
	}

	public Task SetPasswordHashAsync(
		ApplicationUser user,
		string? passwordHash,
		CancellationToken cancellationToken)
	{
		user.PasswordHash = passwordHash;
		return Task.CompletedTask;
	}

	public Task<bool> HasPasswordAsync(
		ApplicationUser user,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));
	}

	private static ApplicationUser MapToApplicationUser(User User)
	{
		return new ApplicationUser
		{
			Id = User.Id,
			UserName = User.UserName,
			NormalizedUserName = User.NormalizedUserName,
			Email = User.Email,
			NormalizedEmail = User.NormalizedEmail,
			EmailConfirmed = User.EmailConfirmed,
			PasswordHash = User.PasswordHash,
			SecurityStamp = User.SecurityStamp,
			ConcurrencyStamp = User.ConcurrencyStamp,
			TwoFactorEnabled = User.TwoFactorEnabled,
			LockoutEnabled = User.LockoutEnabled,
			LockoutEnd = User.LockoutEnd,
			AccessFailedCount = User.AccessFailedCount,
			ProfilePicGuid = User.ProfilePicGuid,
			DateCreated = User.DateCreated,
			DateDeleted = User.DateDeleted
		};
	}

	public void Dispose() { }
}