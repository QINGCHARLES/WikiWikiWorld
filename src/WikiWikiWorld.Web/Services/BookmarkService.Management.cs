using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>Reads visible list pages and performs owner-scoped list management.</summary>
public sealed partial class BookmarkService
{
	/// <summary>The fixed number of bookmark entries loaded per page.</summary>
	public const int PageSize = 50;

	/// <summary>Loads only lists the viewer may read, including an unsaved default placeholder for owners.</summary>
	/// <param name="OwnerId">Profile owner.</param>
	/// <param name="IsOwner">Whether the authenticated viewer owns the profile.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>Default first, followed by named lists; private list names and counts are excluded for visitors.</returns>
	public async Task<IReadOnlyList<BookmarkListSummary>> GetVisibleListsAsync(Guid OwnerId, bool IsOwner, CancellationToken CancellationToken)
	{
		List<BookmarkListSummary> Lists = await Context.BookmarkLists.AsNoTracking()
			.Where(l => l.CreatedByUserId == OwnerId && (IsOwner || l.IsPublic))
			.Select(l => new BookmarkListSummary(l.Id, l.Name, l.IsDefault, l.IsPublic,
				l.Bookmarks.Count(b => Context.ArticleRevisions.Any(r => r.IsCurrent && r.CanonicalArticleId == b.CanonicalArticleId))))
			.ToListAsync(CancellationToken);
		Lists = [.. Lists.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name, StringComparer.InvariantCultureIgnoreCase).ThenBy(l => l.Id)];
		if (IsOwner && !Lists.Any(l => l.IsDefault))
		{
			Lists.Insert(0, new(0, DefaultListName, true, false, 0));
		}
		return Lists;
	}

	/// <summary>Loads a page after visibility, site, culture, and current-article filtering.</summary>
	/// <param name="ListId">List to read.</param>
	/// <param name="OwnerId">Profile owner.</param>
	/// <param name="IsOwner">Whether the authenticated viewer owns the profile.</param>
	/// <param name="Sort">Name for invariant Unicode case-insensitive title ordering; otherwise recent saves first.</param>
	/// <param name="PageNumber">One-based requested page, clamped after counting.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>A bounded projection; inaccessible lists yield no entries.</returns>
	public async Task<BookmarkEntryPage> GetListPageAsync(int ListId, Guid OwnerId, bool IsOwner, string Sort, int PageNumber, CancellationToken CancellationToken)
	{
		var Entries = Context.Bookmarks.AsNoTracking()
			.Where(b => b.BookmarkListId == ListId && b.CreatedByUserId == OwnerId &&
				Context.BookmarkLists.Any(l => l.Id == b.BookmarkListId && l.CreatedByUserId == OwnerId && (IsOwner || l.IsPublic)))
			.Join(Context.ArticleRevisions.Where(r => r.IsCurrent), b => b.CanonicalArticleId, r => r.CanonicalArticleId,
				(b, r) => new { b.Id, r.Title, r.UrlSlug, r.Type, b.DateCreated });
		int Total = await Entries.CountAsync(CancellationToken);
		int ActualPage = Math.Clamp(PageNumber, 1, Math.Max(1, (Total + PageSize - 1) / PageSize));

		// Register on this context's connection before executing SQL; no persistent collation/index dependency.
		// Invariant Unicode comparison also folds non-ASCII case, unlike SQLite's built-in NOCASE.
		((SqliteConnection)Context.Database.GetDbConnection()).CreateCollation("BOOKMARK_TITLE",
			static (x, y) => string.Compare(x, y, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase));
		var Ordered = Sort == "name"
			? Entries.OrderBy(b => EF.Functions.Collate(b.Title, "BOOKMARK_TITLE")).ThenBy(b => b.Id)
			: Entries.OrderByDescending(b => b.DateCreated).ThenByDescending(b => b.Id);
		List<BookmarkEntry> Items = await Ordered.Skip((ActualPage - 1) * PageSize).Take(PageSize)
			.Select(b => new BookmarkEntry(b.Id, b.Title, b.UrlSlug, b.Type, b.DateCreated)).ToListAsync(CancellationToken);
		return new(Items, Total, ActualPage);
	}

	/// <summary>Creates an empty list, keeping duplicate-name and list-limit rules shared with the article menu.</summary>
	/// <param name="Name">User-supplied name, trimmed before validation.</param>
	/// <param name="IsPublic">Explicit visibility selection.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="SiteId">Current site.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>The new identity or a validation error.</returns>
	public async Task<BookmarkListCreation> CreateEmptyListAsync(string? Name, bool IsPublic, Guid UserId, int SiteId, CancellationToken CancellationToken)
	{
		string Trimmed = Name?.Trim() ?? string.Empty;
		if (Trimmed.Length is 0 or > MaxListNameLength)
			return new(null, $"Enter a list name between 1 and {MaxListNameLength} characters.");

		await ListCreationGate.WaitAsync(CancellationToken);
		try
		{
			await GetOrCreateDefaultListAsync(UserId, SiteId, CancellationToken);
			List<string> Names = await Context.BookmarkLists.Where(l => l.CreatedByUserId == UserId).Select(l => l.Name).ToListAsync(CancellationToken);
			if (Names.Contains(Trimmed, StringComparer.OrdinalIgnoreCase))
				return new(null, "You already have a list with that name. Choose a different name.");
			if (Names.Count >= MaxListsPerUser)
				return new(null, $"You can have up to {MaxListsPerUser} lists, including Bookmarks.");
			BookmarkList List = new()
			{
				Name = Trimmed, IsPublic = IsPublic, CreatedByUserId = UserId, SiteId = SiteId, DateCreated = DateTimeOffset.UtcNow
			};
			Context.BookmarkLists.Add(List);
			await Context.SaveChangesAsync(CancellationToken);
			return new(List.Id, null);
		}
		finally
		{
			ListCreationGate.Release();
		}
	}

	/// <summary>Changes visibility only on an active list owned by the caller, creating a default placeholder on demand.</summary>
	/// <param name="ListId">List identity; zero selects the default list.</param>
	/// <param name="IsPublic">Requested visibility.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="SiteId">Current site.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>False when the list is unavailable to the caller.</returns>
	public async Task<bool> SetListVisibilityAsync(int ListId, bool IsPublic, Guid UserId, int SiteId, CancellationToken CancellationToken)
	{
		if (ListId == 0)
			ListId = (await GetOrCreateDefaultListAsync(UserId, SiteId, CancellationToken)).Id;
		return await Context.BookmarkLists.Where(l => l.Id == ListId && l.CreatedByUserId == UserId)
			.ExecuteUpdateAsync(s => s.SetProperty(l => l.IsPublic, IsPublic).SetProperty(l => l.DateModified, DateTimeOffset.UtcNow), CancellationToken) > 0;
	}

	/// <summary>Soft-deletes one membership in the selected active list, without affecting its other memberships.</summary>
	/// <param name="ListId">Selected list.</param>
	/// <param name="BookmarkId">Membership to remove.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>The deletion instant used to bind an undo token, or null if nothing was removed.</returns>
	public async Task<DateTimeOffset?> RemoveEntryAsync(int ListId, int BookmarkId, Guid UserId, CancellationToken CancellationToken)
	{
		DateTimeOffset Now = DateTimeOffset.UtcNow;
		int Changed = await Context.Bookmarks.Where(b => b.Id == BookmarkId && b.BookmarkListId == ListId && b.CreatedByUserId == UserId &&
			Context.BookmarkLists.Any(l => l.Id == ListId && l.CreatedByUserId == UserId))
			.ExecuteUpdateAsync(s => s.SetProperty(b => b.DateDeleted, Now), CancellationToken);
		return Changed > 0 ? Now : null;
	}

	/// <summary>Restores exactly the removed membership, retaining its save date and tolerating an already re-added article.</summary>
	/// <param name="ListId">Selected active list.</param>
	/// <param name="BookmarkId">Removed membership from a protected, short-lived undo token.</param>
	/// <param name="DeletedAt">Deletion instant from the token; a later clear/delete must not be undone accidentally.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>Whether the article is restored or was already present.</returns>
	public async Task<bool> RestoreEntryAsync(int ListId, int BookmarkId, DateTimeOffset DeletedAt, Guid UserId, CancellationToken CancellationToken)
	{
		return await ExecuteListTransactionAsync(async Token =>
		{
			if (!await Context.BookmarkLists.AnyAsync(l => l.Id == ListId && l.CreatedByUserId == UserId, Token))
				return false;
			Bookmark? Removed = await Context.Bookmarks.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
				.FirstOrDefaultAsync(b => b.Id == BookmarkId && b.BookmarkListId == ListId && b.CreatedByUserId == UserId && b.DateDeleted == DeletedAt, Token);
			if (Removed is null)
				return false;
			if (await Context.Bookmarks.AnyAsync(b => b.BookmarkListId == ListId && b.CanonicalArticleId == Removed.CanonicalArticleId, Token))
				return true;
			Removed.DateDeleted = null;
			try
			{
				await Context.SaveChangesAsync(Token);
				return true;
			}
			catch (DbUpdateException Exception) when (IsUniqueViolation(Exception))
			{
				Context.Entry(Removed).State = EntityState.Detached;
				return true;
			}
		}, CancellationToken);
	}

	/// <summary>Atomically deletes a custom list or clears the default list, including memberships in all site cultures.</summary>
	/// <param name="ListId">Active list owned by the caller.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="CancellationToken">Cancels the transaction.</param>
	/// <returns>False when the list was already removed or belongs to another owner.</returns>
	public async Task<bool> DeleteOrClearListAsync(int ListId, Guid UserId, CancellationToken CancellationToken)
	{
		return await ExecuteListTransactionAsync(async Token =>
		{
			BookmarkList? List = await Context.BookmarkLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == ListId && l.CreatedByUserId == UserId, Token);
			if (List is null)
				return false;
			DateTimeOffset Now = DateTimeOffset.UtcNow;
			// Include previously removed rows so a still-valid undo token cannot resurrect entries after clearing.
			await Context.Bookmarks.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
				.Where(b => b.BookmarkListId == ListId && b.CreatedByUserId == UserId)
				.ExecuteUpdateAsync(s => s.SetProperty(b => b.DateDeleted, Now), Token);
			if (!List.IsDefault)
			{
				await Context.BookmarkLists.Where(l => l.Id == ListId && l.CreatedByUserId == UserId)
					.ExecuteUpdateAsync(s => s.SetProperty(l => l.DateDeleted, Now), Token);
			}
			return true;
		}, CancellationToken);
	}

	/// <summary>Counts active memberships across cultures for an explicit publish/delete confirmation.</summary>
	/// <param name="ListId">Selected list.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="CancellationToken">Cancels the query.</param>
	/// <returns>The number affected, including entries whose current article is unavailable.</returns>
	public Task<int> CountListMembershipsAsync(int ListId, Guid UserId, CancellationToken CancellationToken) =>
		Context.Bookmarks.CountAsync(b => b.BookmarkListId == ListId && b.CreatedByUserId == UserId &&
			Context.BookmarkLists.Any(l => l.Id == ListId && l.CreatedByUserId == UserId), CancellationToken);

	/// <summary>Runs a list mutation atomically under the configured SQLite busy/locked retry strategy.</summary>
	/// <typeparam name="TResult">Operation result.</typeparam>
	/// <param name="Operation">Work sharing the current context; must express a repeatable requested state.</param>
	/// <param name="CancellationToken">Cancels lock retries and the transaction.</param>
	/// <returns>The result of the committed operation.</returns>
	/// <remarks>Failed attempts detach only bookmark entities so rolled-back generated IDs cannot poison a retry.</remarks>
	private Task<TResult> ExecuteListTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> Operation, CancellationToken CancellationToken) =>
		Context.Database.CreateExecutionStrategy().ExecuteAsync(async Token =>
		{
			await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(Token);
			try
			{
				TResult Result = await Operation(Token);
				await Transaction.CommitAsync(Token);
				return Result;
			}
			catch
			{
				foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry in Context.ChangeTracker.Entries().ToArray())
				{
					if (Entry.Entity is Bookmark or BookmarkList)
						Entry.State = EntityState.Detached;
				}
				throw;
			}
		}, CancellationToken);
}
