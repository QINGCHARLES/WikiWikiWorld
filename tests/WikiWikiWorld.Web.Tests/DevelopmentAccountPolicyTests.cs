using WikiWikiWorld.Web.Infrastructure;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies that the reserved account is usable only in Development across shared authentication checks.
/// </summary>
public sealed class DevelopmentAccountPolicyTests
{
	/// <summary>
	/// Covers production, staging, custom environments, case variants and the legacy email alias.
	/// </summary>
	/// <param name="EnvironmentName">The configured host environment.</param>
	/// <param name="UserName">The candidate username.</param>
	/// <param name="Email">The optional email.</param>
	/// <param name="Expected">Whether access must be refused.</param>
	[Theory]
	[InlineData("Development", "DevvyTheBot", null, false)]
	[InlineData("development", "DEVVYTHEBOT", "llmtest@ave.nu", false)]
	[InlineData("Production", "DevvyTheBot", null, true)]
	[InlineData("Staging", "DEVVYTHEBOT", null, true)]
	[InlineData("Preview", " DevvyTheBot ", null, true)]
	[InlineData("", "DevvyTheBot", null, true)]
	[InlineData("Production", "renamed", "LLMTEST@ave.nu", true)]
	[InlineData("Production", "llmtest", null, true)]
	[InlineData("Production", "ordinary-user", null, false)]
	[InlineData("Production", null, null, false)]
	public void RestrictsOnlyReservedAccount(string EnvironmentName, string? UserName, string? Email, bool Expected)
	{
		Assert.Equal(Expected, DevelopmentAccountPolicy.IsBlocked(EnvironmentName, UserName, Email));
	}
}
