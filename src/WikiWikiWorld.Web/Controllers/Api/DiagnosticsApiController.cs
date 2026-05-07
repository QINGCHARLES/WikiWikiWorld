using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// Development-only API controller for database diagnostics.
/// </summary>
/// <param name="Environment">The hosting environment.</param>
[Route("api/diagnostics")]
[ApiController]
[Produces("application/json")]
public sealed class DiagnosticsApiController(IWebHostEnvironment Environment) : ControllerBase
{
	/// <summary>
	/// Clears all SQLite connection pools, releasing any held database locks.
	/// Only available in the Development environment.
	/// </summary>
	/// <returns>A confirmation message.</returns>
	[HttpPost("clear-pools")]
	[ProducesResponseType<object>(StatusCodes.Status200OK)]
	[ProducesResponseType(StatusCodes.Status404NotFound)]
	public IActionResult ClearPools()
	{
		if (!Environment.IsDevelopment())
			return NotFound();

		SqliteConnection.ClearAllPools();

		return Ok(new { Message = "All SQLite connection pools cleared." });
	}
}
