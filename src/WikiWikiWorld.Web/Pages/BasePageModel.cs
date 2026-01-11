using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WikiWikiWorld.Web.Configuration
{
	/// <summary>
	/// Represents the site configuration settings.
	/// </summary>
	public sealed class SiteConfiguration
	{
		/// <summary>
		/// Gets or sets the list of configured sites.
		/// </summary>
		public List<SiteInfo> Sites { get; set; } = [];
	}

	/// <summary>
	/// Represents configuration information for a specific site.
	/// </summary>
	public sealed class SiteInfo
	{
		/// <summary>
		/// Gets or sets the unique identifier for the site.
		/// </summary>
		public int SiteId { get; set; }

		/// <summary>
		/// Gets or sets the default culture code for the site.
		/// Required for single-culture sites where the root domain serves content directly.
		/// Optional for multi-culture sites with <see cref="RootDomainIsCultureSelectorOnly"/> set to true.
		/// </summary>
		public string? DefaultCulture { get; set; } = null;

		/// <summary>
		/// Gets or sets whether the root domain only displays a culture selector page.
		/// When true, content is served from culture subdomains (e.g., en.site.com), and
		/// the root domain (site.com) shows a culture selection page with minimal sitemap/robots.txt.
		/// When false (default), the root domain serves wiki content directly using <see cref="DefaultCulture"/>.
		/// </summary>
		public bool RootDomainIsCultureSelectorOnly { get; set; } = false;

		/// <summary>
		/// Gets or sets the list of domains associated with the site.
		/// </summary>
		public List<string> Domains { get; set; } = [];
	}
}

namespace WikiWikiWorld.Web.Pages
{
	/// <summary>
	/// Base class for all page models, providing common functionality for site context and metadata.
	/// </summary>
	public abstract class BasePageModel : PageModel
	{
		private readonly SiteResolverService SiteResolverService;

		/// <summary>
		/// Initializes a new instance of the <see cref="BasePageModel"/> class.
		/// </summary>
		/// <param name="SiteResolverService">The service used to resolve site context.</param>
		protected BasePageModel(SiteResolverService SiteResolverService)
		{
			this.SiteResolverService = SiteResolverService;
		}

		/// <summary>
		/// Gets the current Site ID resolved from the request.
		/// </summary>
		public int SiteId { get; private set; }

		/// <summary>
		/// Gets the current Culture resolved from the request.
		/// </summary>
		public string Culture { get; private set; } = string.Empty;

		/// <summary>
		/// Gets or sets the meta description for the page.
		/// </summary>
		public string MetaDescription { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets a value indicating whether search engines should index this page.
		/// </summary>
		public bool AllowSearchEngineIndexingOfPage { get; set; } = true;

		/// <summary>
		/// Gets or sets the URL of the header image for the page.
		/// </summary>
		public string? HeaderImage { get; set; } // Added property

		/// <inheritdoc/>
		public override void OnPageHandlerExecuting(PageHandlerExecutingContext Context)
		{
			base.OnPageHandlerExecuting(Context);

			(this.SiteId, this.Culture) = this.SiteResolverService.ResolveSiteAndCulture();
		}
	}
}