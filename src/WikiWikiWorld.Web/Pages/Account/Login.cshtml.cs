using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Pages.Account;

/// <summary>
/// Page model for the login page, handling user authentication.
/// </summary>
/// <param name="SignInManager">The sign-in manager.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="Environment">The web host environment.</param>
public sealed class LoginModel(
	SignInManager<User> SignInManager,
	UserManager<User> UserManager,
	SiteResolverService SiteResolverService,
	IWebHostEnvironment Environment) : BasePageModel(SiteResolverService)
{
	private readonly SignInManager<User> SignInManager = SignInManager;
	private readonly UserManager<User> UserManager = UserManager;

	/// <summary>
	/// Gets or sets the login input model.
	/// </summary>
	[BindProperty]
	public required LoginInputModel Input { get; set; }

	/// <summary>
	/// Gets or sets the URL to redirect to after a successful login.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? ReturnUrl { get; set; }

	/// <summary>
	/// Handles the GET request to display the login page.
	/// </summary>
	/// <param name="ReturnUrl">The URL to redirect to after login.</param>
	public void OnGet(string? ReturnUrl = null)
	{
		this.ReturnUrl = ReturnUrl;
		ViewData["SiteId"] = SiteId;
		ViewData["Culture"] = Culture;
	}

	/// <summary>
	/// Handles the POST request to authenticate the user.
	/// </summary>
	/// <returns>A redirect to <see cref="ReturnUrl"/> (or the home page) on success, or the page with errors on failure.</returns>
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

		// Restrict DevvyTheBot and its legacy aliases to Development.
		if (DevelopmentAccountPolicy.IsBlocked(Environment.EnvironmentName, TargetUser.UserName, TargetUser.Email))
		{
			ModelState.AddModelError(string.Empty, "Invalid username or password.");
			return Page();
		}

		// Authenticate user by username & password
		Microsoft.AspNetCore.Identity.SignInResult Result = await SignInManager.PasswordSignInAsync(TargetUser, Input.Password, Input.RememberMe, lockoutOnFailure: true);

		if (Result.Succeeded)
		{
			// The user's @page (their home page) is no longer ensured here: it exists from registration and is
			// otherwise created on demand when the profile is viewed or messaged. Keeping login off this
			// write avoids turning a successful sign-in into a 500 if the write were to fail.
			if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
				return LocalRedirect(ReturnUrl);
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
	/// Input model for the login form.
	/// </summary>
	public sealed class LoginInputModel
	{
		/// <summary>
		/// Gets or sets the username.
		/// </summary>
		[Required]
		[Display(Name = "Username")]
		public required string UserName { get; set; }

		/// <summary>
		/// Gets or sets the password.
		/// </summary>
		[Required]
		[DataType(DataType.Password)]
		public required string Password { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether to remember the user's login session.
		/// </summary>
		[Display(Name = "Remember me")]
		public bool RememberMe { get; set; }
	}
}
