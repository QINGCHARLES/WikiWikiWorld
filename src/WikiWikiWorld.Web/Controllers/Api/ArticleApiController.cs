using WikiWikiWorld.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;

namespace WikiWikiWorld.Web.Controllers.Api
{
	[Route("api/article")]
	[ApiController]
	public class ArticleApiController(IArticleRevisionRepository ArticleRevisionRepository, SiteResolverService SiteResolverService) : ControllerBase
	{
		[HttpGet("{UrlSlug}")]
		public async Task<IActionResult> GetArticleRevision(string UrlSlug, [FromQuery] string? Revision)
		{
			if (string.IsNullOrWhiteSpace(UrlSlug))
			{
				return BadRequest("Invalid parameters.");
			}

			(int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

			ArticleRevision? SpecificRevision = null;
			ArticleRevision? CurrentRevision;

			// If a revision is specified, parse the date and retrieve the specific revision
			if (!string.IsNullOrWhiteSpace(Revision) && RevisionDateParser.TryParseRevisionDate(Revision, out DateTimeOffset RevisionDate))
			{
				(CurrentRevision, SpecificRevision) = await ArticleRevisionRepository
					.GetRevisionBySiteIdCultureUrlSlugAndDateAsync(SiteId, Culture, UrlSlug, RevisionDate);

				// If specific revision exists, return it
				if (SpecificRevision is not null)
				{
					return Ok(SpecificRevision);
				}
			}

			// If no specific revision found, return the current revision
			CurrentRevision = await ArticleRevisionRepository
				.GetCurrentBySiteIdCultureAndUrlSlugAsync(SiteId, Culture, UrlSlug);

			if (CurrentRevision is null)
			{
				return NotFound("Article revision not found.");
			}

			return Ok(CurrentRevision);
		}

		[HttpPut("{UrlSlug}")]
		[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
		public async Task<IActionResult> UpdateArticleRevision(string UrlSlug, [FromBody] UpdateArticleRevisionModel Model)
		{
			if (string.IsNullOrWhiteSpace(UrlSlug) || Model == null)
			{
				return BadRequest("Invalid parameters.");
			}

			(int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

			var UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
			if (UserId == null)
			{
				return Unauthorized("User ID not found in token.");
			}

		var ArticleRevision = new ArticleRevision
		{
			CanonicalArticleId = Model.CanonicalArticleId ?? Guid.NewGuid(),
			SiteId = SiteId,
			Culture = Culture,
			Title = Model.Title,
			DisplayTitle = Model.DisplayTitle,
			UrlSlug = UrlSlug,
			Type = Model.Type,
			CanonicalFileId = Model.CanonicalFileId,
			Text = Model.Text,
			RevisionReason = Model.RevisionReason,
			CreatedByUserId = Guid.Parse(UserId)
		};			await ArticleRevisionRepository.InsertAsync(
				Model.CanonicalArticleId,
				SiteId,
				Culture,
				Model.Title,
				Model.DisplayTitle,
				UrlSlug,
				Model.Type,
				Model.CanonicalFileId,
				Model.Text,
				Model.RevisionReason,
				Guid.Parse(UserId)
			);

			return Ok("Article revision updated successfully.");
		}
	}

	public class UpdateArticleRevisionModel
	{
		public Guid? CanonicalArticleId { get; set; }
		public required string Title { get; set; }
		public string? DisplayTitle { get; set; }
		public ArticleType Type { get; set; }
		public Guid? CanonicalFileId { get; set; }
		public required string Text { get; set; }
		public required string RevisionReason { get; set; }
	}
}
