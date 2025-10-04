namespace WikiWikiWorld.Web.Pages.Article;

public sealed class FirehoseModel(IArticleRevisionRepository ArticleRevisionRepository, IUserRepository UserRepository, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	public IReadOnlyList<ArticleRevision> RecentRevisions { get; private set; } = [];
	public Dictionary<Guid, User> Users { get; private set; } = [];

	public async Task<IActionResult> OnGetAsync()
	{
		RecentRevisions = await ArticleRevisionRepository.GetRecentRevisionsAsync(SiteId, Culture);

		// Fetch user objects for all unique user IDs
		IEnumerable<Guid> UniqueUserIds = RecentRevisions.Select(Rev => Rev.CreatedByUserId).Distinct();
		IEnumerable<Task<User?>> UserTasks = UniqueUserIds.Select(UserId => GetUserAsync(UserId));
		User?[] UserResults = await Task.WhenAll(UserTasks);

		// Populate dictionary with UserId -> User mappings
		Users = UniqueUserIds.Zip(UserResults, (UserId, UserObj) => new { UserId, UserObj })
							 .Where(x => x.UserObj is not null)
							 .ToDictionary(x => x.UserId, x => x.UserObj!);

		return Page();
	}

	private async Task<User?> GetUserAsync(Guid UserId)
	{
		return await UserRepository.GetByIdAsync(UserId);
	}
}
