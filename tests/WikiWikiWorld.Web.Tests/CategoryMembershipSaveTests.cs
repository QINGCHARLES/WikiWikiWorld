using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>
/// Verifies revision membership indexing and transactional failure behavior against actual SQLite.
/// </summary>
public sealed class CategoryMembershipSaveTests : IDisposable
{
	private readonly SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
	private readonly CategoryMembershipSaveInterceptor Interceptor = new(
		new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())));

	/// <summary>
	/// Creates a disposable database with the application's EF schema.
	/// </summary>
	public CategoryMembershipSaveTests()
	{
		Connection.Open();
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Database.EnsureCreated();
	}

	/// <inheritdoc/>
	public void Dispose() => Connection.Dispose();

	/// <summary>
	/// Covers both save APIs and all article types, deduplication, explicit identity, Unicode, and code examples.
	/// </summary>
	/// <param name="Type">The article namespace being saved.</param>
	/// <param name="UseAsync">Whether to exercise the asynchronous save hook.</param>
	[Theory]
	[InlineData(ArticleType.Article, true)]
	[InlineData(ArticleType.Category, false)]
	[InlineData(ArticleType.File, true)]
	[InlineData(ArticleType.User, false)]
	[InlineData(ArticleType.Info, true)]
	public async Task NewRevisionStoresOnlyDistinctParsedSlugs(ArticleType Type, bool UseAsync)
	{
		using WikiWikiWorldDbContext Context = CreateContext();
		ArticleRevision Revision = CreateRevision("""
			{{Category Fashion magazines}}
			{{Category Another label|#|fashion-magazines}}
			{{Category हिन्दी पत्रिकाएँ}}
			{{Category Label|#|Exact-Slug}}
			{{Category Invalid|#|../bad}}
			`{{Category Inline example}}`
			
			```
			{{Category Fenced example}}
			```
			""", Type);
		Context.ArticleRevisions.Add(Revision);
		if (UseAsync) await Context.SaveChangesAsync();
		else Context.SaveChanges();
		Assert.True(Revision.Id > 0);
		Assert.Equal(3, Context.ArticleCategoryMemberships.Count());
		Assert.All(Context.ArticleCategoryMemberships, m => Assert.Equal(Revision.Id, m.ArticleRevisionId));
		Assert.Equal(["Exact-Slug", "fashion-magazines", "हिन्दी-पत्रिकाएँ"],
			Context.ArticleCategoryMemberships.Select(m => m.CategorySlug).OrderBy(s => s).ToArray());
		Assert.DoesNotContain("CategoryMemberships", JsonSerializer.Serialize(Revision));
		await Context.SaveChangesAsync();
		Assert.Equal(3, Context.ArticleCategoryMemberships.Count());
	}

	/// <summary>
	/// Uses the revision workflow to remove old current memberships without erasing historical ones.
	/// </summary>
	[Fact]
	public async Task EditAndDeleteRetainHistoricalMemberships()
	{
		using WikiWikiWorldDbContext Context = CreateContext();
		ArticleRevision Original = CreateRevision("{{Category Old}}");
		Context.Add(Original);
		await Context.SaveChangesAsync();
		ArticleRevisionService Service = new(Context, NullLogger<ArticleRevisionService>.Instance);
		ArticleRevision Updated = await Service.UpdateArticleAsync(new ArticleRevisionUpdateCommand(
			OriginalUrlSlug: Original.UrlSlug, NewUrlSlug: Original.UrlSlug,
			Title: Original.Title, DisplayTitle: null, CanonicalFileId: null,
			Text: "No categories remain.", RevisionReason: "Remove category",
			SiteId: 1, Culture: "en", Type: ArticleType.Article, CreatedByUserId: Original.CreatedByUserId), CancellationToken.None);
		Assert.Empty(Context.ArticleCategoryMemberships.Where(m => m.ArticleRevisionId == Updated.Id));
		Assert.Single(Context.ArticleCategoryMemberships.Where(m => m.ArticleRevisionId == Original.Id));
		await Service.DeleteArticleAsync(Original.UrlSlug, CancellationToken.None);
		Assert.Empty(Context.ArticleRevisions.Where(r => r.IsCurrent));
		Assert.Single(Context.ArticleCategoryMemberships);
	}

	/// <summary>
	/// Repairs missing historical membership rows when the existing restore workflow reactivates a revision.
	/// </summary>
	[Fact]
	public async Task RestoringLegacyRevisionIndexesItsCategories()
	{
		int PreviousId;
		using (WikiWikiWorldDbContext Seed = CreateContext(EnableIndexing: false))
		{
			ArticleRevision Previous = CreateRevision("{{Category Old label|#|old-slug}}") with { IsCurrent = false };
			Seed.Add(Previous);
			Seed.Add(CreateRevision("{{Category Current}}") with { CanonicalArticleId = Previous.CanonicalArticleId });
			Seed.SaveChanges();
			PreviousId = Previous.Id;
		}
		using WikiWikiWorldDbContext Context = CreateContext();
		ArticleRevisionService Service = new(Context, NullLogger<ArticleRevisionService>.Instance);
		ArticleRevision Restored = await Service.RevertToPreviousRevisionAsync("example", CancellationToken.None);
		Assert.Equal(PreviousId, Restored.Id);
		Assert.Equal("old-slug", Assert.Single(Context.ArticleCategoryMemberships).CategorySlug);
	}

	/// <summary>
	/// Forces a membership insert failure to prove the parent revision rolls back and the same save can be retried.
	/// </summary>
	[Fact]
	public async Task MembershipFailureRollsBackRevisionAndRetryDoesNotDuplicate()
	{
		using WikiWikiWorldDbContext Context = CreateContext();
		await Context.Database.ExecuteSqlRawAsync("""
			CREATE TRIGGER RejectMembership BEFORE INSERT ON ArticleCategoryMemberships
			BEGIN SELECT RAISE(ABORT, 'test membership failure'); END;
			""");
		Context.Add(CreateRevision("{{Category Test}}"));
		await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
		using (WikiWikiWorldDbContext Verify = CreateContext())
		{
			Assert.Empty(Verify.ArticleRevisions);
			Assert.Empty(Verify.ArticleCategoryMemberships);
		}
		await Context.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectMembership;");
		await Context.SaveChangesAsync();
		Assert.Single(Context.ArticleRevisions);
		Assert.Single(Context.ArticleCategoryMemberships);
	}

	/// <summary>
	/// Ensures a cancelled save does not create revision or membership rows.
	/// </summary>
	[Fact]
	public async Task CancellationDoesNotWrite()
	{
		using WikiWikiWorldDbContext Context = CreateContext();
		Context.Add(CreateRevision("{{Category Test}}"));
		using CancellationTokenSource Cancellation = new();
		Cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Context.SaveChangesAsync(Cancellation.Token));
		using WikiWikiWorldDbContext Verify = CreateContext();
		Assert.Empty(Verify.ArticleRevisions);
		Assert.Empty(Verify.ArticleCategoryMemberships);
	}

	/// <summary>
	/// Creates a context using the production interceptor or an unindexed legacy writer for restoration fixtures.
	/// </summary>
	/// <param name="EnableIndexing">Whether saves should index their categories.</param>
	/// <returns>A context sharing the fixture's SQLite connection.</returns>
	private WikiWikiWorldDbContext CreateContext(bool EnableIndexing = true)
	{
		DbContextOptionsBuilder<WikiWikiWorldDbContext> Builder = new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection);
		if (EnableIndexing) Builder.AddInterceptors(Interceptor);
		return new(Builder.Options);
	}

	/// <summary>
	/// Creates a revision with stable routing metadata and a fresh canonical identity.
	/// </summary>
	/// <param name="Text">The Markdown source to index.</param>
	/// <param name="Type">The article namespace.</param>
	/// <returns>An unsaved current revision.</returns>
	private static ArticleRevision CreateRevision(string Text, ArticleType Type = ArticleType.Article) => new()
	{
		CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = "Example", UrlSlug = "example",
		IsCurrent = true, Type = Type, Text = Text, RevisionReason = "Test", CreatedByUserId = Guid.NewGuid(),
		DateCreated = DateTimeOffset.UtcNow
	};
}
