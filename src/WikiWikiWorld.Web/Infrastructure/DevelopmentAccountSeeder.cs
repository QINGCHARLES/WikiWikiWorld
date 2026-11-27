using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Recreates the DevvyTheBot test account when the development database lacks it, as it does after the database is
/// replaced with a copy of production (where the account is blocked and never exists).
/// </summary>
public static class DevelopmentAccountSeeder
{
	/// <summary>The development test account's user name.</summary>
	private const string UserName = "DevvyTheBot";

	/// <summary>The role the test account holds in development, so it can exercise administrator and editor tools.</summary>
	private const string RoleName = "Administrator";

	/// <summary>
	/// Ensures DevvyTheBot exists and holds <see cref="RoleName"/>, using the credentials in the Git-ignored
	/// <c>.local/DevvyTheBot.json</c> found by walking up from the content root. Does nothing when that file is absent.
	/// Call only in the Development environment.
	/// </summary>
	/// <remarks>
	/// An existing account keeps its password; only a missing account is created from the file. Failures are logged
	/// and never stop the application from starting.
	/// </remarks>
	/// <param name="Services">The application's root service provider.</param>
	/// <param name="ContentRootPath">The web project's content root, from which the repository's <c>.local</c> folder is located.</param>
	/// <param name="CancellationToken">Cancels reading the credentials file.</param>
	/// <returns>A task that completes when the account has been checked.</returns>
	public static async Task EnsureAsync(IServiceProvider Services, string ContentRootPath, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(Services);

		await using AsyncServiceScope Scope = Services.CreateAsyncScope();
		ILogger Logger = Scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevelopmentAccountSeeder));

		string? CredentialsPath = FindCredentialsFile(ContentRootPath);
		if (CredentialsPath is null)
		{
			Logger.LogInformation("No .local/{UserName}.json found; the development test account was not checked.", UserName);
			return;
		}

		try
		{
			UserManager<User> Users = Scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			User? Account = await Users.FindByNameAsync(UserName);

			if (Account is null)
			{
				await using FileStream Stream = File.OpenRead(CredentialsPath);
				using JsonDocument Credentials = await JsonDocument.ParseAsync(Stream, cancellationToken: CancellationToken);
				string? Password = Credentials.RootElement.TryGetProperty("Password", out JsonElement Value) ? Value.GetString() : null;
				if (string.IsNullOrWhiteSpace(Password))
				{
					Logger.LogWarning("{Path} has no Password; the development test account was not created.", CredentialsPath);
					return;
				}

				Account = new()
				{
					UserName = UserName,
					Email = "devvythebot@localhost.invalid",
					EmailConfirmed = true,
					DateCreated = DateTimeOffset.UtcNow,
				};

				IdentityResult Created = await Users.CreateAsync(Account, Password);
				if (!Created.Succeeded)
				{
					Logger.LogWarning("Could not create the development test account: {Errors}", string.Join("; ", Created.Errors.Select(e => e.Description)));
					return;
				}

				Logger.LogInformation("Created the development test account {UserName}.", UserName);
			}

			if (!await Users.IsInRoleAsync(Account, RoleName))
			{
				IdentityResult Granted = await Users.AddToRoleAsync(Account, RoleName);
				if (!Granted.Succeeded)
					Logger.LogWarning("Could not grant {Role} to {UserName}: {Errors}", RoleName, UserName, string.Join("; ", Granted.Errors.Select(e => e.Description)));
			}
		}
		catch (Exception Ex) when (Ex is not OperationCanceledException)
		{
			Logger.LogWarning(Ex, "Checking the development test account failed; startup continues.");
		}
	}

	/// <summary>
	/// Looks for <c>.local/DevvyTheBot.json</c> in the content root and each folder above it, so it is found from the
	/// web project whether the app runs from the repository root or a worktree.
	/// </summary>
	/// <param name="ContentRootPath">The folder to start from.</param>
	/// <returns>The file's full path, or <see langword="null"/> when no folder above has it.</returns>
	private static string? FindCredentialsFile(string ContentRootPath)
	{
		for (DirectoryInfo? Folder = new(ContentRootPath); Folder is not null; Folder = Folder.Parent)
		{
			string Candidate = Path.Combine(Folder.FullName, ".local", $"{UserName}.json");
			if (File.Exists(Candidate))
				return Candidate;
		}

		return null;
	}
}
