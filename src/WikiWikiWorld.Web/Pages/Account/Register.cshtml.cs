using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Pages.Account;

public sealed class RegisterModel(
	UserManager<User> UserManager,
	SignInManager<User> SignInManager,
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	private readonly UserManager<User> UserManager = UserManager;
	private readonly SignInManager<User> SignInManager = SignInManager;

	[BindProperty]
	public required RegisterInputModel Input { get; set; }

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

		User NewUser = new() { UserName = Input.UserName, Email = Input.Email };
		IdentityResult Result = await UserManager.CreateAsync(NewUser, Input.Password);

		if (Result.Succeeded)
		{
			// Create user article for DM inbox
			await CreateUserArticleAsync(NewUser);

			await SignInManager.SignInAsync(NewUser, isPersistent: false);
			return RedirectToPage("/Index");
		}

		foreach (IdentityError Error in Result.Errors)
		{
			ModelState.AddModelError(string.Empty, Error.Description);
		}

		return Page();
	}

	/// <summary>
	/// Creates a user article for the newly registered user.
	/// </summary>
	/// <param name="NewUser">The newly created user.</param>
	private async Task CreateUserArticleAsync(User NewUser)
	{
		string UserSlug = $"@{NewUser.UserName}";

		ArticleRevision NewUserArticle = new()
		{
			CanonicalArticleId = Guid.NewGuid(),
			SiteId = SiteId,
			Culture = Culture,
			Title = UserSlug,
			DisplayTitle = $"User: {NewUser.UserName}",
			UrlSlug = UserSlug,
			Type = ArticleType.User,
			Text = $"Welcome to {NewUser.UserName}'s page.",
			RevisionReason = "User article created on registration.",
			CreatedByUserId = NewUser.Id,
			DateCreated = DateTimeOffset.UtcNow,
			IsCurrent = true
		};

		Context.ArticleRevisions.Add(NewUserArticle);
		await Context.SaveChangesAsync();
	}

	public sealed class RegisterInputModel
	{
		[Required]
		[Display(Name = "Username")]
		public required string UserName { get; set; }

		[Required]
		[EmailAddress]
		[Display(Name = "Email")]
		public required string Email { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public required string Password { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public required string ConfirmPassword { get; set; }
	}
}
