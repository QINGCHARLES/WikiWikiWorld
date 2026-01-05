using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// Page model for viewing a user's messages (inbox and sent).
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public class MessagesModel(
	WikiWikiWorldDbContext Context,
	UserManager<User> UserManager,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the username to view messages for.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string Username { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the active tab (inbox or sent).
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string Tab { get; set; } = "inbox";

	/// <summary>
	/// Gets or sets the path to the user's profile picture.
	/// </summary>
	public string? ProfilePicPath { get; set; }

	/// <summary>
	/// Gets or sets the date the user joined.
	/// </summary>
	public DateTimeOffset DateJoined { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the current user is viewing their own profile.
	/// </summary>
	public bool IsViewingOwnProfile { get; set; }

	/// <summary>
	/// Gets or sets the canonical article ID for the user's profile/inbox.
	/// </summary>
	public Guid? CanonicalArticleId { get; set; }

	/// <summary>
	/// Gets or sets the list of messages in the inbox.
	/// </summary>
	public IReadOnlyList<ArticleTalkSubject> InboxMessages { get; set; } = [];

	/// <summary>
	/// Gets or sets the list of sent messages.
	/// </summary>
	public IReadOnlyList<ArticleTalkSubject> SentMessages { get; set; } = [];

	/// <summary>
	/// Gets or sets the dictionary of users involved in the messages.
	/// </summary>
	public Dictionary<Guid, User> MessageUsers { get; set; } = [];

	/// <summary>
	/// Gets or sets the count of inbox messages.
	/// </summary>
	public int InboxCount { get; set; }

	/// <summary>
	/// Gets or sets the count of sent messages.
	/// </summary>
	public int SentCount { get; set; }

	/// <summary>
	/// Gets or sets the dictionary of recipient usernames for sent messages.
	/// </summary>
	public Dictionary<Guid, string> RecipientUsernames { get; set; } = [];

	/// <summary>
	/// Handles the GET request to view messages.
	/// </summary>
	/// <returns>The page or NotFound/Forbid.</returns>
	public async Task<IActionResult> OnGetAsync()
	{
		if (string.IsNullOrWhiteSpace(Username))
		{
			return NotFound();
		}

		User? TargetUser = await UserManager.FindByNameAsync(Username);
		if (TargetUser is null)
		{
			return NotFound();
		}

		DateJoined = TargetUser.DateCreated;

		if (!string.IsNullOrWhiteSpace(TargetUser.ProfilePicGuid))
		{
			ProfilePicPath = Url.Content($"~/sitefiles/{SiteId}/profilepics/{TargetUser.ProfilePicGuid}.png");
		}

		// Check if viewing own profile
		User? CurrentUser = await UserManager.GetUserAsync(User);
		IsViewingOwnProfile = CurrentUser?.Id == TargetUser.Id;

		// Only allow viewing own messages
		if (!IsViewingOwnProfile)
		{
			return Forbid();
		}

		// Get user's article to find inbox messages
		string UserSlug = $"@{Username}";
		ArticleRevisionsBySlugSpec ArticleSpec = new(SiteId, Culture, UserSlug, IsCurrent: true);
		ArticleRevision? UserArticle = await Context.ArticleRevisions.WithSpecification(ArticleSpec).FirstOrDefaultAsync();

		if (UserArticle is not null)
		{
			CanonicalArticleId = UserArticle.CanonicalArticleId;

			// Get inbox messages (messages TO this user)
			ArticleTalkSubjectsByCanonicalIdSpec InboxSpec = new(SiteId, UserArticle.CanonicalArticleId);
			InboxMessages = await Context.ArticleTalkSubjects.WithSpecification(InboxSpec).ToListAsync();
			InboxCount = InboxMessages.Count;
		}

		// Get sent messages (messages FROM this user)
		ArticleTalkSubjectsByCreatorSpec SentSpec = new(SiteId, TargetUser.Id);
		SentMessages = await Context.ArticleTalkSubjects.WithSpecification(SentSpec).ToListAsync();
		SentCount = SentMessages.Count;

		// Get user info for message senders/recipients
		List<Guid> UserIds = InboxMessages.Select(m => m.CreatedByUserId).Distinct().ToList();

		// For sent messages, we need to find who owns the wall (Article) the message was posted to
		if (SentMessages.Any())
		{
			List<Guid> SentArticleIds = SentMessages.Select(m => m.CanonicalArticleId).Distinct().ToList();
			ArticleRevisionsByCanonicalIdsSpec SentArticleSpec = new(SentArticleIds);
			IReadOnlyList<ArticleRevision> RecipientArticles = await Context.ArticleRevisions.WithSpecification(SentArticleSpec).ToListAsync();
			
			// Map ArticleId -> Username (from Title, e.g. "@username")
			foreach (var Article in RecipientArticles)
			{
				if (Article.Title.StartsWith('@'))
				{
					RecipientUsernames[Article.CanonicalArticleId] = Article.Title;
				}
			}
		}

		if (UserIds.Count > 0)
		{
			UserByIdsSpec UserSpec = new(UserIds);
			IReadOnlyList<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync();
			MessageUsers = Users.ToDictionary(u => u.Id, u => u);
		}

		return Page();
	}
}

