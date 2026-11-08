using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>Edits the Markdown reason for one saved membership without changing its save date or list header.</summary>
public sealed partial class BookmarkService
{
	/// <summary>Sets or clears notes on an active owned bookmark whose parent list is also active and owned on the current site.</summary>
	/// <param name="BookmarkId">The membership identity, not the article or list identity.</param>
	/// <param name="Notes">Markdown source; blank input clears the note, while other whitespace is preserved.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="CancellationToken">Cancels database reads and saving.</param>
	/// <returns>True when an eligible bookmark was found and saved; otherwise false without creating a membership.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The source exceeds <see cref="Bookmark.MaxNotesLength"/> UTF-16 code units.</exception>
	/// <remarks>Only note source changes. Callers render authorized notes with <see cref="IMarkdownPipelineFactory.RenderUserContentMarkdown"/>.</remarks>
	public async Task<bool> SetBookmarkNotesAsync(int BookmarkId, string? Notes, Guid UserId, CancellationToken CancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(Notes?.Length ?? 0, Bookmark.MaxNotesLength, nameof(Notes));
		string? NormalizedNotes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
		Bookmark? Membership = await Context.Bookmarks.FirstOrDefaultAsync(b => b.Id == BookmarkId && b.CreatedByUserId == UserId &&
			Context.BookmarkLists.Any(l => l.Id == b.BookmarkListId && l.CreatedByUserId == UserId && l.SiteId == b.SiteId), CancellationToken);
		if (Membership is null)
			return false;

		Membership.Notes = NormalizedNotes;
		await Context.SaveChangesAsync(CancellationToken);
		return true;
	}
}
