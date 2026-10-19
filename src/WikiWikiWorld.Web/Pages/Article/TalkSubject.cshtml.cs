using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Renders a single discussion subject for an article (Reddit-style nested posts) and accepts new replies.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="MarkdownPipelineFactory">The Markdown pipeline factory; used for the talk pipeline.</param>
public sealed class TalkSubjectModel(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	UserManager<User> UserManager,
	IMarkdownPipelineFactory MarkdownPipelineFactory) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Maximum indent depth before child posts collapse inside a &lt;details&gt; element.
	/// </summary>
	public const int CollapseDepth = 5;

	/// <summary>
	/// Gets or sets the article slug captured from the URL rewrite.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string ArticleSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the subject slug captured from the URL rewrite.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string SubjectSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the reply form input.
	/// </summary>
	[BindProperty]
	public ReplyInput Input { get; set; } = new();

	/// <summary>
	/// Gets the resolved current revision of the article hosting this subject.
	/// </summary>
	public ArticleRevision? Article { get; private set; }

	/// <summary>
	/// Gets the resolved subject entity.
	/// </summary>
	public ArticleTalkSubject? Subject { get; private set; }

	/// <summary>
	/// Gets the root nodes of the post tree.
	/// </summary>
	public IReadOnlyList<PostNode> PostTree { get; private set; } = [];

	/// <summary>
	/// Gets the lookup of author <see cref="User"/> records keyed by user id.
	/// </summary>
	public IReadOnlyDictionary<Guid, User> Authors { get; private set; } = new Dictionary<Guid, User>();

	/// <summary>
	/// Gets the canonical article path (used for breadcrumbs).
	/// </summary>
	public string ArticlePath => Article is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(Article);

	/// <summary>
	/// Gets the canonical talk-list path for this article.
	/// </summary>
	public string TalkListPath => Article is null ? string.Empty : ArticleUrlHelper.BuildRelativePath(Article, "talk");

	/// <summary>
	/// Gets the canonical path of this subject thread.
	/// </summary>
	public string SubjectPath => Article is null || Subject is null
		? string.Empty
		: ArticleUrlHelper.BuildRelativePath(Article, $"talk/{Subject.UrlSlug}");

	/// <summary>
	/// Handles the GET request to render a single talk subject.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The page or a not-found result.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		IActionResult? Failure = await LoadArticleAndSubjectAsync(CancellationToken);
		if (Failure is not null)
		{
			return Failure;
		}

		await LoadPostTreeAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Handles the POST request to add a reply (top-level or nested) to this subject.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A redirect back to the subject (with a post anchor), the page with validation errors, or a challenge.</returns>
	public async Task<IActionResult> OnPostReplyAsync(CancellationToken CancellationToken)
	{
		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		IActionResult? Failure = await LoadArticleAndSubjectAsync(CancellationToken);
		if (Failure is not null)
		{
			return Failure;
		}

		if (!ModelState.IsValid)
		{
			await LoadPostTreeAsync(CancellationToken);
			return Page();
		}

		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser is null)
		{
			return Challenge();
		}

		if (Input.ParentTalkSubjectPostId is int ParentId)
		{
			bool ParentBelongsToSubject = await Context.ArticleTalkSubjectPosts
				.AsNoTracking()
				.AnyAsync(P => P.Id == ParentId
				            && P.ArticleTalkSubjectId == Subject!.Id
				            && P.DateDeleted == null, CancellationToken);

			if (!ParentBelongsToSubject)
			{
				return NotFound();
			}
		}

		ArticleTalkSubjectPost NewPost = new()
		{
			ArticleTalkSubjectId = Subject!.Id,
			ParentTalkSubjectPostId = Input.ParentTalkSubjectPostId,
			Text = Input.Text.Trim(),
			HasBeenEdited = false,
			CreatedByUserId = CurrentUser.Id,
			DateCreated = DateTimeOffset.UtcNow,
		};

		Context.ArticleTalkSubjectPosts.Add(NewPost);
		await Context.SaveChangesAsync(CancellationToken);

		string Redirected = $"{SubjectPath}#post-{NewPost.Id}";
		return Redirect(Redirected);
	}

	private async Task<IActionResult?> LoadArticleAndSubjectAsync(CancellationToken CancellationToken)
	{
		if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(ArticleSlug) || string.IsNullOrWhiteSpace(SubjectSlug))
		{
			return BadRequest("Invalid parameters.");
		}

		string LookupSlug = ArticleUrlHelper.NormalizeLookupSlug(ArticleSlug);
		ArticleRevisionsBySlugSpec ArticleSpec = new(LookupSlug, IsCurrent: true);
		Article = await Context.ArticleRevisions.WithSpecification(ArticleSpec).AsNoTracking().FirstOrDefaultAsync(CancellationToken);

		if (Article is null)
		{
			return NotFound();
		}

		ArticleTalkSubjectBySlugSpec SubjectSpec = new(Article.CanonicalArticleId, SubjectSlug);
		Subject = await Context.ArticleTalkSubjects.WithSpecification(SubjectSpec).AsNoTracking().FirstOrDefaultAsync(CancellationToken);

		if (Subject is null)
		{
			return NotFound();
		}

		return null;
	}

	private async Task LoadPostTreeAsync(CancellationToken CancellationToken)
	{
		if (Subject is null)
		{
			return;
		}

		int SubjectId = Subject.Id;
		List<ArticleTalkSubjectPost> Posts = await Context.ArticleTalkSubjectPosts
			.AsNoTracking()
			.Where(P => P.ArticleTalkSubjectId == SubjectId && P.DateDeleted == null)
			.OrderBy(P => P.DateCreated)
			.ToListAsync(CancellationToken);

		Dictionary<int, PostNode> Nodes = new(Posts.Count);
		foreach (ArticleTalkSubjectPost Post in Posts)
		{
			Nodes[Post.Id] = new PostNode
			{
				Id = Post.Id,
				ParentTalkSubjectPostId = Post.ParentTalkSubjectPostId,
				Html = MarkdownPipelineFactory.RenderTalkMarkdown(Post.Text),
				CreatedByUserId = Post.CreatedByUserId,
				DateCreated = Post.DateCreated,
				HasBeenEdited = Post.HasBeenEdited,
				DateModified = Post.DateModified,
			};
		}

		List<PostNode> Roots = [];
		foreach (PostNode Node in Nodes.Values)
		{
			if (Node.ParentTalkSubjectPostId is int ParentId && Nodes.TryGetValue(ParentId, out PostNode? Parent))
			{
				Parent.Children.Add(Node);
			}
			else
			{
				Roots.Add(Node);
			}
		}

		PostTree = Roots;

		List<Guid> AuthorIds = [.. Posts.Select(P => P.CreatedByUserId).Distinct()];
		if (Subject is not null)
		{
			AuthorIds.Add(Subject.CreatedByUserId);
		}
		AuthorIds = [.. AuthorIds.Distinct()];

		if (AuthorIds.Count == 0)
		{
			Authors = new Dictionary<Guid, User>();
			return;
		}

		UserByIdsSpec UserSpec = new(AuthorIds);
		List<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);
		Authors = Users.ToDictionary(U => U.Id);
	}

	/// <summary>
	/// Inbound form payload for a new reply.
	/// </summary>
	public sealed class ReplyInput
	{
		/// <summary>
		/// Gets or sets the parent post id, or <see langword="null"/> for a top-level reply to the subject.
		/// </summary>
		public int? ParentTalkSubjectPostId { get; set; }

		/// <summary>
		/// Gets or sets the body of the new reply.
		/// </summary>
		[Required]
		[StringLength(10_000, MinimumLength = 1)]
		public string Text { get; set; } = string.Empty;
	}

	/// <summary>
	/// In-memory node for rendering the post tree.
	/// </summary>
	public sealed class PostNode
	{
		/// <summary>Gets the post identifier.</summary>
		public int Id { get; init; }

		/// <summary>Gets the parent post identifier, or null for roots.</summary>
		public int? ParentTalkSubjectPostId { get; init; }

		/// <summary>Gets the rendered HTML body.</summary>
		public required string Html { get; init; }

		/// <summary>Gets the author user id.</summary>
		public Guid CreatedByUserId { get; init; }

		/// <summary>Gets the UTC creation timestamp.</summary>
		public DateTimeOffset DateCreated { get; init; }

		/// <summary>Gets a value indicating whether the post has been edited.</summary>
		public bool HasBeenEdited { get; init; }

		/// <summary>Gets the UTC last-modified timestamp, if edited.</summary>
		public DateTimeOffset? DateModified { get; init; }

		/// <summary>Gets the ordered list of child posts.</summary>
		public List<PostNode> Children { get; } = [];
	}
}
