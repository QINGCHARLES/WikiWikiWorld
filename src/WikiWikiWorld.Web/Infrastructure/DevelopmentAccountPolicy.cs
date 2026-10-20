namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Keeps the reserved automation account out of all non-development authentication paths.
/// </summary>
public static class DevelopmentAccountPolicy
{
	/// <summary>
	/// Checks DevvyTheBot and its former username and email alias without case sensitivity.
	/// </summary>
	/// <param name="EnvironmentName">The application's configured hosting environment.</param>
	/// <param name="UserName">The account username.</param>
	/// <param name="Email">The optional account email.</param>
	/// <returns>True when authentication or registration must be refused.</returns>
	public static bool IsBlocked(string EnvironmentName, string? UserName, string? Email = null) =>
		!string.Equals(EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
		&& (string.Equals(UserName?.Trim(), "DevvyTheBot", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(UserName?.Trim(), "llmtest", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(Email?.Trim(), "llmtest@ave.nu", StringComparison.OrdinalIgnoreCase));
}
