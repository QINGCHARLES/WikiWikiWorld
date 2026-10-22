using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Coordinates bookmark read/write workflows.
/// </summary>
/// <param name="Context">The database context.</param>
public sealed class BookmarkService(WikiWikiWorldDbContext Context)
{
	/// <summary>
	/// The display name used when lazily creating a user's default bookmark list.
	/// </summary>
	public const string DefaultListName = "Bookmarks";

	/// <summary>
	/// SQLite's extended result code for a unique constraint or unique index violation (<c>SQLITE_CONSTRAINT_UNIQUE</c>).
	/// </summary>
	private const int SqliteConstraintUniqueErrorCode = 2067;

	/// <summary>
	/// Puts the article in, or takes it out of, the user's default list on the current site.
	/// </summary>
	/// <remarks>
	/// Callers send the state the user asked for rather than a toggle, so repeated or concurrent requests with the same
	/// intent (a double click, a second tab, mirrored browser panes) all settle on that state instead of flipping it back.
	/// </remarks>
	/// <param name="CanonicalArticleId">The canonical identifier of the article being bookmarked.</param>
	/// <param name="UserId">The acting user's identifier.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="Saved"><see langword="true"/> to save the article in the default list; <see langword="false"/> to remove it.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the default list holds the requested state.</returns>
	public async Task SetBookmarkAsync(Guid CanonicalArticleId, Guid UserId, int SiteId, bool Saved, CancellationToken CancellationToken)
	{
		BookmarkList List = await GetOrCreateDefaultListAsync(UserId, SiteId, CancellationToken);

		await SetInListAsync(List.Id, CanonicalArticleId, UserId, SiteId, Saved, CancellationToken);
	}

	/// <summary>
	/// Puts the article in, or takes it out of, a specific list owned by the user on the current site.
	/// </summary>
	/// <param name="BookmarkListId">The target list; it must belong to <paramref name="UserId"/> and the current site.</param>
	/// <param name="CanonicalArticleId">The canonical identifier of the article being bookmarked.</param>
	/// <param name="UserId">The acting user's identifier.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="Saved"><see langword="true"/> to save the article in the list; <see langword="false"/> to remove it.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>
	/// <see langword="true"/> when the list now holds the requested state, or <see langword="false"/> when no active list
	/// with that identifier is owned by the user, in which case nothing changes.
	/// </returns>
	public async Task<bool> SetBookmarkInListAsync(int BookmarkListId, Guid CanonicalArticleId, Guid UserId, int SiteId, bool Saved, CancellationToken CancellationToken)
	{
		bool OwnsList = await Context.BookmarkLists
			.AnyAsync(l => l.Id == BookmarkListId && l.CreatedByUserId == UserId, CancellationToken);

		if (!OwnsList)
		{
			return false;
		}

		await SetInListAsync(BookmarkListId, CanonicalArticleId, UserId, SiteId, Saved, CancellationToken);

		return true;
	}

	/// <summary>
	/// Lists the user's bookmark lists on the current site with whether each one contains the given article.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical identifier of the article.</param>
	/// <param name="UserId">The owner's user identifier.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>
	/// The default list first, then the other lists in creation order. The default list is always present: when the user
	/// has not bookmarked anything yet it is a placeholder with identifier 0, created on the first toggle.
	/// </returns>
	public async Task<IReadOnlyList<BookmarkListMembership>> GetListMembershipsAsync(Guid CanonicalArticleId, Guid UserId, CancellationToken CancellationToken)
	{
		List<BookmarkListMembership> Lists = await Context.BookmarkLists
			.AsNoTracking()
			.Where(l => l.CreatedByUserId == UserId)
			.OrderByDescending(l => l.IsDefault)
			.ThenBy(l => l.Id)
			.Select(l => new BookmarkListMembership(
				l.Id,
				l.Name,
				l.IsDefault,
				l.Bookmarks.Any(b => b.CanonicalArticleId == CanonicalArticleId)))
			.ToListAsync(CancellationToken);

		if (Lists is [] or [{ IsDefault: false }, ..])
		{
			Lists.Insert(0, new BookmarkListMembership(0, DefaultListName, IsDefault: true, ContainsArticle: false));
		}

		return Lists;
	}

	/// <summary>
	/// Retrieves the user's active bookmarks for the current site, newest first.
	/// </summary>
	/// <param name="UserId">The owner's user identifier.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The user's bookmarks ordered by most recently added.</returns>
	public async Task<IReadOnlyList<Bookmark>> GetUserBookmarksAsync(Guid UserId, CancellationToken CancellationToken)
	{
		BookmarksByUserSpec Spec = new(UserId);
		return await Context.Bookmarks.WithSpecification(Spec).ToListAsync(CancellationToken);
	}

	/// <summary>
	/// Soft-deletes a bookmark, validating that the caller owns it.
	/// </summary>
	/// <param name="BookmarkId">The identifier of the bookmark to remove.</param>
	/// <param name="UserId">The acting user's identifier (must match <see cref="Bookmark.CreatedByUserId"/>).</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> if a bookmark was removed; <see langword="false"/> if no matching active bookmark was found.</returns>
	public async Task<bool> RemoveBookmarkAsync(int BookmarkId, Guid UserId, CancellationToken CancellationToken)
	{
		Bookmark? Bookmark = await Context.Bookmarks
			.FirstOrDefaultAsync(b => b.Id == BookmarkId && b.CreatedByUserId == UserId, CancellationToken);

		if (Bookmark is null)
		{
			return false;
		}

		Bookmark.DateDeleted = DateTimeOffset.UtcNow;
		Context.Bookmarks.Update(Bookmark);

		await Context.SaveChangesAsync(CancellationToken);

		return true;
	}

	/// <summary>
	/// Makes the list hold the requested state for the article: adds an active bookmark if one is wanted and missing,
	/// soft-deletes the active one if it is unwanted, and otherwise changes nothing.
	/// </summary>
	/// <remarks>
	/// Concurrent adds race between the existence check and the insert; the losers hit
	/// <c>UX_Bookmarks_BookmarkListId_CanonicalArticleId_Active</c>, which means another request already saved it, so they
	/// succeed without writing.
	/// </remarks>
	/// <param name="BookmarkListId">The list to change; the caller has already verified ownership.</param>
	/// <param name="CanonicalArticleId">The canonical identifier of the article.</param>
	/// <param name="UserId">The acting user's identifier, recorded on a new bookmark.</param>
	/// <param name="SiteId">The current site identifier, denormalized onto a new bookmark.</param>
	/// <param name="Saved">Whether the article should be in the list.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the list holds the requested state.</returns>
	private async Task SetInListAsync(int BookmarkListId, Guid CanonicalArticleId, Guid UserId, int SiteId, bool Saved, CancellationToken CancellationToken)
	{
		BookmarkByListAndArticleSpec Spec = new(BookmarkListId, CanonicalArticleId);
		Bookmark? Existing = await Context.Bookmarks.WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);

		if (Existing is not null)
		{
			if (!Saved)
			{
				Existing.DateDeleted = DateTimeOffset.UtcNow;
				Context.Bookmarks.Update(Existing);

				await Context.SaveChangesAsync(CancellationToken);
			}

			return;
		}

		if (!Saved)
		{
			return;
		}

		Bookmark NewBookmark = new()
		{
			BookmarkListId = BookmarkListId,
			SiteId = SiteId,
			CanonicalArticleId = CanonicalArticleId,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
		};

		Context.Bookmarks.Add(NewBookmark);

		try
		{
			await Context.SaveChangesAsync(CancellationToken);
		}
		catch (DbUpdateException Exception) when (IsUniqueViolation(Exception))
		{
			Context.Entry(NewBookmark).State = EntityState.Detached;
		}
	}

	/// <summary>
	/// Determines whether a failed save was rejected by a SQLite unique constraint or unique index.
	/// </summary>
	/// <param name="Exception">The exception thrown by <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.</param>
	/// <returns><see langword="true"/> for <c>SQLITE_CONSTRAINT_UNIQUE</c>; otherwise <see langword="false"/>.</returns>
	private static bool IsUniqueViolation(DbUpdateException Exception) =>
		Exception.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUniqueErrorCode };

	/// <summary>
	/// Returns the user's default list on the current site, creating it on first use.
	/// </summary>
	/// <param name="UserId">The owner's user identifier.</param>
	/// <param name="SiteId">The current site identifier for a newly created list.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The existing or newly saved default list.</returns>
	/// <remarks>
	/// If a concurrent request creates the list first, the insert hits
	/// <c>UX_BookmarkLists_SiteId_CreatedByUserId_Default_Active</c> and the winner's list is returned instead.
	/// </remarks>
	private async Task<BookmarkList> GetOrCreateDefaultListAsync(Guid UserId, int SiteId, CancellationToken CancellationToken)
	{
		DefaultBookmarkListByUserSpec Spec = new(UserId);
		BookmarkList? Existing = await Context.BookmarkLists.WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);

		if (Existing is not null)
		{
			return Existing;
		}

		BookmarkList NewList = new()
		{
			SiteId = SiteId,
			Name = DefaultListName,
			IsDefault = true,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
		};

		Context.BookmarkLists.Add(NewList);

		try
		{
			await Context.SaveChangesAsync(CancellationToken);
		}
		catch (DbUpdateException Exception) when (IsUniqueViolation(Exception))
		{
			Context.Entry(NewList).State = EntityState.Detached;

			return await Context.BookmarkLists.WithSpecification(Spec).FirstAsync(CancellationToken);
		}

		return NewList;
	}
}
