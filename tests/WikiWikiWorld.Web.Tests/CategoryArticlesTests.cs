using Markdig;
using Markdig.Syntax;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.MarkdigExtensions;
using WikiWikiWorld.Web.Services;
using Xunit;

namespace WikiWikiWorld.Web.Tests;

/// <summary>Verifies category listing SQL and production rendering against SQLite.</summary>
public sealed class CategoryArticlesTests
{
	/// <summary>Checks standalone syntax while preserving malformed directives and code examples.</summary>
	[Theory]
	[InlineData("{{CategoryArticles}}", 1)]
	[InlineData("{{CategoryArticles}} trailing text", 0)]
	[InlineData("`{{CategoryArticles}}`", 0)]
	[InlineData("```\n{{CategoryArticles}}\n```", 0)]
	[InlineData("    {{CategoryArticles}}", 0)]
	public void ParsesOnlyStandaloneMarker(string Source, int Expected)
	{
		MarkdownDocument Document = Markdown.Parse(Source, new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())).GetPipeline());
		Assert.Equal(Expected, Document.Descendants<CategoryArticlesBlock>().Count());
	}

	/// <summary>Proves bounded paging, deterministic order, scope filtering, escaping, and live membership changes.</summary>
	[Fact]
	public async Task ListsOnlyVisibleScopedMembersAndPaginates()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		await Context.Database.EnsureCreatedAsync();
		for (int i = 0; i < 101; i++)
		{
			Context.Add(CreateRevision($"member-{i:D3}", $"Title {i:D3}") with
			{
				CategoryMemberships = [new() { CategorySlug = "fashion" }]
			});
		}
		foreach (ArticleRevision Excluded in new[]
		{
			CreateRevision("other-site", "Excluded") with { SiteId = 2 },
			CreateRevision("other-language", "Excluded") with { Culture = "fr" },
			CreateRevision("old", "Excluded") with { IsCurrent = false },
			CreateRevision("deleted", "Excluded") with { DateDeleted = DateTimeOffset.UtcNow }
		})
		{
			Excluded.CategoryMemberships.Add(new() { CategorySlug = "fashion" });
			Context.Add(Excluded);
		}
		Context.Add(CreateRevision("not-a-member", "Excluded"));
		Context.Add(CreateRevision("cover", "<script>alert('x')</script>") with
		{
			Type = ArticleType.File, CategoryMemberships = [new() { CategorySlug = "fashion" }]
		});
		await Context.SaveChangesAsync();
		ArticleRevision Category = CreateRevision("category:fashion", "Unrelated display name") with
		{
			Type = ArticleType.Category, Text = "{{CategoryArticles}}"
		};
		ArticleRenderer Renderer = new(Context, new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())), Options.Create(new SiteConfiguration()));
		string First = (await Renderer.RenderAsync(Category, 1, "en", "/edit", CancellationToken.None)).Html;
		Assert.DoesNotContain("Excluded", First);
		Assert.Contains("&lt;script&gt;", First);
		Assert.DoesNotContain("<script>", First);
		Assert.Contains("href=\"/file:cover\"", First);
		Assert.Contains("CategoryPage=2", First);
		Assert.Equal(100, First.Split("<li>").Length - 1);
		string Last = (await Renderer.RenderAsync(Category, 1, "en", "/edit", CancellationToken.None, int.MaxValue)).Html;
		Assert.Equal(2, Last.Split("<li>").Length - 1);
		Assert.Contains("Page 2 of 2", Last);
		Assert.Contains("Title 100", Last);
		Assert.DoesNotContain(">Next<", Last);
		Assert.Contains("CategoryPage=1", Last);
		Assert.Equal(First, (await Renderer.RenderAsync(Category, 1, "en", "/edit", CancellationToken.None, -1)).Html);
		await Context.ArticleCategoryMemberships.ExecuteDeleteAsync();
		Assert.Contains("No articles are currently listed", (await Renderer.RenderAsync(Category, 1, "en", "/edit", CancellationToken.None)).Html);
	}

	/// <summary>Articles without the marker or outside the category namespace need no membership table.</summary>
	[Fact]
	public async Task UnsupportedPlacementDoesNotQueryMemberships()
	{
		await using SqliteConnection Connection = new("Data Source=:memory:");
		await Connection.OpenAsync();
		await using WikiWikiWorldDbContext Context = new(new DbContextOptionsBuilder<WikiWikiWorldDbContext>().UseSqlite(Connection).Options);
		ArticleRenderer Renderer = new(Context, new MarkdownPipelineFactory(Options.Create(new SiteConfiguration())), Options.Create(new SiteConfiguration()));
		Assert.Contains("available on category articles", (await Renderer.RenderAsync(CreateRevision("plain", "Plain") with { Text = "{{CategoryArticles}}" }, 1, "en", "/edit", CancellationToken.None)).Html);
		Assert.Contains("Ordinary text", (await Renderer.RenderAsync(CreateRevision("category:plain", "Plain") with { Type = ArticleType.Category }, 1, "en", "/edit", CancellationToken.None)).Html);
	}

	/// <summary>Creates a current revision with explicit routing and source fields.</summary>
	/// <param name="Slug">Stored URL slug.</param>
	/// <param name="Title">Plain-text title.</param>
	/// <returns>A new unsaved revision.</returns>
	private static ArticleRevision CreateRevision(string Slug, string Title) => new()
	{
		CanonicalArticleId = Guid.NewGuid(), SiteId = 1, Culture = "en", Title = Title, UrlSlug = Slug,
		Text = "Ordinary text", IsCurrent = true, CreatedByUserId = Guid.NewGuid(), DateCreated = DateTimeOffset.UtcNow,
		RevisionReason = "Test"
	};
}
