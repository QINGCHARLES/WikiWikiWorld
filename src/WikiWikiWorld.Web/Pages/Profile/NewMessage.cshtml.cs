using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>Composes a private conversation with fixed membership, independently of public user articles.</summary>
/// <param name="Messaging">Enforces membership and atomically saves conversations.</param>
/// <param name="UserManager">Resolves the authenticated account and optional recipient.</param>
/// <param name="Context">Queries recipient suggestions.</param>
/// <param name="SiteResolverService">Resolves the request's site and culture.</param>
[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class NewMessageModel(PrivateMessagingService Messaging, UserManager<User> UserManager,
	WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>Optional profile-route prefill; submitted recipients remain editable.</summary>
	[BindProperty(SupportsGet = true)]
	public string? Recipient { get; set; }
	/// <summary>Gets or sets only the conversation fields accepted from the form.</summary>
	[BindProperty]
	public NewMessageInputModel Input { get; set; } = new();
	/// <summary>Gets the account name displayed beside a successful profile prefill.</summary>
	public string? RecipientDisplayName { get; private set; }
	/// <summary>Gets the optional avatar URL for a profile prefill.</summary>
	public string? RecipientProfilePicPath { get; private set; }
	/// <summary>Gets whether the initial form has a resolved profile prefill.</summary>
	public bool HasPresetRecipient { get; private set; }
	/// <summary>Gets an actionable error without discarding the submitted draft.</summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>Loads the composer and optionally prefills a recipient without creating a user article.</summary>
	/// <param name="CancellationToken">Cancels request work.</param>
	/// <returns>The form, or an authentication challenge for an unavailable account.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		User? Sender = await UserManager.GetUserAsync(User);
		if (Sender is null) return Challenge();
		CancellationToken.ThrowIfCancellationRequested();
		if (!string.IsNullOrWhiteSpace(Recipient))
		{
			User? Account = await UserManager.FindByNameAsync(Recipient.Trim().TrimStart('@'));
			if (Account is null) ErrorMessage = "That recipient was not found. Choose another account below.";
			else if (Account.Id != Sender.Id)
			{
				HasPresetRecipient = true;
				RecipientDisplayName = Account.UserName;
				RecipientProfilePicPath = Account.ProfilePicGuid is Guid Picture ? $"/sitefiles/{SiteId}/profilepics/{Picture}.png" : null;
				Input.Recipients = Account.UserName ?? string.Empty;
			}
		}
		return Page();
	}

	/// <summary>Starts a conversation for the authenticated sender, retaining invalid drafts.</summary>
	/// <param name="CancellationToken">Cancels lookups and persistence.</param>
	/// <returns>A local conversation redirect after commit, or the form with its validation errors.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		AllowSearchEngineIndexingOfPage = false;
		User? Sender = await UserManager.GetUserAsync(User);
		if (Sender is null) return Challenge();
		if (!ModelState.IsValid) return Page();
		try
		{
			Guid Id = await Messaging.CreateAsync(SiteId, Culture, Sender.Id, Input.Recipients, Input.Subject, Input.Message, CancellationToken);
			return LocalRedirect($"/message/{Id:D}");
		}
		catch (ArgumentException Failure)
		{
			ErrorMessage = Failure.Message;
			return Page();
		}
	}

	/// <summary>Suggests up to ten public account names for the final comma-separated recipient token.</summary>
	/// <param name="term">Typed username fragment.</param>
	/// <param name="CancellationToken">Cancels the bounded database query.</param>
	/// <returns>Public name/avatar suggestions for an authenticated account.</returns>
	public async Task<IActionResult> OnGetSearchUsersAsync(string? term, CancellationToken CancellationToken)
	{
		if (await UserManager.GetUserAsync(User) is null) return Challenge();
		if (string.IsNullOrWhiteSpace(term) || term.Length > 256) return new JsonResult(Array.Empty<object>());
		string Normalized = UserManager.NormalizeName(term.Trim().TrimStart('@'))!;
		var Matches = await Context.Users.AsNoTracking().Where(u => u.NormalizedUserName != null && u.NormalizedUserName.Contains(Normalized))
			.OrderBy(u => u.UserName).Take(10).Select(u => new { userName = u.UserName, profilePicGuid = u.ProfilePicGuid }).ToListAsync(CancellationToken);
		return new JsonResult(Matches);
	}

	/// <summary>Fields that can be submitted when starting a fixed-membership conversation.</summary>
	public sealed class NewMessageInputModel
	{
		/// <summary>Comma-separated usernames; the sender is included automatically.</summary>
		[Required, StringLength(2000)]
		public string Recipients { get; set; } = string.Empty;
		/// <summary>Plain-text title visible only to participants.</summary>
		[Required, StringLength(PrivateMessagingService.MaxSubjectLength)]
		public string Subject { get; set; } = string.Empty;
		/// <summary>Initial body, rendered with the restricted discussion Markdown pipeline.</summary>
		[Required, StringLength(PrivateMessagingService.MaxMessageLength)]
		public string Message { get; set; } = string.Empty;
	}
}
