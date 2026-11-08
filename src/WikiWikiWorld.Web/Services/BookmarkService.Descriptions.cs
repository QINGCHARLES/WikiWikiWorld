using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>Reads selected-list introductions and applies owner-scoped Markdown edits.</summary>
public sealed partial class BookmarkService
{
	/// <summary>Loads one list's metadata without loading descriptions for the list selector or requiring any articles.</summary>
	/// <param name="ListId">Selected list identity; zero selects the owner's default list or its unsaved placeholder.</param>
	/// <param name="OwnerId">Profile owner.</param>
	/// <param name="IsOwner">Ownership established by the authenticated caller, never by request input.</param>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>Metadata with original Markdown source; null for inaccessible, deleted, or missing lists.</returns>
	/// <remarks>Site and soft-delete filters remain enabled. Callers render authorized source with <see cref="IMarkdownPipelineFactory.RenderUserContentMarkdown"/>.</remarks>
	public async Task<BookmarkListDetails?> GetListDetailsAsync(int ListId, Guid OwnerId, bool IsOwner, CancellationToken CancellationToken)
	{
		BookmarkListDetails? Details = await Context.BookmarkLists.AsNoTracking()
			.Where(l => l.CreatedByUserId == OwnerId && (IsOwner || l.IsPublic) &&
				(ListId == 0 ? IsOwner && l.IsDefault : l.Id == ListId))
			.Select(l => new BookmarkListDetails(l.Id, l.Name, l.IsDefault, l.IsPublic, l.Description))
			.SingleOrDefaultAsync(CancellationToken);
		return Details ?? (ListId == 0 && IsOwner ? new(0, DefaultListName, true, false, null) : null);
	}

	/// <summary>Changes only an active owned list's introduction and modification time, creating the default list on demand.</summary>
	/// <param name="ListId">List identity; zero selects the user's default list.</param>
	/// <param name="Description">Markdown source; null or whitespace clears the description, while other whitespace is preserved.</param>
	/// <param name="UserId">Authenticated owner.</param>
	/// <param name="SiteId">Current site.</param>
	/// <param name="CancellationToken">Cancels the write and any default-list creation.</param>
	/// <returns>True when an active list belonging to this owner and site was updated; otherwise false.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The source exceeds <see cref="BookmarkList.MaxDescriptionLength"/> UTF-16 code units.</exception>
	/// <remarks>The set-based update bypasses tracked list instances; subsequent reads must query the database rather than reuse tracked values.</remarks>
	public Task<bool> SetListDescriptionAsync(int ListId, string? Description, Guid UserId, int SiteId, CancellationToken CancellationToken)
	{
		string? NormalizedDescription = NormalizeListDescription(Description);
		return ExecuteListTransactionAsync(async Token =>
		{
			int ActualListId = ListId == 0 ? (await GetOrCreateDefaultListAsync(UserId, SiteId, Token)).Id : ListId;
			return await Context.BookmarkLists
				.Where(l => l.Id == ActualListId && l.CreatedByUserId == UserId && l.SiteId == SiteId)
				.ExecuteUpdateAsync(s => s.SetProperty(l => l.Description, NormalizedDescription)
					.SetProperty(l => l.DateModified, DateTimeOffset.UtcNow), Token) > 0;
		}, CancellationToken);
	}

	/// <summary>Validates the source length and represents absence without trimming meaningful Markdown whitespace.</summary>
	/// <param name="Description">Unrendered user input.</param>
	/// <returns>Null for blank input, otherwise the unchanged source.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The source exceeds the list description limit.</exception>
	private static string? NormalizeListDescription(string? Description)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(Description?.Length ?? 0, BookmarkList.MaxDescriptionLength, nameof(Description));
		return string.IsNullOrWhiteSpace(Description) ? null : Description;
	}
}
