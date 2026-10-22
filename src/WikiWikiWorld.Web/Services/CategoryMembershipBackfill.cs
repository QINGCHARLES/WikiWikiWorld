using Markdig;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Reconciles revision-owned memberships across all sites, cultures, and revision states in bounded transactions.
/// </summary>
/// <param name="Context">A dedicated SQLite context with an open connection and no pending changes.</param>
/// <param name="PipelineFactory">The full article parser shared with save-time indexing.</param>
public sealed class CategoryMembershipBackfill(WikiWikiWorldDbContext Context, IMarkdownPipelineFactory PipelineFactory)
{
	/// <summary>
	/// Scans a fixed upper revision ID in batches, preserving source text and repairing only derived memberships.
	/// Write batches acquire SQLite's writer lock before reading so source changes cannot race reconciliation.
	/// </summary>
	/// <param name="Apply">Whether to commit changes; false only reports proposed changes.</param>
	/// <param name="Output">Receives progress after each completed batch and final totals.</param>
	/// <param name="CancellationToken">Cancels between operations; completed batches remain committed.</param>
	/// <returns>A task completing after all revisions present at the starting upper bound have been scanned.</returns>
	public async Task RunAsync(bool Apply, TextWriter Output, CancellationToken CancellationToken)
	{
		CancellationToken.ThrowIfCancellationRequested();
		// Check the required table even when there are no revisions. Schema changes are always explicit.
		await Context.ArticleCategoryMemberships.AnyAsync(CancellationToken);
		int UpperId = await Context.ArticleRevisions.IgnoreQueryFilters().MaxAsync(r => (int?)r.Id, CancellationToken) ?? 0;
		int LastId = 0;
		int Scanned = 0;
		int Added = 0;
		int Removed = 0;
		SqliteConnection Connection = (SqliteConnection)Context.Database.GetDbConnection();
		if (Apply)
		{
			// Maintenance uses durable commits without changing the website's configured connection policy.
			await Context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=FULL;", CancellationToken);
		}
		while (LastId < UpperId)
		{
			CancellationToken.ThrowIfCancellationRequested();
			using SqliteTransaction Transaction = Connection.BeginTransaction(deferred: !Apply);
			await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Enlisted =
				(await Context.Database.UseTransactionAsync(Transaction, CancellationToken))!;
			var Revisions = await Context.ArticleRevisions.IgnoreQueryFilters()
				.Where(r => r.Id > LastId && r.Id <= UpperId).OrderBy(r => r.Id).Take(100)
				.Select(r => new { r.Id, r.Text }).ToListAsync(CancellationToken);
			if (Revisions.Count == 0) break;
			int[] Ids = [.. Revisions.Select(r => r.Id)];
			List<ArticleCategoryMembership> Existing = await Context.ArticleCategoryMemberships
				.Where(m => Ids.Contains(m.ArticleRevisionId)).ToListAsync(CancellationToken);
			ILookup<int, ArticleCategoryMembership> ByRevision = Existing.ToLookup(m => m.ArticleRevisionId);
			int BatchAdded = 0;
			int BatchRemoved = 0;
			foreach (var Revision in Revisions)
			{
				CancellationToken.ThrowIfCancellationRequested();
				HashSet<string> Slugs = CategoryExtension.GetCategories(Markdown.Parse(Revision.Text, PipelineFactory.GetPipeline()))
					.Select(c => c.UrlSlug!).ToHashSet(StringComparer.Ordinal);
				foreach (ArticleCategoryMembership Membership in ByRevision[Revision.Id])
				{
					if (Slugs.Remove(Membership.CategorySlug)) continue;
					Context.Remove(Membership);
					BatchRemoved++;
				}
				foreach (string Slug in Slugs)
				{
					Context.ArticleCategoryMemberships.Add(new() { ArticleRevisionId = Revision.Id, CategorySlug = Slug });
					BatchAdded++;
				}
			}
			if (Apply) await Context.SaveChangesAsync(CancellationToken);
			await Transaction.CommitAsync(CancellationToken);
			Context.ChangeTracker.Clear();
			LastId = Revisions[^1].Id;
			Scanned += Revisions.Count;
			Added += BatchAdded;
			Removed += BatchRemoved;
			await Output.WriteLineAsync($"Through revision {LastId}/{UpperId}: scanned {Scanned}, add {Added}, remove {Removed}.");
		}
		await Output.WriteLineAsync($"{(Apply ? "Applied" : "Dry run complete")}: {Scanned} revisions, {Added} additions, {Removed} removals.");
	}
}
