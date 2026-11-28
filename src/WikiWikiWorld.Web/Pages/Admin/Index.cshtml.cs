using Microsoft.AspNetCore.Authorization;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Admin;

/// <summary>Provides the entry point to the administrator workspace.</summary>
/// <param name="SiteResolverService">Resolves the current site and language for the workspace.</param>
[Authorize(Policy = nameof(Permission.UseAdminTools))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class IndexModel(SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService);
