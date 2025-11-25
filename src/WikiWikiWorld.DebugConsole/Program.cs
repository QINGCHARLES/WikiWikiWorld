using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;

Console.WriteLine("WikiWikiWorld Debug Console - Direct DbContext Verification");

// Configure DbContext
var optionsBuilder = new DbContextOptionsBuilder<WikiWikiWorldDbContext>();
optionsBuilder.UseSqlite("Data Source=e:\\source\\WikiWikiWorld\\src\\WikiWikiWorld.Web\\bin\\Debug\\net9.0\\WikiWikiWorld.db");

using var context = new WikiWikiWorldDbContext(optionsBuilder.Options);

try
{
    Console.WriteLine("Connecting to database...");
    if (await context.Database.CanConnectAsync())
    {
        Console.WriteLine("Successfully connected to the database.");
    }
    else
    {
        Console.WriteLine("Failed to connect to the database.");
        return;
    }

    // 1. Verify Users and Profile Pics
    Console.WriteLine("\n--- Verifying Users ---");
    var users = await context.Users.Select(u => new { u.UserName, u.Id, u.ProfilePicGuid }).Take(5).ToListAsync();
    foreach (var user in users)
    {
        Console.WriteLine($"User: {user.UserName}, Id: {user.Id}, ProfilePic: {user.ProfilePicGuid}");
    }

    // 2. Verify Article Revisions by Slug (Header/Cover Image logic)
    Console.WriteLine("\n--- Verifying Article Revisions by Slug ---");
    int siteId = 1;
    string culture = "en";
    string slug = "home"; // Assuming 'home' exists, or pick another known slug

    var spec = new ArticleRevisionsBySlugSpec(siteId, culture, slug, isCurrent: true);
    var article = await context.ArticleRevisions.WithSpecification(spec).FirstOrDefaultAsync();

    if (article != null)
    {
        Console.WriteLine($"Found Article: {article.Title}, Slug: {article.UrlSlug}, CanonicalFileId: {article.CanonicalFileId}");

        if (article.CanonicalFileId.HasValue)
        {
            var fileSpec = new CurrentFileRevisionByCanonicalIdSpec(article.CanonicalFileId.Value);
            var file = await context.FileRevisions.WithSpecification(fileSpec).FirstOrDefaultAsync();

            if (file != null)
            {
                Console.WriteLine($"Found File: {file.Filename}, CanonicalId: {file.CanonicalFileId}");
            }
            else
            {
                Console.WriteLine("File not found for article.");
            }
        }
    }
    else
    {
        Console.WriteLine($"Article with slug '{slug}' not found.");
    }

    // 3. Verify Latest Articles with Infobox (Index page logic)
    Console.WriteLine("\n--- Verifying Latest Articles with Infobox ---");
    var latestSpec = new LatestArticlesWithPublicationIssueInfoboxSpec(siteId, culture, 5);
    var latestArticles = await context.ArticleRevisions.WithSpecification(latestSpec).ToListAsync();

    Console.WriteLine($"Found {latestArticles.Count} articles with infoboxes.");
    foreach (var a in latestArticles)
    {
        Console.WriteLine($"Title: {a.Title}, Date: {a.DateCreated}");
    }

}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
