using Microsoft.AspNetCore.Identity;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Identity error messages in the request's UI language, for the registration and sign-in forms.
/// </summary>
/// <remarks>
/// English pages get ASP.NET Core Identity's own wording, unchanged. Other languages look up the same English
/// sentence in <see cref="SharedResource"/>. Only errors a person can trigger from the site's forms are covered;
/// the rest keep Identity's English text.
/// </remarks>
public sealed class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
	// Translation keys are passed to Translate below; listed for scripts/localization.py:
	// l10n: "An unknown failure has occurred.", "Incorrect password.", "Username '{0}' is invalid, can only contain letters or digits.", "Email '{0}' is invalid.", "Username '{0}' is already taken.", "Email '{0}' is already taken."
	// l10n: "Passwords must be at least {0} characters.", "Passwords must use at least {0} different characters.", "Passwords must have at least one non alphanumeric character.", "Passwords must have at least one digit ('0'-'9').", "Passwords must have at least one lowercase ('a'-'z').", "Passwords must have at least one uppercase ('A'-'Z')."
	/// <inheritdoc/>
	public override IdentityError DefaultError() => Translate(base.DefaultError(), "An unknown failure has occurred.");

	/// <inheritdoc/>
	public override IdentityError PasswordMismatch() => Translate(base.PasswordMismatch(), "Incorrect password.");

	/// <inheritdoc/>
	public override IdentityError InvalidUserName(string? UserName) =>
		Translate(base.InvalidUserName(UserName), "Username '{0}' is invalid, can only contain letters or digits.", UserName);

	/// <inheritdoc/>
	public override IdentityError InvalidEmail(string? Email) => Translate(base.InvalidEmail(Email), "Email '{0}' is invalid.", Email);

	/// <inheritdoc/>
	public override IdentityError DuplicateUserName(string UserName) =>
		Translate(base.DuplicateUserName(UserName), "Username '{0}' is already taken.", UserName);

	/// <inheritdoc/>
	public override IdentityError DuplicateEmail(string Email) => Translate(base.DuplicateEmail(Email), "Email '{0}' is already taken.", Email);

	/// <inheritdoc/>
	public override IdentityError PasswordTooShort(int Length) =>
		Translate(base.PasswordTooShort(Length), "Passwords must be at least {0} characters.", Length);

	/// <inheritdoc/>
	public override IdentityError PasswordRequiresUniqueChars(int UniqueChars) =>
		Translate(base.PasswordRequiresUniqueChars(UniqueChars), "Passwords must use at least {0} different characters.", UniqueChars);

	/// <inheritdoc/>
	public override IdentityError PasswordRequiresNonAlphanumeric() =>
		Translate(base.PasswordRequiresNonAlphanumeric(), "Passwords must have at least one non alphanumeric character.");

	/// <inheritdoc/>
	public override IdentityError PasswordRequiresDigit() => Translate(base.PasswordRequiresDigit(), "Passwords must have at least one digit ('0'-'9').");

	/// <inheritdoc/>
	public override IdentityError PasswordRequiresLower() => Translate(base.PasswordRequiresLower(), "Passwords must have at least one lowercase ('a'-'z').");

	/// <inheritdoc/>
	public override IdentityError PasswordRequiresUpper() => Translate(base.PasswordRequiresUpper(), "Passwords must have at least one uppercase ('A'-'Z').");

	/// <summary>
	/// Keeps Identity's error code and replaces its description with the UI language's sentence.
	/// </summary>
	/// <param name="Error">Identity's error, with its English description.</param>
	/// <param name="Key">The English sentence used as the translation key; it matches Identity's own wording.</param>
	/// <param name="Arguments">The values for the sentence's placeholders.</param>
	/// <returns>The error unchanged on English pages; otherwise the same code with a translated description.</returns>
	private static IdentityError Translate(IdentityError Error, string Key, params object?[] Arguments)
	{
		if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "en" or "iv")
			return Error;

		return new IdentityError { Code = Error.Code, Description = SharedResource.Format(Key, Arguments) };
	}
}
