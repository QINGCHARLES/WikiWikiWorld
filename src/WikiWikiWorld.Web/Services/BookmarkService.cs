using Ardalis.Specification.EntityFrameworkCore;
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
	/// Toggles the bookmark status of an article for a user on the current site.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical identifier of the article being bookmarked.</param>
	/// <param name="UserId">The acting user's identifier.</param>
	/// <param name="SiteId">The current site identifier.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> if the article is now bookmarked, <see langword="false"/> if it was removed.</returns>
	public async Task<bool> ToggleBookmarkAsync(Guid CanonicalArticleId, Guid UserId, int SiteId, CancellationToken CancellationToken)
	{
		BookmarkList List = await GetOrCreateDefaultListAsync(UserId, SiteId, CancellationToken);

		BookmarkByListAndArticleSpec Spec = new(List.Id, CanonicalArticleId);
		Bookmark? Existing = await Context.Bookmarks.WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);

		if (Existing is not null)
		{
			Existing.DateDeleted = DateTimeOffset.UtcNow;
			Context.Bookmarks.Update(Existing);

			await Context.SaveChangesAsync(CancellationToken);

			return false;
		}

		Bookmark NewBookmark = new()
		{
			BookmarkListId = List.Id,
			SiteId = SiteId,
			CanonicalArticleId = CanonicalArticleId,
			CreatedByUserId = UserId,
			DateCreated = DateTimeOffset.UtcNow,
		};

		Context.Bookmarks.Add(NewBookmark);

		await Context.SaveChangesAsync(CancellationToken);

		return true;
	}

	/// <summary>
	/// Determines whether the user currently has an active bookmark for the given article on the current site.
	/// </summary>
	/// <param name="CanonicalArticleId">The canonical identifier of the article.</param>
	/// <param name="UserId">The acting user's identifier.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns><see langword="true"/> if an active bookmark exists; otherwise <see langword="false"/>.</returns>
	public async Task<bool> IsBookmarkedAsync(Guid CanonicalArticleId, Guid UserId, CancellationToken CancellationToken)
	{
		return await Context.Bookmarks
			.AsNoTracking()
			.AnyAsync(b => b.CanonicalArticleId == CanonicalArticleId && b.CreatedByUserId == UserId, CancellationToken);
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

		await Context.SaveChangesAsync(CancellationToken);

		return NewList;
	}
}
