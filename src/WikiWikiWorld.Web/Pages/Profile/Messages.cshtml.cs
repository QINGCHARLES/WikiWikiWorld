using Microsoft.AspNetCore.Authorization;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>Displays only the owner's private inbox or conversations they started.</summary>
/// <param name="Messaging">Loads member-only folder summaries.</param>
/// <param name="ProfileService">Resolves the profile and ownership.</param>
/// <param name="SiteResolverService">Resolves request scope.</param>
[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class MessagesModel(PrivateMessagingService Messaging, ProfileService ProfileService,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>Profile route whose owner must match the viewer.</summary>
	[BindProperty(SupportsGet = true)]
	public string Username { get; set; } = string.Empty;
	/// <summary>Inbox or sent; sent means conversations started by the viewer.</summary>
	[BindProperty(SupportsGet = true)]
	public string Tab { get; set; } = "inbox";
	/// <summary>Requested one-based folder page.</summary>
	[BindProperty(SupportsGet = true, Name = "pagenumber")]
	public int PageNumber { get; set; } = 1;
	/// <summary>Shared public header for the authenticated owner.</summary>
	public ProfileHeader Header { get; private set; } = null!;
	/// <summary>Bounded, authorized summaries with no message bodies.</summary>
	public PrivateConversationFolder Folder { get; private set; } = new([], 1, 1);

	/// <summary>Checks ownership before querying private conversation summaries.</summary>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>The owner's folder, an authentication challenge, or a refusal for another profile.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		if (User.Identity?.IsAuthenticated != true) return Challenge();
		ProfileHeader? Loaded = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
		if (Loaded is null) return NotFound();
		if (!Loaded.IsOwner) return Forbid();
		if (Tab is not ("inbox" or "sent")) return NotFound();
		Header = Loaded;
		Folder = await Messaging.GetFolderAsync(SiteId, Culture, Header.UserId, Tab == "sent", PageNumber, CancellationToken);
		return Page();
	}
}
