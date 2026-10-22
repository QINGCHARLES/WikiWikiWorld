using Markdig;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.MarkdigExtensions;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Stages parsed category memberships in the same EF save as new, edited, or reactivated article revisions.
/// </summary>
/// <param name="MarkdownPipelineFactory">The shared article parser, including native-script category identities.</param>
/// <remarks>
/// This interceptor performs no separate save or transaction. EF propagates generated revision IDs
/// through the membership relationship. Direct SQL and external maintenance writers bypass it.
/// </remarks>
public sealed class CategoryMembershipSaveInterceptor(IMarkdownPipelineFactory MarkdownPipelineFactory) : SaveChangesInterceptor
{
	/// <inheritdoc/>
	public override InterceptionResult<int> SavingChanges(DbContextEventData EventData, InterceptionResult<int> Result)
	{
		if (EventData.Context is not DbContext Context) return Result;
		foreach (EntityEntry<ArticleRevision> Entry in GetRevisions(Context))
		{
			if (Entry.State != EntityState.Added)
				Entry.Collection(r => r.CategoryMemberships).Load();
			StageMemberships(Context, Entry.Entity);
		}
		Context.ChangeTracker.DetectChanges();
		return Result;
	}

	/// <inheritdoc/>
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData EventData, InterceptionResult<int> Result, CancellationToken CancellationToken = default)
	{
		CancellationToken.ThrowIfCancellationRequested();
		if (EventData.Context is not DbContext Context) return Result;
		foreach (EntityEntry<ArticleRevision> Entry in GetRevisions(Context))
		{
			CancellationToken.ThrowIfCancellationRequested();
			if (Entry.State != EntityState.Added)
				await Entry.Collection(r => r.CategoryMemberships).LoadAsync(CancellationToken);
			StageMemberships(Context, Entry.Entity);
		}
		Context.ChangeTracker.DetectChanges();
		return Result;
	}

	/// <summary>
	/// Captures only revisions whose content is new or changed, or whose visibility is being restored.
	/// </summary>
	/// <param name="Context">The context currently saving tracked changes.</param>
	/// <returns>A snapshot safe to iterate while membership entities are added or removed.</returns>
	private static List<EntityEntry<ArticleRevision>> GetRevisions(DbContext Context)
	{
		Context.ChangeTracker.DetectChanges();
		return [.. Context.ChangeTracker.Entries<ArticleRevision>().Where(e =>
			e.State == EntityState.Added || (e.State == EntityState.Modified &&
				(e.Property(r => r.Text).OriginalValue != e.Entity.Text ||
				 (e.Entity.IsCurrent && e.Entity.DateDeleted is null &&
				  (!e.Property(r => r.IsCurrent).OriginalValue || e.Property(r => r.DateDeleted).OriginalValue is not null)))))];
	}

	/// <summary>
	/// Reconciles distinct parsed slugs with tracked memberships, preserving IDs and handling repeated save attempts.
	/// </summary>
	/// <param name="Context">The context that will persist the revision and its memberships atomically.</param>
	/// <param name="Revision">The revision with its existing memberships loaded, or a newly added revision.</param>
	private void StageMemberships(DbContext Context, ArticleRevision Revision)
	{
		HashSet<string> Slugs = CategoryExtension.GetCategories(Markdown.Parse(Revision.Text, MarkdownPipelineFactory.GetPipeline()))
			.Select(c => c.UrlSlug!).ToHashSet(StringComparer.Ordinal);
		foreach (ArticleCategoryMembership Membership in Revision.CategoryMemberships.ToArray())
		{
			if (Slugs.Remove(Membership.CategorySlug)) continue;
			Revision.CategoryMemberships.Remove(Membership);
			Context.Remove(Membership);
		}
		foreach (string Slug in Slugs)
		{
			Revision.CategoryMemberships.Add(new() { CategorySlug = Slug });
		}
	}
}
