using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Database;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Repositories;

namespace WikiWikiWorld.Identity;

public sealed class ApplicationRoleStore : IRoleStore<ApplicationRole>
{
	private readonly IRoleRepository _roleRepository;
	private readonly IDatabaseConnectionFactory _connectionFactory;

	public ApplicationRoleStore(IRoleRepository roleRepository, IDatabaseConnectionFactory connectionFactory)
	{
		_roleRepository = roleRepository;
		_connectionFactory = connectionFactory;
	}

	public async Task<IdentityResult> CreateAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		Guid RoleId = await _roleRepository.InsertAsync(
			role.Name ?? string.Empty,
			role.NormalizedName ?? string.Empty,
			role.ConcurrencyStamp ?? Guid.NewGuid().ToString(),
			role.DateCreated,
			cancellationToken
		);

		role.Id = RoleId;
		return IdentityResult.Success;
	}

	public async Task<IdentityResult> UpdateAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		try
		{
			bool Success = await _connectionFactory.ExecuteWithRetryInTransactionAsync(async (Connection, Transaction) =>
			{
				const string UpdateSql = @"
UPDATE Roles
SET Name = @Name,
	NormalizedName = @NormalizedName,
	ConcurrencyStamp = @ConcurrencyStamp
WHERE Id = @Id AND DateDeleted IS NULL;";

				CommandDefinition Command = new(
					UpdateSql,
					new
					{
						role.Id,
						role.Name,
						role.NormalizedName,
						role.ConcurrencyStamp
					},
					transaction: Transaction,
					cancellationToken: cancellationToken);

				int RowsAffected = await Connection.ExecuteAsync(Command).ConfigureAwait(false);
				return RowsAffected > 0;
			}, cancellationToken).ConfigureAwait(false);

			return Success
				? IdentityResult.Success
				: IdentityResult.Failed(new IdentityError { Description = "Role update failed." });
		}
		catch (Exception Ex)
		{
			return IdentityResult.Failed(new IdentityError { Description = $"An error occurred while updating the role: {Ex.Message}" });
		}
	}

	public async Task<ApplicationRole?> FindByIdAsync(
		string roleId,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(roleId, out Guid RoleGuid))
		{
			return null;
		}

		Role? Role = await _roleRepository.GetByIdAsync(RoleGuid, cancellationToken);
		return Role is null ? null : MapToApplicationRole(Role);
	}

	public async Task<ApplicationRole?> FindByNameAsync(
		string normalizedRoleName,
		CancellationToken cancellationToken)
	{
		Role? Role = await _roleRepository.GetByNameAsync(normalizedRoleName, cancellationToken);
		return Role is null ? null : MapToApplicationRole(Role);
	}

	public async Task<IdentityResult> DeleteAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		bool Success = await _roleRepository.SoftDeleteAsync(role.Id, cancellationToken);
		return Success
			? IdentityResult.Success
			: IdentityResult.Failed(new IdentityError { Description = "Role delete failed." });
	}

	public Task<string?> GetNormalizedRoleNameAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(role.NormalizedName);
	}

	public Task<string> GetRoleIdAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(role.Id.ToString());
	}

	public Task<string?> GetRoleNameAsync(
		ApplicationRole role,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(role.Name);
	}

	public Task SetNormalizedRoleNameAsync(
		ApplicationRole role,
		string? normalizedName,
		CancellationToken cancellationToken)
	{
		role.NormalizedName = normalizedName;
		return Task.CompletedTask;
	}

	public Task SetRoleNameAsync(
		ApplicationRole role,
		string? roleName,
		CancellationToken cancellationToken)
	{
		role.Name = roleName;
		return Task.CompletedTask;
	}

	private static ApplicationRole MapToApplicationRole(Role role)
	{
		return new ApplicationRole
		{
			Id = role.Id,
			Name = role.Name,
			NormalizedName = role.NormalizedName,
			ConcurrencyStamp = role.ConcurrencyStamp,
			DateCreated = role.DateCreated,
			DateDeleted = role.DateDeleted
		};
	}

	public void Dispose() { }
}