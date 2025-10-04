using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WikiWikiWorld.Web.Configuration
{
	public sealed class SiteConfiguration
	{
		public List<SiteInfo> Sites { get; set; } = [];
	}

	public sealed class SiteInfo
	{
		public int SiteId { get; set; }
		public string? DefaultCulture { get; set; } = null;
		public List<string> Domains { get; set; } = [];
	}
}

namespace WikiWikiWorld.Web.Pages
{
	public abstract class BasePageModel : PageModel
	{
		private readonly SiteResolverService SiteResolverService;

		protected BasePageModel(SiteResolverService SiteResolverService)
		{
			this.SiteResolverService = SiteResolverService;
		}

		public int SiteId { get; private set; }
		public string Culture { get; private set; } = string.Empty;
		public string MetaDescription { get; set; } = string.Empty;
		public bool AllowSearchEngineIndexingOfPage { get; set; } = true;
		public string? HeaderImage { get; set; } // Added property

		public override void OnPageHandlerExecuting(PageHandlerExecutingContext Context)
		{
			base.OnPageHandlerExecuting(Context);

			(this.SiteId, this.Culture) = this.SiteResolverService.ResolveSiteAndCulture();
		}
	}
}