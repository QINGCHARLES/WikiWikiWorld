using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Gives an account a role, or takes it away, from the command line without starting the website. This is how the first
/// Administrator is appointed; there is no role-management page yet.
/// </summary>
/// <remarks>
/// Permission checks read roles from the database on every request, so the change applies to the account's next request,
/// whether it signs in with a cookie or an API token.
/// </remarks>
public static class RoleAssignmentCommand
{
	/// <summary>
	/// Runs <c>grant-role</c> or <c>revoke-role</c> against an existing database.
	/// </summary>
	/// <param name="Arguments">The verb, then an existing database path, a username and a role name (case-insensitive).</param>
	/// <returns>Zero on success (including when nothing needed changing), two for usage errors, or one when the account or role is not found or the write fails.</returns>
	public static async Task<int> RunAsync(string[] Arguments)
	{
		if (Arguments.Length != 4 || Arguments[0] is not ("grant-role" or "revoke-role"))
		{
			Console.Error.WriteLine("Usage: grant-role|revoke-role <database-path> <username> <role>");
			Console.Error.WriteLine($"Roles: {string.Join(", ", AppRoles.All.Select(Role => $"\"{Role}\""))}");
			return 2;
		}

		bool Grant = Arguments[0] == "grant-role";
		string DatabasePath = Path.GetFullPath(Arguments[1]);
		string UserName = Arguments[2].Trim();
		string RoleName = Arguments[3].Trim();

		try
		{
			SqliteConnectionStringBuilder ConnectionString = new()
			{
				DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, DefaultTimeout = 30
			};
			await using SqliteConnection Connection = new(ConnectionString.ToString());
			await Connection.OpenAsync();
			await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
				.UseSqlite(Connection).Options);

			// Identity's default normalizer upper-cases with the invariant culture.
			string NormalizedUserName = UserName.ToUpperInvariant();
			string NormalizedRoleName = RoleName.ToUpperInvariant();

			User? Account = await Context.Users.SingleOrDefaultAsync(Candidate => Candidate.NormalizedUserName == NormalizedUserName);
			if (Account is null)
			{
				Console.Error.WriteLine($"No active account is named '{UserName}'.");
				return 1;
			}

			Role? TargetRole = await Context.Roles.SingleOrDefaultAsync(Candidate => Candidate.NormalizedName == NormalizedRoleName);
			if (TargetRole is null)
			{
				Console.Error.WriteLine($"No role is named '{RoleName}'. Roles: {string.Join(", ", AppRoles.All)}. Has AddRolesAndTrustedLinkDomains.sql been applied?");
				return 1;
			}

			IdentityUserRole<Guid>? Existing = await Context.UserRoles
				.SingleOrDefaultAsync(Link => Link.UserId == Account.Id && Link.RoleId == TargetRole.Id);

			if (Grant && Existing is null)
			{
				Context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = Account.Id, RoleId = TargetRole.Id });
				await Context.SaveChangesAsync();
				Console.WriteLine($"{DatabasePath}: {Account.UserName} is now {TargetRole.Name}.");
			}
			else if (!Grant && Existing is not null)
			{
				Context.UserRoles.Remove(Existing);
				await Context.SaveChangesAsync();
				Console.WriteLine($"{DatabasePath}: {Account.UserName} is no longer {TargetRole.Name}.");
			}
			else
			{
				Console.WriteLine($"{DatabasePath}: no change; {Account.UserName} {(Grant ? "already is" : "is not")} {TargetRole.Name}.");
			}

			return 0;
		}
		catch (Exception Error)
		{
			Console.Error.WriteLine($"Role change failed: {Error.Message}");
			return 1;
		}
	}
}
