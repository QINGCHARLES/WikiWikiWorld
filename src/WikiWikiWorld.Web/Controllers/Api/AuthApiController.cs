using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Infrastructure;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// API controller for authentication and JWT token issuance.
/// </summary>
/// <param name="UserManager">The user manager.</param>
/// <param name="SignInManager">The sign-in manager.</param>
/// <param name="Configuration">The application configuration.</param>
/// <param name="Environment">The host environment used to restrict development accounts.</param>
[Route("api/auth")]
[ApiController]
[Produces("application/json")]
public sealed class AuthApiController(
	UserManager<User> UserManager,
	SignInManager<User> SignInManager,
	IConfiguration Configuration,
	IWebHostEnvironment Environment) : ControllerBase
{
	/// <summary>
	/// The claim carrying the account's Identity security stamp. The JWT bearer handler compares it with the
	/// account's current stamp on every request, so changing the stamp (e.g. a password change) revokes the token.
	/// </summary>
	public const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";

	/// <summary>
	/// The longest token lifetime a client may request, in days.
	/// </summary>
	public const int MaxTokenLifetimeDays = 365;

	/// <summary>
	/// Issues a JWT token for valid credentials, refusing development accounts outside Development.
	/// </summary>
	/// <remarks>
	/// Tokens last 24 hours unless <see cref="LoginModel.ExpiresInDays"/> asks for longer, up to
	/// <see cref="MaxTokenLifetimeDays"/>. Every token is bound to the account's current security stamp.
	/// </remarks>
	/// <param name="Model">The login credentials and optional lifetime.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A JWT token and its expiry on success; 400 for an invalid request; 401 on failure.</returns>
	[HttpPost("token")]
	[Consumes("application/json")]
	[ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
	public async Task<IActionResult> GetToken([FromBody] LoginModel Model, CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Model.Username) || string.IsNullOrWhiteSpace(Model.Password))
		{
			return BadRequest("Username and password are required.");
		}

		if (Model.ExpiresInDays is < 1 or > MaxTokenLifetimeDays)
		{
			return BadRequest($"ExpiresInDays must be between 1 and {MaxTokenLifetimeDays}.");
		}

		User? TargetUser = await UserManager.FindByNameAsync(Model.Username);
		if (TargetUser is null || DevelopmentAccountPolicy.IsBlocked(Environment.EnvironmentName, TargetUser.UserName, TargetUser.Email))
		{
			return Unauthorized("Invalid credentials.");
		}

		Microsoft.AspNetCore.Identity.SignInResult Result = await SignInManager.CheckPasswordSignInAsync(
			TargetUser, Model.Password, lockoutOnFailure: true);

		if (!Result.Succeeded)
		{
			return Unauthorized("Invalid credentials.");
		}

		// Accounts created outside Identity may lack a stamp; give them one so the token can be revoked later.
		string? SecurityStamp = await UserManager.GetSecurityStampAsync(TargetUser);
		if (string.IsNullOrEmpty(SecurityStamp))
		{
			await UserManager.UpdateSecurityStampAsync(TargetUser);
			SecurityStamp = await UserManager.GetSecurityStampAsync(TargetUser)
				?? throw new InvalidOperationException("The account has no security stamp.");
		}

		DateTime ExpiresUtc = Model.ExpiresInDays is int Days ? DateTime.UtcNow.AddDays(Days) : DateTime.UtcNow.AddHours(24);
		string Token = GenerateJwtToken(TargetUser, SecurityStamp, ExpiresUtc);
		return Ok(new TokenResponse(Token, ExpiresUtc));
	}

	/// <summary>
	/// Generates a JWT token for the specified user, bound to the given security stamp.
	/// </summary>
	/// <param name="TargetUser">The user to generate a token for.</param>
	/// <param name="SecurityStamp">The account's current security stamp, checked on every request.</param>
	/// <param name="ExpiresUtc">When the token stops being accepted.</param>
	/// <returns>The signed JWT token string.</returns>
	private string GenerateJwtToken(User TargetUser, string SecurityStamp, DateTime ExpiresUtc)
	{
		string? JwtSecret = Configuration["JwtSettings:Secret"];
		if (string.IsNullOrWhiteSpace(JwtSecret))
		{
			throw new InvalidOperationException("JWT secret is not configured.");
		}

		SymmetricSecurityKey SecurityKey = new(Encoding.UTF8.GetBytes(JwtSecret));
		SigningCredentials Credentials = new(SecurityKey, SecurityAlgorithms.HmacSha256);

		Claim[] Claims =
		[
			new(ClaimTypes.NameIdentifier, TargetUser.Id.ToString()),
			new(ClaimTypes.Name, TargetUser.UserName ?? string.Empty),
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
			new(SecurityStampClaimType, SecurityStamp)
		];

		JwtSecurityToken Token = new(
			issuer: Configuration["JwtSettings:Issuer"],
			audience: Configuration["JwtSettings:Audience"],
			claims: Claims,
			expires: ExpiresUtc,
			signingCredentials: Credentials);

		return new JwtSecurityTokenHandler().WriteToken(Token);
	}
}

/// <summary>
/// Model for login credentials.
/// </summary>
public sealed record LoginModel
{
	/// <summary>
	/// Gets the username.
	/// </summary>
	public required string Username { get; init; }

	/// <summary>
	/// Gets the password.
	/// </summary>
	public required string Password { get; init; }

	/// <summary>
	/// Gets the requested token lifetime in days (1 to <see cref="AuthApiController.MaxTokenLifetimeDays"/>);
	/// <see langword="null"/> issues the default 24-hour token.
	/// </summary>
	public int? ExpiresInDays { get; init; }
}

/// <summary>
/// Response containing the issued JWT token.
/// </summary>
/// <param name="Token">The JWT token string.</param>
/// <param name="ExpiresUtc">When the token expires, in UTC.</param>
public sealed record TokenResponse(string Token, DateTime ExpiresUtc);
