using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>Reads and replies to fixed-membership private conversations without exposing public talk routes.</summary>
/// <param name="Messaging">Authorizes and loads every conversation operation.</param>
/// <param name="UserManager">Resolves the current live account.</param>
/// <param name="SiteResolverService">Resolves request scope.</param>
[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ConversationModel(PrivateMessagingService Messaging, UserManager<User> UserManager,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>Untrusted conversation route selector.</summary>
	[BindProperty(SupportsGet = true)]
	public Guid ConversationId { get; set; }
	/// <summary>Requested chronological page, defaulting to the latest.</summary>
	[BindProperty(SupportsGet = true, Name = "pagenumber")]
	public int? PageNumber { get; set; }
	/// <summary>Reply fields accepted from the form.</summary>
	[BindProperty]
	public ReplyInput Input { get; set; } = new();
	/// <summary>Authorized conversation page, assigned only after membership succeeds.</summary>
	public PrivateConversationThread Thread { get; private set; } = null!;
	/// <summary>Account name for the viewer's inbox link.</summary>
	public string ViewerName { get; private set; } = string.Empty;

	/// <summary>Challenges unavailable accounts and loads only participant-visible content.</summary>
	/// <param name="CancellationToken">Cancels database reads.</param>
	/// <returns>The authorized thread, a challenge, or an indistinguishable 404.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		User? Viewer = await UserManager.GetUserAsync(User);
		return Viewer is null ? Challenge() : await LoadAsync(Viewer, CancellationToken);
	}

	/// <summary>Rechecks membership on every reply and redirects to the committed message.</summary>
	/// <param name="CancellationToken">Cancels persistence and error-page reads.</param>
	/// <returns>A local redirect, authorized form errors, a challenge, or 404.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		User? Viewer = await UserManager.GetUserAsync(User);
		if (Viewer is null) return Challenge();
		if (!ModelState.IsValid) return await LoadAsync(Viewer, CancellationToken);
		try
		{
			int? MessageId = await Messaging.ReplyAsync(SiteId, Culture, Viewer.Id, ConversationId, Input.Text, CancellationToken);
			return MessageId is null ? NotFound() : LocalRedirect($"/message/{ConversationId:D}#message-{MessageId}");
		}
		catch (ArgumentException Failure)
		{
			ModelState.AddModelError("Input.Text", Failure.Message);
			return await LoadAsync(Viewer, CancellationToken);
		}
	}

	/// <summary>Loads private bodies only after the service has authorized this viewer and scope.</summary>
	/// <param name="Viewer">Live authenticated account.</param>
	/// <param name="CancellationToken">Cancels database reads.</param>
	/// <returns>The initialized page or 404 without private data.</returns>
	private async Task<IActionResult> LoadAsync(User Viewer, CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		PrivateConversationThread? Loaded = await Messaging.GetThreadAsync(SiteId, Culture, Viewer.Id, ConversationId, PageNumber, CancellationToken);
		if (Loaded is null) return NotFound();
		Thread = Loaded;
		ViewerName = Viewer.UserName ?? string.Empty;
		return Page();
	}

	/// <summary>Only the reply body can be supplied by a caller.</summary>
	public sealed class ReplyInput
	{
		/// <summary>Markdown body rendered by the restricted discussion pipeline.</summary>
		[Required, StringLength(PrivateMessagingService.MaxMessageLength)]
		public string Text { get; set; } = string.Empty;
	}
}
