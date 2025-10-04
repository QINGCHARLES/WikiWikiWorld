﻿using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace WikiWikiWorld.Web.Pages.Account;

public sealed class LoginModel(SignInManager<ApplicationUser> SignInManager, UserManager<ApplicationUser> UserManager, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	private readonly SignInManager<ApplicationUser> SignInManager = SignInManager;
	private readonly UserManager<ApplicationUser> UserManager = UserManager;

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

		// ✅ Find user by UserName (instead of Email)
		ApplicationUser? User = await UserManager.FindByNameAsync(Input.UserName);
		if (User is null)
		{
			ModelState.AddModelError(string.Empty, "Invalid username or password.");
			return Page();
		}

		// ✅ Authenticate user by username & password
		Microsoft.AspNetCore.Identity.SignInResult Result = await SignInManager.PasswordSignInAsync(User, Input.Password, Input.RememberMe, lockoutOnFailure: true);
		if (Result.Succeeded)
		{
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

	public sealed class LoginInputModel
	{
		[Required]
		public required string UserName { get; set; } // ✅ Changed from Email to Username

		[Required]
		[DataType(DataType.Password)]
		public required string Password { get; set; }

		[Display(Name = "Remember Me")]
		public bool RememberMe { get; set; }
	}
}
