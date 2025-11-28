using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Pages.Account;

public sealed class RegisterModel(UserManager<User> UserManager, SignInManager<User> SignInManager, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
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

		var User = new User { UserName = Input.UserName, Email = Input.Email };
		var Result = await UserManager.CreateAsync(User, Input.Password);

		if (Result.Succeeded)
		{
			await SignInManager.SignInAsync(User, isPersistent: false);
			return RedirectToPage("/Index");
		}

		foreach (var Error in Result.Errors)
		{
			ModelState.AddModelError(string.Empty, Error.Description);
		}

		return Page();
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
