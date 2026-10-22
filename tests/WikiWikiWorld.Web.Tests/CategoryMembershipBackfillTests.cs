using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Exercises maintenance batching, repair, and rollback against SQLite.</summary>
public sealed class CategoryMembershipBackfillTests
{
	/// <summary>Verifies dry runs, all revision scopes, batch boundaries, stale-row repair, and repeatability.</summary>
	[Fact]
	public async Task BackfillRepairsAllScopesWithoutChangingSourceAndIsIdempotent()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:;Foreign Keys=True");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();
		for (int i = 0; i < 101; i++)
		{
			Context.Add(new ArticleRevision
			{
				CanonicalArticleId = Guid.NewGuid(), SiteId = i % 2 + 1, Culture = i % 2 == 0 ? "en" : "hi",
				Title = "Test", UrlSlug = $"test-{i}", Text = "{{Category हिन्दी}} {{Category Another label|#|हिन्दी}} `{{Category Code}}`",
				RevisionReason = "Test", CreatedByUserId = Guid.NewGuid(), DateCreated = DateTimeOffset.UtcNow,
				IsCurrent = i % 2 == 0, DateDeleted = i % 3 == 0 ? DateTimeOffset.UtcNow : null
			});
		}
		await Context.SaveChangesAsync();
		Context.ArticleCategoryMemberships.Add(new() { ArticleRevisionId = 1, CategorySlug = "stale" });
		await Context.SaveChangesAsync();
		Context.ChangeTracker.Clear();
		CategoryMembershipBackfill Backfill = CreateBackfill(Context);
		using StringWriter Output = new();
		await Backfill.RunAsync(false, Output, CancellationToken.None);
		Assert.Equal("stale", (await Context.ArticleCategoryMemberships.SingleAsync()).CategorySlug);
		Assert.Contains("101 additions, 1 removals", Output.ToString());
		Context.ChangeTracker.Clear();
		await Backfill.RunAsync(true, TextWriter.Null, CancellationToken.None);
		Assert.Equal(101, await Context.ArticleCategoryMemberships.CountAsync());
		Assert.All(await Context.ArticleCategoryMemberships.ToListAsync(), m => Assert.Equal("हिन्दी", m.CategorySlug));
		Assert.Equal(101, await Context.ArticleRevisions.IgnoreQueryFilters().CountAsync());
		Assert.All(await Context.ArticleRevisions.IgnoreQueryFilters().ToListAsync(), r => Assert.Contains("{{Category Another label", r.Text));
		Context.ChangeTracker.Clear();
		using StringWriter RepeatOutput = new();
		await Backfill.RunAsync(true, RepeatOutput, CancellationToken.None);
		Assert.Contains("0 additions, 0 removals", RepeatOutput.ToString());
	}

	/// <summary>Ensures a failed batch cannot leave partial memberships or change the source article.</summary>
	[Fact]
	public async Task FailedBatchRollsBack()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();
		Context.Add(new ArticleRevision
		{
			CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = "Test", UrlSlug = "test",
			Text = "{{Category A}} {{Category B}}", RevisionReason = "Test", CreatedByUserId = Guid.NewGuid(),
			DateCreated = DateTimeOffset.UtcNow, IsCurrent = true
		});
		await Context.SaveChangesAsync();
		Context.ChangeTracker.Clear();
		await Context.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailMembership BEFORE INSERT ON ArticleCategoryMemberships WHEN NEW.CategorySlug = 'b' BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
		await Assert.ThrowsAsync<DbUpdateException>(() => CreateBackfill(Context).RunAsync(true, TextWriter.Null, CancellationToken.None));
		Context.ChangeTracker.Clear();
		Assert.Empty(await Context.ArticleCategoryMemberships.ToListAsync());
		Assert.Single(await Context.ArticleRevisions.ToListAsync());
		await Context.Database.ExecuteSqlRawAsync("DROP TRIGGER FailMembership;");
		await CreateBackfill(Context).RunAsync(true, TextWriter.Null, CancellationToken.None);
		Assert.Equal(2, await Context.ArticleCategoryMemberships.CountAsync());
	}

	/// <summary>Uses the same full parser as the maintenance command.</summary>
	/// <param name="Context">The test database context.</param>
	/// <returns>A maintenance service using production parsing rules.</returns>
	private static CategoryMembershipBackfill CreateBackfill(WikiWikiWorldDbContext Context) =>
		new(Context, new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())));
}
