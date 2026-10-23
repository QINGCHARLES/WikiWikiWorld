using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Account;

/// <summary>
/// Page model for the registration page, handling new user creation.
/// </summary>
/// <param name="UserManager">The user manager.</param>
/// <param name="SignInManager">The sign-in manager.</param>
/// <param name="ArticleRevisionService">The article revision service.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="Environment">The host environment used to reserve development-only accounts.</param>
public sealed class RegisterModel(
	UserManager<User> UserManager,
	SignInManager<User> SignInManager,
	ArticleRevisionService ArticleRevisionService,
	SiteResolverService SiteResolverService,
	IWebHostEnvironment Environment) : BasePageModel(SiteResolverService)
{
	private readonly UserManager<User> UserManager = UserManager;
	private readonly SignInManager<User> SignInManager = SignInManager;

	/// <summary>
	/// Gets or sets the registration input model.
	/// </summary>
	[BindProperty]
	public required RegisterInputModel Input { get; set; }

	/// <summary>
	/// Handles the GET request to display the registration page.
	/// </summary>
	public void OnGet()
	{
		ViewData["SiteId"] = SiteId;
		ViewData["Culture"] = Culture;
	}

	/// <summary>
	/// Registers a new user, refusing the reserved development account outside Development.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect to the home page on success, or the page with errors on failure.</returns>
	public async Task<IActionResult> OnPostAsync(CancellationToken CancellationToken)
	{
		if (!ModelState.IsValid)
		{
			return Page();
		}

		if (DevelopmentAccountPolicy.IsBlocked(Environment.EnvironmentName, Input.UserName, Input.Email))
		{
			ModelState.AddModelError(string.Empty, "This username or email is unavailable.");
			return Page();
		}

		User NewUser = new() { UserName = Input.UserName, Email = Input.Email, DateCreated = DateTimeOffset.UtcNow };
		IdentityResult Result = await UserManager.CreateAsync(NewUser, Input.Password);

		if (Result.Succeeded)
		{
			// Best-effort: ensure the user's @page (their home page) exists. A failure here must never block
			// registration — the page is recreated on demand when the profile is viewed or messaged.
			await ArticleRevisionService.TryEnsureUserArticleAsync(SiteId, Culture, NewUser, CancellationToken);

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
	/// Input model for the registration form.
	/// </summary>
	public sealed class RegisterInputModel
	{
		/// <summary>
		/// Gets or sets the username.
		/// </summary>
		[Required]
		[Display(Name = "Username")]
		public required string UserName { get; set; }

		/// <summary>
		/// Gets or sets the email address.
		/// </summary>
		[Required]
		[EmailAddress]
		[Display(Name = "Email")]
		public required string Email { get; set; }

		/// <summary>
		/// Gets or sets the password, with an eight-character minimum matching the account policy.
		/// Identity validates the required character types when creating the account.
		/// </summary>
		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public required string Password { get; set; }

		/// <summary>
		/// Gets or sets the password confirmation.
		/// </summary>
		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public required string ConfirmPassword { get; set; }
	}
}
