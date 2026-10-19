using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Web.Pages.Article;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies the file revision history and codec resolution for file articles.
/// </summary>
public sealed class FileRevisionHistoryTests : IDisposable
{
	private readonly SqliteConnection Connection;
	private readonly DbContextOptions<WikiWikiWorldDbContext> DbOptions;

	/// <summary>
	/// Initializes a new instance of the <see cref="FileRevisionHistoryTests"/> class.
	/// </summary>
	public FileRevisionHistoryTests()
	{
		Connection = new SqliteConnection("DataSource=:memory:");
		Connection.Open();

		DbOptions = new DbContextOptionsBuilder<WikiWikiWorldDbContext>()
			.UseSqlite(Connection)
			.Options;

		using WikiWikiWorldDbContext Context = new(DbOptions);
		Context.Database.EnsureCreated();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		Connection.Dispose();
	}

	/// <summary>
	/// Verifies that FormatCodecName correctly identifies well-known image MIME types.
	/// </summary>
	/// <param name="MimeType">The MIME type.</param>
	/// <param name="Filename">The filename.</param>
	/// <param name="ExpectedCodec">The expected friendly codec name.</param>
	[Theory]
	[InlineData("image/jpeg", "photo.jpg", "JPEG")]
	[InlineData("image/png", "graphic.png", "PNG")]
	[InlineData("image/webp", "header.webp", "WebP")]
	[InlineData("image/avif", "banner.avif", "AVIF")]
	[InlineData("image/gif", "anim.gif", "GIF")]
	[InlineData("image/heic", "shot.heic", "HEIC")]
	[InlineData("image/heif", "shot.heif", "HEIF")]
	[InlineData("image/svg+xml", "icon.svg", "SVG")]
	[InlineData("application/octet-stream", "scan.jpg", "JPEG")]
	[InlineData("application/octet-stream", "cover.webp", "WebP")]
	[InlineData("application/octet-stream", "unknown.xyz", "XYZ")]
	public void FormatCodecName_IdentifiesExpectedCodecs(string MimeType, string Filename, string ExpectedCodec)
	{
		string ActualCodec = ViewModel.FormatCodecName(MimeType, Filename);
		Assert.Equal(ExpectedCodec, ActualCodec);
	}

	/// <summary>
	/// Verifies that multiple file revisions for a single canonical file are retrieved in reverse chronological order
	/// with correct metadata, dimensions, sizes, and current version flags.
	/// </summary>
	[Fact]
	public async Task FileRevisionHistory_RetrievesAllVersionsOrderedWithStats()
	{
		Guid CanonicalArticleId = Guid.NewGuid();
		Guid CanonicalFileId = Guid.NewGuid();
		Guid UserId = Guid.NewGuid();
		DateTimeOffset BaseTime = DateTimeOffset.UtcNow;

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			User TestUser = new()
			{
				Id = UserId,
				UserName = "editor1",
				Email = "editor1@example.com"
			};
			Context.Users.Add(TestUser);

			FileRevision InitialVersion = new()
			{
				Id = 1,
				CanonicalFileId = CanonicalFileId,
				IsCurrent = false,
				Type = FileType.Image2D,
				Filename = "original.jpg",
				MimeType = "image/jpeg",
				FileSizeBytes = 100_000,
				ImageWidthPixels = 800,
				ImageHeightPixels = 600,
				RevisionReason = "Initial upload",
				SourceAndRevisionReasonCulture = "en",
				CreatedByUserId = UserId,
				DateCreated = BaseTime.AddHours(-2)
			};

			FileRevision UpdatedVersion = new()
			{
				Id = 2,
				CanonicalFileId = CanonicalFileId,
				IsCurrent = true,
				Type = FileType.Image2D,
				Filename = "high-res.webp",
				MimeType = "image/webp",
				FileSizeBytes = 250_000,
				ImageWidthPixels = 1600,
				ImageHeightPixels = 1200,
				RevisionReason = "Replaced with higher resolution scan",
				SourceAndRevisionReasonCulture = "en",
				CreatedByUserId = UserId,
				DateCreated = BaseTime
			};

			ArticleRevision Article = new()
			{
				Id = 10,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Header for Magazine",
				UrlSlug = "header-for-magazine",
				Type = ArticleType.File,
				CanonicalFileId = CanonicalFileId,
				Text = "Test file article",
				RevisionReason = "Created",
				CreatedByUserId = UserId,
				DateCreated = BaseTime,
				IsCurrent = true
			};

			Context.FileRevisions.AddRange(InitialVersion, UpdatedVersion);
			Context.ArticleRevisions.Add(Article);
			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			List<FileRevision> AllRevisions = await Context.FileRevisions
				.AsNoTracking()
				.Where(F => F.CanonicalFileId == CanonicalFileId && F.DateDeleted == null)
				.OrderByDescending(F => F.DateCreated)
				.ThenByDescending(F => F.Id)
				.ToListAsync();

			Assert.Equal(2, AllRevisions.Count);

			FileRevision Current = AllRevisions[0];
			Assert.True(Current.IsCurrent);
			Assert.Equal("high-res.webp", Current.Filename);
			Assert.Equal(1600, Current.ImageWidthPixels);
			Assert.Equal(1200, Current.ImageHeightPixels);
			Assert.Equal(250_000, Current.FileSizeBytes);

			FileRevision Previous = AllRevisions[1];
			Assert.False(Previous.IsCurrent);
			Assert.Equal("original.jpg", Previous.Filename);
			Assert.Equal(800, Previous.ImageWidthPixels);
			Assert.Equal(600, Previous.ImageHeightPixels);
			Assert.Equal(100_000, Previous.FileSizeBytes);

			Dictionary<Guid, string> UserNames = await Context.Users
				.AsNoTracking()
				.Where(U => U.Id == UserId && U.UserName != null)
				.ToDictionaryAsync(U => U.Id, U => U.UserName!);

			List<FileRevisionHistoryItem> HistoryItems = [.. AllRevisions.Select(F => new FileRevisionHistoryItem(
				F.Id,
				F.CanonicalFileId,
				F.Filename,
				ImageUrlHelper.BuildImageUrl(1, F.CanonicalFileId, F.Id, F.Filename),
				F.MimeType,
				ViewModel.FormatCodecName(F.MimeType, F.Filename),
				F.FileSizeBytes,
				F.ImageWidthPixels,
				F.ImageHeightPixels,
				F.IsCurrent == true,
				F.DateCreated,
				F.CreatedByUserId,
				UserNames.GetValueOrDefault(F.CreatedByUserId),
				F.RevisionReason))];

			Assert.Equal(2, HistoryItems.Count);
			Assert.Equal("WebP", HistoryItems[0].Codec);
			Assert.True(HistoryItems[0].IsCurrent);
			Assert.Equal("editor1", HistoryItems[0].CreatedByUserName);
			Assert.Equal("/sitefiles/1/images/" + CanonicalFileId + "/2.webp", HistoryItems[0].FileImageUrl);

			Assert.Equal("JPEG", HistoryItems[1].Codec);
			Assert.False(HistoryItems[1].IsCurrent);
			Assert.Equal("editor1", HistoryItems[1].CreatedByUserName);
			Assert.Equal("/sitefiles/1/images/" + CanonicalFileId + "/1.jpg", HistoryItems[1].FileImageUrl);
		}
	}

	/// <summary>
	/// Verifies that GetRecentAuthorsAsync retrieves the author list.
	/// </summary>
	[Fact]
	public async Task GetRecentAuthorsAsync_ReturnsAuthors()
	{
		Guid UserId = Guid.NewGuid();
		Guid CanonicalArticleId = Guid.NewGuid();
		DateTimeOffset BaseTime = DateTimeOffset.UtcNow;

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			User TestUser = new()
			{
				Id = UserId,
				UserName = "editor1",
				Email = "editor1@example.com",
				ProfilePicGuid = Guid.NewGuid()
			};
			Context.Users.Add(TestUser);

			ArticleRevision Article = new()
			{
				Id = 100,
				CanonicalArticleId = CanonicalArticleId,
				SiteId = 1,
				Culture = "en",
				Title = "Test Article",
				UrlSlug = "test-article",
				Type = ArticleType.File,
				Text = "Test",
				RevisionReason = "Created",
				CreatedByUserId = UserId,
				DateCreated = BaseTime,
				IsCurrent = true
			};
			Context.ArticleRevisions.Add(Article);
			await Context.SaveChangesAsync();
		}

		await using (WikiWikiWorldDbContext Context = new(DbOptions))
		{
			WikiWikiWorld.Web.Services.ArticleRevisionService Service = new(Context, Microsoft.Extensions.Logging.Abstractions.NullLogger<WikiWikiWorld.Web.Services.ArticleRevisionService>.Instance);
			var AuthorsCurrent = await Service.GetRecentAuthorsAsync(CanonicalArticleId, null, CancellationToken.None);
			Assert.Single(AuthorsCurrent);
			Assert.Equal("editor1", AuthorsCurrent[0].UserName);

			var AuthorsWithDate = await Service.GetRecentAuthorsAsync(CanonicalArticleId, BaseTime, CancellationToken.None);
			Assert.Single(AuthorsWithDate);
			Assert.Equal("editor1", AuthorsWithDate[0].UserName);
		}
	}
}


