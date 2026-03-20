using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// API controller for authentication and JWT token issuance.
/// </summary>
/// <param name="UserManager">The user manager.</param>
/// <param name="SignInManager">The sign-in manager.</param>
/// <param name="Configuration">The application configuration.</param>
[Route("api/auth")]
[ApiController]
public sealed class AuthApiController(
	UserManager<User> UserManager,
	SignInManager<User> SignInManager,
	IConfiguration Configuration) : ControllerBase
{
	/// <summary>
	/// Issues a JWT token for valid credentials.
	/// </summary>
	/// <param name="Model">The login credentials.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A JWT token on success; 401 on failure.</returns>
	[HttpPost("token")]
	public async Task<IActionResult> GetToken([FromBody] LoginModel Model, CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Model.Username) || string.IsNullOrWhiteSpace(Model.Password))
		{
			return BadRequest("Username and password are required.");
		}

		User? TargetUser = await UserManager.FindByNameAsync(Model.Username);
		if (TargetUser is null)
		{
			return Unauthorized("Invalid credentials.");
		}

		Microsoft.AspNetCore.Identity.SignInResult Result = await SignInManager.CheckPasswordSignInAsync(
			TargetUser, Model.Password, lockoutOnFailure: true);

		if (!Result.Succeeded)
		{
			return Unauthorized("Invalid credentials.");
		}

		string Token = GenerateJwtToken(TargetUser);
		return Ok(new TokenResponse(Token));
	}

	/// <summary>
	/// Generates a JWT token for the specified user.
	/// </summary>
	/// <param name="TargetUser">The user to generate a token for.</param>
	/// <returns>The signed JWT token string.</returns>
	private string GenerateJwtToken(User TargetUser)
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
			new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
		];

		JwtSecurityToken Token = new(
			issuer: Configuration["JwtSettings:Issuer"],
			audience: Configuration["JwtSettings:Audience"],
			claims: Claims,
			expires: DateTime.UtcNow.AddHours(24),
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
}

/// <summary>
/// Response containing the issued JWT token.
/// </summary>
/// <param name="Token">The JWT token string.</param>
public sealed record TokenResponse(string Token);
