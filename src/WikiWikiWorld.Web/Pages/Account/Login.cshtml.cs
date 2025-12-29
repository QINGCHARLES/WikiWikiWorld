using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Pages.Account;

public sealed class LoginModel(
	SignInManager<User> SignInManager,
	UserManager<User> UserManager,
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	IWebHostEnvironment Environment) : BasePageModel(SiteResolverService)
{
	private readonly SignInManager<User> SignInManager = SignInManager;
	private readonly UserManager<User> UserManager = UserManager;

	[BindProperty]
	public required LoginInputModel Input { get; set; }

	public void OnGet()
	{
		ViewData["SiteId"] = SiteId;
		ViewData["Culture"] = Culture;
	}

	public async Task<IActionResult> OnPostAsync()
	{
		if (!ModelState.IsValid)
		{
			return Page();
		}

		// Find user by UserName (instead of Email)
		User? TargetUser = await UserManager.FindByNameAsync(Input.UserName);
		if (TargetUser is null)
		{
			ModelState.AddModelError(string.Empty, "Invalid username or password.");
			return Page();
		}

		// Restrict llmtest account to Development environment only
		if (!Environment.IsDevelopment() &&
			(string.Equals(TargetUser.UserName, "llmtest", StringComparison.OrdinalIgnoreCase) ||
			 string.Equals(TargetUser.Email, "llmtest@ave.nu", StringComparison.OrdinalIgnoreCase)))
		{
			ModelState.AddModelError(string.Empty, "Invalid username or password.");
			return Page();
		}

		// Authenticate user by username & password
		Microsoft.AspNetCore.Identity.SignInResult Result = await SignInManager.PasswordSignInAsync(TargetUser, Input.Password, Input.RememberMe, lockoutOnFailure: true);
		if (Result.Succeeded)
		{
			// Ensure user article exists for DM inbox
			await EnsureUserArticleExistsAsync(TargetUser);
			return RedirectToPage("/Index");
		}
		else if (Result.IsLockedOut)
		{
			ModelState.AddModelError("", "Your account is locked out.");
		}
		else
		{
			ModelState.AddModelError("", "Invalid username or password.");
		}

		return Page();
	}

	/// <summary>
	/// Ensures that a user article exists for the specified user.
	/// Creates one if it doesn't exist.
	/// </summary>
	/// <param name="TargetUser">The user to ensure an article exists for.</param>
	private async Task EnsureUserArticleExistsAsync(User TargetUser)
	{
		string UserSlug = $"@{TargetUser.UserName}";
		ArticleRevisionsBySlugSpec Spec = new(SiteId, Culture, UserSlug, IsCurrent: true);
		ArticleRevision? ExistingArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

		if (ExistingArticle is null)
		{
			ArticleRevision NewUserArticle = new()
			{
				CanonicalArticleId = Guid.NewGuid(),
				SiteId = SiteId,
				Culture = Culture,
				Title = UserSlug,
				DisplayTitle = $"User: {TargetUser.UserName}",
				UrlSlug = UserSlug,
				Type = ArticleType.User,
				Text = $"Welcome to {TargetUser.UserName}'s page.",
				RevisionReason = "User article created on login.",
				CreatedByUserId = TargetUser.Id,
				DateCreated = DateTimeOffset.UtcNow,
				IsCurrent = true
			};

			Context.ArticleRevisions.Add(NewUserArticle);
			await Context.SaveChangesAsync();
		}
	}

	public sealed class LoginInputModel
	{
		[Required]
		public required string UserName { get; set; }

		[Required]
		[DataType(DataType.Password)]
		public required string Password { get; set; }

		[Display(Name = "Remember Me")]
		public bool RememberMe { get; set; }
	}
}
