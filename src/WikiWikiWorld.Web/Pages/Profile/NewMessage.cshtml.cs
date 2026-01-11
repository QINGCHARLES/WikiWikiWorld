using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// Page model for composing and sending a new message.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
[Authorize]
public class NewMessageModel(
	WikiWikiWorldDbContext Context,
	UserManager<User> UserManager,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the recipient username (from query string).
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? Recipient { get; set; }

	/// <summary>
	/// Gets or sets the new message input model.
	/// </summary>
	[BindProperty]
	public required NewMessageInputModel Input { get; set; }

	/// <summary>
	/// Gets or sets the display name of the recipient.
	/// </summary>
	public string? RecipientDisplayName { get; set; }

	/// <summary>
	/// Gets or sets the path to the recipient's profile picture.
	/// </summary>
	public string? RecipientProfilePicPath { get; set; }

	/// <summary>
	/// Gets or sets the error message, if any.
	/// </summary>
	public string? ErrorMessage { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether a recipient was preset in the query string.
	/// </summary>
	public bool HasPresetRecipient { get; set; }

	/// <summary>
	/// Handles the GET request to display the new message form.
	/// </summary>
	/// <returns>The page.</returns>
	public async Task<IActionResult> OnGetAsync()
	{
		// If no recipient is specified, show the compose form with a recipient input
		if (string.IsNullOrWhiteSpace(Recipient))
		{
			HasPresetRecipient = false;
			return Page();
		}

		HasPresetRecipient = true;

		User? RecipientUser = await UserManager.FindByNameAsync(Recipient);
		if (RecipientUser is null)
		{
			ErrorMessage = "User not found.";
			HasPresetRecipient = false;
			return Page();
		}

		// Can't message yourself
		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser?.Id == RecipientUser.Id)
		{
			// Treat as "Compose New" if targeting self
			HasPresetRecipient = false;
			Recipient = string.Empty;
			return Page();
		}

		RecipientDisplayName = RecipientUser.UserName;
		if (RecipientUser.ProfilePicGuid.HasValue)
		{
			RecipientProfilePicPath = Url.Content($"~/sitefiles/{SiteId}/profilepics/{RecipientUser.ProfilePicGuid}.png");
		}

		return Page();
	}

	/// <summary>
	/// Handles the POST request to send the message.
	/// </summary>
	/// <returns>A redirect to the sent messages on success, or the page with errors.</returns>
	public async Task<IActionResult> OnPostAsync()
	{
		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser is null)
		{
			return Challenge();
		}

		// Get recipient from query string or input field
		// If query string (Recipient) is present but is the same as CurrentUser, ignore it (treat as Compose New)
		string? TargetRecipient = Recipient;
		if (!string.IsNullOrWhiteSpace(TargetRecipient))
		{
			var TargetUser = await UserManager.FindByNameAsync(TargetRecipient);
			if (TargetUser?.Id == CurrentUser.Id)
			{
				TargetRecipient = string.Empty;
			}
		}

		string RecipientUsername = !string.IsNullOrWhiteSpace(TargetRecipient) 
			? TargetRecipient 
			: Input.Recipient ?? string.Empty;

		if (string.IsNullOrWhiteSpace(RecipientUsername))
		{
			ErrorMessage = "Please specify a recipient.";
			return Page();
		}

		if (!ModelState.IsValid)
		{
			return Page();
		}

		User? RecipientUser = await UserManager.FindByNameAsync(RecipientUsername);
		if (RecipientUser is null)
		{
			ErrorMessage = "User not found.";
			return Page();
		}

		if (CurrentUser is null)
		{
			return Challenge();
		}

		// Can't message yourself
		if (CurrentUser.Id == RecipientUser.Id)
		{
			ErrorMessage = "You cannot send a message to yourself.";
			return Page();
		}

		// Get recipient's user article (to link the talk subject)
		string RecipientSlug = $"@{RecipientUsername}";
		ArticleRevisionsBySlugSpec ArticleSpec = new(RecipientSlug, IsCurrent: true);
		ArticleRevision? RecipientArticle = await Context.ArticleRevisions.WithSpecification(ArticleSpec).FirstOrDefaultAsync();

		// If recipient doesn't have a user article, create one
		if (RecipientArticle is null)
		{
			RecipientArticle = new ArticleRevision
			{
				CanonicalArticleId = Guid.NewGuid(),
				SiteId = SiteId,
				Culture = Culture,
				Title = RecipientSlug,
				DisplayTitle = $"User: {RecipientUser.UserName}",
				UrlSlug = RecipientSlug,
				Type = ArticleType.User,
				Text = $"Welcome to {RecipientUser.UserName}'s page.",
				RevisionReason = "User article created for messaging.",
				CreatedByUserId = RecipientUser.Id,
				DateCreated = DateTimeOffset.UtcNow,
				IsCurrent = true
			};
			Context.ArticleRevisions.Add(RecipientArticle);
			await Context.SaveChangesAsync();
		}

		// Create talk subject (message thread) - Id is auto-generated by SQLite
		string SubjectSlug = SlugHelper.GenerateSlug(Input.Subject);
		ArticleTalkSubject NewSubject = new()
		{
			SiteId = SiteId,
			CanonicalArticleId = RecipientArticle.CanonicalArticleId,
			Subject = Input.Subject,
			UrlSlug = $"{SubjectSlug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
			HasBeenEdited = false,
			CreatedByUserId = CurrentUser.Id,
			DateCreated = DateTimeOffset.UtcNow
		};
		Context.ArticleTalkSubjects.Add(NewSubject);
		await Context.SaveChangesAsync(); // Save to get the auto-generated Id

		// Create the first post in the thread
		ArticleTalkSubjectPost NewPost = new()
		{
			ArticleTalkSubjectId = NewSubject.Id, // Now has the auto-generated Id
			ParentTalkSubjectPostId = null,
			Text = Input.Message,
			HasBeenEdited = false,
			CreatedByUserId = CurrentUser.Id,
			DateCreated = DateTimeOffset.UtcNow
		};
		Context.ArticleTalkSubjectPosts.Add(NewPost);
		await Context.SaveChangesAsync();

		// Redirect to sender's inbox with success message
		return LocalRedirect($"/@{CurrentUser.UserName}/messages");
	}

	/// <summary>
	/// AJAX handler for searching users by username.
	/// </summary>
	/// <param name="term">The search term.</param>
	/// <returns>A JSON list of matching users.</returns>
	public async Task<IActionResult> OnGetSearchUsersAsync(string term)
	{
		if (string.IsNullOrWhiteSpace(term))
		{
			return new JsonResult(Array.Empty<object>());
		}

		var Users = await Context.Users
			.Where(u => u.UserName!.ToLower().Contains(term.ToLower()))
			.OrderBy(u => u.UserName)
			.Take(10)
			.Select(u => new 
			{ 
				u.UserName, 
				u.ProfilePicGuid 
			})
			.ToListAsync();

		return new JsonResult(Users);
	}

	/// <summary>
	/// Input model for the new message form.
	/// </summary>
	public sealed class NewMessageInputModel
	{
		/// <summary>
		/// Gets or sets the recipient username.
		/// </summary>
		[Display(Name = "To")]
		public string? Recipient { get; set; }

		/// <summary>
		/// Gets or sets the subject of the message.
		/// </summary>
		[Required(ErrorMessage = "Subject is required.")]
		[StringLength(200, ErrorMessage = "Subject must be 200 characters or less.")]
		[Display(Name = "Subject")]
		public required string Subject { get; set; }

		/// <summary>
		/// Gets or sets the body of the message.
		/// </summary>
		[Required(ErrorMessage = "Message is required.")]
		[StringLength(10000, ErrorMessage = "Message must be 10,000 characters or less.")]
		[Display(Name = "Message")]
		public required string Message { get; set; }
	}
}
