using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// An action that only some roles may take. Each value is also an authorization policy of the same name, so pages use
/// <c>[Authorize(Policy = nameof(Permission.UseAdminTools))]</c>; which roles hold it is decided in <see cref="AppRoles.Grants"/>.
/// </summary>
public enum Permission
{
	/// <summary>Manage user accounts, domain redirects, and file migration under <c>/Admin</c>.</summary>
	UseAdminTools,

	/// <summary>Delete any article. Owners may still delete their own <c>@user</c> page without this.</summary>
	DeleteArticles,

	/// <summary>Confirm, deny or withdraw trusted link domains, which decides whether links to a domain are followed.</summary>
	ReviewTrustedLinkDomains
}

/// <summary>
/// The application's roles and the permissions each one holds. Signed-in accounts without a role are ordinary members:
/// they edit, upload and propose trusted domains.
/// </summary>
/// <remarks>
/// Roles apply across every site. The role rows are created by <c>AddRolesAndTrustedLinkDomains.sql</c> and given out
/// with the <c>grant-role</c> command. Role names are not translated.
/// </remarks>
public static class AppRoles
{
	/// <summary>Runs the site: admin tools, trusted-domain review, and everything a Senior Editor can do.</summary>
	public const string Administrator = "Administrator";

	/// <summary>Trusted content editor who may delete articles.</summary>
	public const string SeniorEditor = "Senior Editor";

	/// <summary>Handles conduct (talk pages, messages, accounts). Holds no permission yet; reserved for moderation tools.</summary>
	public const string Moderator = "Moderator";

	/// <summary>Marks an automation or API account. Holds no extra permission.</summary>
	public const string Bot = "Bot";

	/// <summary>
	/// Gets every role name, in rank order.
	/// </summary>
	public static IReadOnlyList<string> All { get; } = [Administrator, SeniorEditor, Moderator, Bot];

	/// <summary>
	/// Decides whether any of an account's roles holds a permission.
	/// </summary>
	/// <param name="RoleNames">The account's role names, compared case-insensitively.</param>
	/// <param name="Permission">The permission being checked.</param>
	/// <returns>True when one of the roles holds the permission; false for unknown permissions.</returns>
	public static bool Grants(IReadOnlySet<string> RoleNames, Permission Permission) => Permission switch
	{
		Permission.UseAdminTools => RoleNames.Contains(Administrator),
		Permission.DeleteArticles => RoleNames.Contains(Administrator) || RoleNames.Contains(SeniorEditor),
		Permission.ReviewTrustedLinkDomains => RoleNames.Contains(Administrator),
		_ => false
	};
}

/// <summary>
/// Checks permissions against the account's current roles in the database, so a role change applies on the next request
/// for both cookie and API-token sign-ins (tokens carry no role claims). Roles are read once per request.
/// </summary>
/// <param name="Context">The request's database context; <c>Roles</c> and <c>UserRoles</c> are not site-filtered.</param>
public sealed class PermissionService(WikiWikiWorldDbContext Context)
{
	/// <summary>The account whose roles are cached for this request, if any.</summary>
	private Guid? CachedUserId;

	/// <summary>The cached role names for <see cref="CachedUserId"/>.</summary>
	private IReadOnlySet<string> CachedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Decides whether the signed-in principal holds a permission.
	/// </summary>
	/// <param name="User">The request principal; anonymous principals hold nothing.</param>
	/// <param name="Permission">The permission being checked.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>True when one of the account's active roles holds the permission.</returns>
	public async Task<bool> HasPermissionAsync(ClaimsPrincipal User, Permission Permission, CancellationToken CancellationToken)
	{
		Guid? UserId = GetUserId(User);
		if (UserId is null) return false;

		return AppRoles.Grants(await GetRolesAsync(UserId.Value, CancellationToken), Permission);
	}

	/// <summary>
	/// Decides whether the principal may delete an article, by the rule <c>ArticleRevisionService.DeleteArticleAsync</c>
	/// enforces: an <c>@user</c> page only by its owner, any other page with <see cref="Permission.DeleteArticles"/>.
	/// </summary>
	/// <param name="User">The request principal.</param>
	/// <param name="UrlSlug">The article's stored slug.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>True when the delete would be allowed; used to show or hide delete controls.</returns>
	public async Task<bool> MayDeleteArticleAsync(ClaimsPrincipal User, string UrlSlug, CancellationToken CancellationToken) =>
		ArticleRevisionService.IsUserPageSlug(UrlSlug)
			? User.Identity?.IsAuthenticated == true && string.Equals(UrlSlug[1..], User.Identity.Name, StringComparison.OrdinalIgnoreCase)
			: await HasPermissionAsync(User, Permission.DeleteArticles, CancellationToken);

	/// <summary>
	/// Loads an account's active (not soft-deleted) role names, reusing the result for the rest of the request.
	/// </summary>
	/// <param name="UserId">The account identifier.</param>
	/// <param name="CancellationToken">The request cancellation token.</param>
	/// <returns>The role names, compared case-insensitively; empty for ordinary members.</returns>
	public async Task<IReadOnlySet<string>> GetRolesAsync(Guid UserId, CancellationToken CancellationToken)
	{
		if (CachedUserId == UserId) return CachedRoles;

		List<string> Names = await Context.UserRoles
			.Where(Link => Link.UserId == UserId)
			.Join(Context.Roles, Link => Link.RoleId, Role => Role.Id, (Link, Role) => Role.Name)
			.Where(Name => Name != null)
			.Select(Name => Name!)
			.ToListAsync(CancellationToken);

		CachedRoles = new HashSet<string>(Names, StringComparer.OrdinalIgnoreCase);
		CachedUserId = UserId;
		return CachedRoles;
	}

	/// <summary>
	/// Reads the account identifier that both the cookie and the API-token schemes put in the name-identifier claim.
	/// </summary>
	/// <param name="User">The request principal.</param>
	/// <returns>The identifier, or null when the principal is anonymous or the claim is not a GUID.</returns>
	public static Guid? GetUserId(ClaimsPrincipal User) =>
		User.Identity?.IsAuthenticated == true && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid Id) ? Id : null;
}

/// <summary>
/// Requires the signed-in account to hold one <see cref="Permission"/>.
/// </summary>
/// <param name="Permission">The required permission.</param>
public sealed record PermissionRequirement(Permission Permission) : IAuthorizationRequirement;

/// <summary>
/// Satisfies <see cref="PermissionRequirement"/> from the database through <see cref="PermissionService"/>. Registered as
/// scoped so it shares the request's context and role cache.
/// </summary>
/// <param name="Permissions">The request's permission service.</param>
/// <param name="HttpContextAccessor">Supplies the request-aborted token, which the authorization API does not pass.</param>
public sealed class PermissionAuthorizationHandler(PermissionService Permissions, IHttpContextAccessor HttpContextAccessor)
	: AuthorizationHandler<PermissionRequirement>
{
	/// <summary>
	/// Succeeds the requirement when the account holds the permission; otherwise leaves it unmet so access is refused.
	/// </summary>
	/// <param name="Context">The authorization context holding the principal.</param>
	/// <param name="Requirement">The permission required.</param>
	/// <returns>A task that completes when the check has run.</returns>
	protected override async Task HandleRequirementAsync(AuthorizationHandlerContext Context, PermissionRequirement Requirement)
	{
		CancellationToken CancellationToken = HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
		if (await Permissions.HasPermissionAsync(Context.User, Requirement.Permission, CancellationToken))
		{
			Context.Succeed(Requirement);
		}
	}
}
