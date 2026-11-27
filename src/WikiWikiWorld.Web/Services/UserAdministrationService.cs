using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Searches registered accounts across sites and changes their administrative access status.
/// </summary>
/// <param name="Context">The request's Identity database context.</param>
/// <param name="Normalizer">The same lookup normalizer used by Identity.</param>
public sealed class UserAdministrationService(WikiWikiWorldDbContext Context, ILookupNormalizer Normalizer)
{
	/// <summary>Gets the number of accounts shown on each directory page.</summary>
	public const int PageSize = 50;

	/// <summary>Loads a page of accounts with stable ordering, including accounts with disabled access.</summary>
	/// <param name="Search">An optional username fragment.</param>
	/// <param name="IsDisabled">The optional account access status to match.</param>
	/// <param name="RoleId">An optional role to match.</param>
	/// <param name="OldestFirst">Whether to sort by oldest registration first.</param>
	/// <param name="PageNumber">The requested one-based page number.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The filtered page and its navigation bounds.</returns>
	public async Task<RegisteredUsersPage> FindUsersAsync(string? Search, bool? IsDisabled, Guid? RoleId,
		bool OldestFirst, int PageNumber, CancellationToken CancellationToken)
	{
		IQueryable<User> Query = Context.Users.AsNoTracking();
		string? NormalizedSearch = Normalizer.NormalizeName(Search?.Trim());
		if (!string.IsNullOrEmpty(NormalizedSearch))
		{
			Query = Query.Where(Account => Account.NormalizedUserName != null && Account.NormalizedUserName.Contains(NormalizedSearch));
		}

		if (IsDisabled is bool Disabled)
		{
			Query = Query.Where(Account => Account.IsDisabled == Disabled);
		}

		if (RoleId is Guid SelectedRoleId)
		{
			Query = Query.Where(Account => Context.UserRoles.Any(Link => Link.UserId == Account.Id && Link.RoleId == SelectedRoleId)
				&& Context.Roles.Any(AssignedRole => AssignedRole.Id == SelectedRoleId));
		}

		int TotalCount = await Query.CountAsync(CancellationToken);
		int PageCount = Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
		PageNumber = Math.Clamp(PageNumber, 1, PageCount);

		IOrderedQueryable<User> Ordered = OldestFirst
			? Query.OrderBy(Account => Account.DateCreated).ThenBy(Account => Account.Id)
			: Query.OrderByDescending(Account => Account.DateCreated).ThenByDescending(Account => Account.Id);
		List<RegisteredUser> Accounts = await Ordered
			.Skip((PageNumber - 1) * PageSize)
			.Take(PageSize)
			.Select(Account => new RegisteredUser(Account.Id, Account.UserName, Account.Email, Account.DateCreated, Account.IsDisabled))
			.ToListAsync(CancellationToken);

		List<RegisteredUserRole> Roles = await Context.Roles.AsNoTracking().Where(Role => Role.Name != null)
			.OrderBy(Role => Role.Name)
			.Select(Role => new RegisteredUserRole(Role.Id, Role.Name!))
			.ToListAsync(CancellationToken);

		return new(Accounts, Roles, TotalCount, PageNumber, PageCount);
	}

	/// <summary>Changes account access and rotates its security stamp as part of the same write.</summary>
	/// <param name="AccountId">The registered account to update.</param>
	/// <param name="IsDisabled">The desired access state.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The account's username if the update succeeds, or null if the account is missing or changed concurrently.</returns>
	public async Task<string?> SetDisabledAsync(Guid AccountId, bool IsDisabled, CancellationToken CancellationToken)
	{
		User? Account = await Context.Users.SingleOrDefaultAsync(User => User.Id == AccountId, CancellationToken);
		if (Account is null) return null;
		if (Account.IsDisabled == IsDisabled) return Account.UserName ?? Account.Id.ToString();

		Account.IsDisabled = IsDisabled;
		Account.SecurityStamp = Guid.NewGuid().ToString("N");
		Account.ConcurrencyStamp = Guid.NewGuid().ToString("N");

		try
		{
			await Context.SaveChangesAsync(CancellationToken);
		}
		catch (DbUpdateConcurrencyException)
		{
			return null;
		}

		return Account.UserName ?? Account.Id.ToString();
	}
}

/// <summary>Contains the account details shown in the administrator directory.</summary>
/// <param name="Id">The account identifier.</param>
/// <param name="UserName">The username, if available.</param>
/// <param name="Email">The registered email address, if available.</param>
/// <param name="DateCreated">The recorded registration instant.</param>
/// <param name="IsDisabled">Whether access has been disabled by an administrator.</param>
public sealed record RegisteredUser(Guid Id, string? UserName, string? Email, DateTimeOffset DateCreated, bool IsDisabled);

/// <summary>Provides a role choice for filtering the administrator directory.</summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role's display name.</param>
public sealed record RegisteredUserRole(Guid Id, string Name);

/// <summary>Holds one page of directory results and the data needed to navigate it.</summary>
/// <param name="Accounts">The accounts on the current page.</param>
/// <param name="Roles">The available role filters.</param>
/// <param name="TotalCount">The total number of accounts matching the filters.</param>
/// <param name="PageNumber">The current one-based page number.</param>
/// <param name="PageCount">The total number of pages, with one page even for empty results.</param>
public sealed record RegisteredUsersPage(IReadOnlyList<RegisteredUser> Accounts, IReadOnlyList<RegisteredUserRole> Roles,
	int TotalCount, int PageNumber, int PageCount);
