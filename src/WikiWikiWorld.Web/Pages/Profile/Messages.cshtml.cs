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
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="ProfileService">Loads the shared profile header.</param>
public sealed class MessagesModel(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	ProfileService ProfileService) : BasePageModel(SiteResolverService)
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
	/// Gets the shared profile header; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileHeader Header { get; private set; } = null!;

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
	/// Handles the GET request to view messages. Only the signed-in owner may read their messages.
	/// </summary>
	/// <param name="CancellationToken">The request-aborted token.</param>
	/// <returns>The page, 404 for an unknown user, a login challenge for anonymous visitors, or Forbid for other users.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		ProfileHeader? LoadedHeader = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
		if (LoadedHeader is null)
		{
			return NotFound();
		}

		if (!LoadedHeader.IsViewerAuthenticated)
		{
			return Challenge();
		}

		// Only allow viewing own messages
		if (!LoadedHeader.IsOwner)
		{
			return Forbid();
		}

		Header = LoadedHeader;
		AllowSearchEngineIndexingOfPage = false;

		// Get user's article to find inbox messages
		string UserSlug = $"@{Header.UserName}";
		ArticleRevisionsBySlugSpec ArticleSpec = new(UserSlug, IsCurrent: true);
		ArticleRevision? UserArticle = await Context.ArticleRevisions.WithSpecification(ArticleSpec).FirstOrDefaultAsync();

		if (UserArticle is not null)
		{
			CanonicalArticleId = UserArticle.CanonicalArticleId;

			// Get inbox messages (messages TO this user)
			ArticleTalkSubjectsByCanonicalIdSpec InboxSpec = new(UserArticle.CanonicalArticleId);
			InboxMessages = await Context.ArticleTalkSubjects.WithSpecification(InboxSpec).ToListAsync();
			InboxCount = InboxMessages.Count;
		}

		// Get sent messages (messages FROM this user)
		ArticleTalkSubjectsByCreatorSpec SentSpec = new(Header.UserId);
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

