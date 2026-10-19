using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Infrastructure;
using WikiWikiWorld.Web.Pages;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Lists every discussion subject for a single article and lets signed-in users start a new one.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="UserManager">The user manager.</param>
public sealed class TalkSubjectsModel(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	UserManager<User> UserManager) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the article slug captured from the URL rewrite.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string ArticleSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the new-subject form input.
	/// </summary>
	[BindProperty]
	public NewSubjectInput Input { get; set; } = new();

	/// <summary>
	/// Gets the resolved current revision of the article hosting these subjects.
	/// </summary>
	public ArticleRevision? Article { get; private set; }

	/// <summary>
	/// Gets the discussion subjects for the article, newest first.
	/// </summary>
	public IReadOnlyList<SubjectRow> Subjects { get; private set; } = [];

	/// <summary>
	/// Gets the lookup of author <see cref="User"/> records keyed by user id.
	/// </summary>
	public IReadOnlyDictionary<Guid, User> Authors { get; private set; } = new Dictionary<Guid, User>();

	/// <summary>
	/// Gets the canonical article path (used for breadcrumbs).
	/// </summary>
	public string ArticlePath => Article is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(Article);

	/// <summary>
	/// Gets the canonical path of the talk list page itself.
	/// </summary>
	public string TalkPath => Article is null ? string.Empty : ArticleUrlHelper.BuildRelativePath(Article, "talk");

	/// <summary>
	/// Handles the GET request to list discussion subjects.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The page or a not-found result.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		IActionResult? Failure = await LoadArticleAsync(CancellationToken);
		if (Failure is not null)
		{
			return Failure;
		}

		await LoadSubjectsAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Handles the POST request for creating a new discussion subject (with an initial root post).
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A redirect to the new subject thread, the same page with validation errors, or a challenge.</returns>
	public async Task<IActionResult> OnPostNewSubjectAsync(CancellationToken CancellationToken)
	{
		if (!User.Identity?.IsAuthenticated ?? true)
		{
			return Challenge();
		}

		IActionResult? Failure = await LoadArticleAsync(CancellationToken);
		if (Failure is not null)
		{
			return Failure;
		}

		if (!ModelState.IsValid)
		{
			await LoadSubjectsAsync(CancellationToken);
			return Page();
		}

		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser is null)
		{
			return Challenge();
		}

		string SubjectSlug = await GenerateUniqueSubjectSlugAsync(Article!.CanonicalArticleId, Input.Subject, CancellationToken);
		DateTimeOffset Now = DateTimeOffset.UtcNow;

		ArticleTalkSubject NewSubject = new()
		{
			SiteId = SiteId,
			CanonicalArticleId = Article!.CanonicalArticleId,
			Subject = Input.Subject.Trim(),
			UrlSlug = SubjectSlug,
			HasBeenEdited = false,
			CreatedByUserId = CurrentUser.Id,
			DateCreated = Now,
		};

		Context.ArticleTalkSubjects.Add(NewSubject);
		await Context.SaveChangesAsync(CancellationToken);

		ArticleTalkSubjectPost RootPost = new()
		{
			ArticleTalkSubjectId = NewSubject.Id,
			ParentTalkSubjectPostId = null,
			Text = Input.Text.Trim(),
			HasBeenEdited = false,
			CreatedByUserId = CurrentUser.Id,
			DateCreated = Now,
		};
		Context.ArticleTalkSubjectPosts.Add(RootPost);
		await Context.SaveChangesAsync(CancellationToken);

		return Redirect(ArticleUrlHelper.BuildRelativePath(Article!, $"talk/{SubjectSlug}"));
	}

	private async Task<IActionResult?> LoadArticleAsync(CancellationToken CancellationToken)
	{
		if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(ArticleSlug))
		{
			return BadRequest("Invalid parameters.");
		}

		string LookupSlug = ArticleUrlHelper.NormalizeLookupSlug(ArticleSlug);
		ArticleRevisionsBySlugSpec Spec = new(LookupSlug, IsCurrent: true);
		Article = await Context.ArticleRevisions.WithSpecification(Spec).AsNoTracking().FirstOrDefaultAsync(CancellationToken);

		if (Article is null)
		{
			return NotFound();
		}

		return null;
	}

	private async Task LoadSubjectsAsync(CancellationToken CancellationToken)
	{
		if (Article is null)
		{
			return;
		}

		Guid CanonicalArticleId = Article.CanonicalArticleId;

		List<SubjectRow> Rows = await Context.ArticleTalkSubjects
			.AsNoTracking()
			.Where(s => s.CanonicalArticleId == CanonicalArticleId && s.DateDeleted == null)
			.OrderByDescending(s => s.DateCreated)
			.Select(s => new SubjectRow
			{
				Id = s.Id,
				Subject = s.Subject,
				UrlSlug = s.UrlSlug,
				CreatedByUserId = s.CreatedByUserId,
				DateCreated = s.DateCreated,
				PostCount = Context.ArticleTalkSubjectPosts
					.Count(p => p.ArticleTalkSubjectId == s.Id && p.DateDeleted == null),
			})
			.ToListAsync(CancellationToken);

		Subjects = Rows;

		List<Guid> AuthorIds = [.. Rows.Select(r => r.CreatedByUserId).Distinct()];
		if (AuthorIds.Count == 0)
		{
			Authors = new Dictionary<Guid, User>();
			return;
		}

		UserByIdsSpec UserSpec = new(AuthorIds);
		List<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync(CancellationToken);
		Authors = Users.ToDictionary(U => U.Id);
	}

	private async Task<string> GenerateUniqueSubjectSlugAsync(Guid CanonicalArticleId, string Subject, CancellationToken CancellationToken)
	{
		string BaseSlug = SlugHelper.GenerateSlug(Subject);
		if (string.IsNullOrWhiteSpace(BaseSlug))
		{
			BaseSlug = "subject";
		}

		HashSet<string> Existing = [.. await Context.ArticleTalkSubjects
			.AsNoTracking()
			.Where(s => s.CanonicalArticleId == CanonicalArticleId && s.UrlSlug.StartsWith(BaseSlug))
			.Select(s => s.UrlSlug)
			.ToListAsync(CancellationToken)];

		if (!Existing.Contains(BaseSlug))
		{
			return BaseSlug;
		}

		for (int Suffix = 2; Suffix < int.MaxValue; Suffix++)
		{
			string Candidate = $"{BaseSlug}-{Suffix}";
			if (!Existing.Contains(Candidate))
			{
				return Candidate;
			}
		}

		throw new InvalidOperationException("Unable to generate a unique subject slug.");
	}

	/// <summary>
	/// Inbound form payload for creating a new talk subject.
	/// </summary>
	public sealed class NewSubjectInput
	{
		/// <summary>
		/// Gets or sets the human-readable subject title.
		/// </summary>
		[Required]
		[StringLength(150, MinimumLength = 1)]
		public string Subject { get; set; } = string.Empty;

		/// <summary>
		/// Gets or sets the body of the initial (root) post.
		/// </summary>
		[Required]
		[StringLength(10_000, MinimumLength = 1)]
		public string Text { get; set; } = string.Empty;
	}

	/// <summary>
	/// Projection used to render one row in the subject list.
	/// </summary>
	public sealed record class SubjectRow
	{
		/// <summary>Gets the subject database identifier.</summary>
		public int Id { get; init; }

		/// <summary>Gets the displayed subject title.</summary>
		public required string Subject { get; init; }

		/// <summary>Gets the URL slug used to open the subject thread.</summary>
		public required string UrlSlug { get; init; }

		/// <summary>Gets the user identifier of the subject's creator.</summary>
		public Guid CreatedByUserId { get; init; }

		/// <summary>Gets the UTC creation timestamp.</summary>
		public DateTimeOffset DateCreated { get; init; }

		/// <summary>Gets the count of non-deleted posts inside the subject.</summary>
		public int PostCount { get; init; }
	}
}
