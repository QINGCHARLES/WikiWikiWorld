using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>Shows visible bookmark lists and handles owner-only forms with progressive pagination.</summary>
/// <param name="SiteResolverService">Resolves the current site and culture.</param>
/// <param name="BookmarkService">Reads and manages scoped bookmark lists.</param>
/// <param name="ProfileService">Loads profile identity and ownership.</param>
/// <param name="DataProtectionProvider">Protects expiring, owner-bound undo commands.</param>
public sealed class BookmarksModel(
	SiteResolverService SiteResolverService,
	BookmarkService BookmarkService,
	ProfileService ProfileService,
	IDataProtectionProvider DataProtectionProvider) : BasePageModel(SiteResolverService)
{
	/// <summary>Gets or sets the profile route identity.</summary>
	[BindProperty(SupportsGet = true)]
	public string Username { get; set; } = string.Empty;

	/// <summary>Gets or sets the optional stable list route identity.</summary>
	[BindProperty(SupportsGet = true, Name = "listid")]
	public int? ListId { get; set; }

	/// <summary>Gets or sets the requested ordering; only name and recent are accepted.</summary>
	[BindProperty(SupportsGet = true, Name = "sort")]
	public string? Sort { get; set; } = "recent";

	/// <summary>Gets or sets the one-based page number, clamped to the selected list.</summary>
	[BindProperty(SupportsGet = true, Name = "pagenumber")]
	public int PageNumber { get; set; } = 1;

	/// <summary>Gets the profile identity shared by the profile header.</summary>
	public ProfileHeader Header { get; private set; } = null!;

	/// <summary>Gets only the lists readable by the current viewer.</summary>
	public IReadOnlyList<BookmarkListSummary> Lists { get; private set; } = [];

	/// <summary>Gets the selected list, or null when a visitor has no public lists to read.</summary>
	public BookmarkListSummary? SelectedList { get; private set; }

	/// <summary>Gets the current bounded page of entries.</summary>
	public BookmarkEntryPage Entries { get; private set; } = new([], 0, 1);

	/// <summary>Gets the operation awaiting explicit confirmation; no mutation has happened yet.</summary>
	public string? PendingOperation { get; private set; }

	/// <summary>Gets the total memberships affected across cultures for confirmation.</summary>
	public int AffectedCount { get; private set; }

	/// <summary>Gets a form validation error without discarding the submitted list name.</summary>
	public string? Error { get; private set; }

	/// <summary>Gets the submitted new-list name after validation fails.</summary>
	public string? NewListName { get; private set; }

	/// <summary>Gets whether the submitted new list was explicitly public.</summary>
	public bool NewListIsPublic { get; private set; }

	/// <summary>Gets or sets the next-page mutation confirmation.</summary>
	[TempData]
	public string? Notice { get; set; }

	/// <summary>Gets or sets a five-minute undo command, protected against tampering.</summary>
	[TempData]
	public string? UndoToken { get; set; }

	/// <summary>Gets the canonical URL for the rendered list, sort, and actual page.</summary>
	public string CurrentUrl => ListUrl(SelectedList, Sort, Entries.PageNumber);

	/// <summary>Loads a complete page or the matching Datastar fragment without exposing private list identities.</summary>
	/// <param name="CancellationToken">Cancels request work.</param>
	/// <returns>The page, a fragment, or a missing-profile/list response.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		IActionResult? Failure = await LoadAsync(CancellationToken);
		if (Failure is not null)
			return Failure;
		if (Request.Headers["Datastar-Request"] != "true" && PageNumber != Entries.PageNumber)
			return LocalRedirect(CurrentUrl);
		return Request.Headers["Datastar-Request"] == "true" ? Partial("_BookmarkBrowser", this) : Page();
	}

	/// <summary>Validates ownership and antiforgery-protected form intent before changing a list or membership.</summary>
	/// <param name="Operation">Create, visibility, delete, remove, or undo.</param>
	/// <param name="Name">New list name.</param>
	/// <param name="IsPublic">Requested list visibility.</param>
	/// <param name="BookmarkId">Membership to remove.</param>
	/// <param name="Token">Protected undo token returned after removal.</param>
	/// <param name="Confirmed">Whether the user submitted the explicit publish/delete confirmation.</param>
	/// <param name="CancellationToken">Cancels request work.</param>
	/// <returns>A redirect after mutation, a confirmation/validation page, or an access failure.</returns>
	public async Task<IActionResult> OnPostAsync(string Operation, string? Name, bool IsPublic, int BookmarkId, string? Token, bool Confirmed, CancellationToken CancellationToken)
	{
		IActionResult? Failure = await LoadAsync(CancellationToken);
		if (Failure is not null)
			return Failure;
		if (!Header.IsViewerAuthenticated)
			return Challenge();
		if (!Header.IsOwner || SelectedList is null)
			return Forbid();
		if (!ModelState.IsValid)
			return BadRequest();

		int SelectedId = SelectedList.Id;
		switch (Operation)
		{
			case "create":
				BookmarkListCreation Creation = await BookmarkService.CreateEmptyListAsync(Name, IsPublic, Header.UserId, SiteId, CancellationToken);
				if (Creation.ListId is not { } CreatedId)
				{
					Error = Creation.Error;
					NewListName = Name;
					NewListIsPublic = IsPublic;
					return Page();
				}
				Notice = "List created.";
				return LocalRedirect(ListUrl(new(CreatedId, Name!, false, IsPublic, 0)));
			case "visibility":
			case "delete":
				if (!Confirmed && (Operation == "delete" || IsPublic && !SelectedList.IsPublic))
				{
					PendingOperation = Operation;
					AffectedCount = await BookmarkService.CountListMembershipsAsync(SelectedId, Header.UserId, CancellationToken);
					return Page();
				}
				bool Changed = Operation == "visibility"
					? await BookmarkService.SetListVisibilityAsync(SelectedId, IsPublic, Header.UserId, SiteId, CancellationToken)
					: SelectedId == 0 || await BookmarkService.DeleteOrClearListAsync(SelectedId, Header.UserId, CancellationToken);
				if (!Changed)
					return NotFound();
				Notice = Operation == "visibility" ? (IsPublic ? "List is now public." : "List is now private.")
					: SelectedList.IsDefault ? "Default list cleared." : "List deleted.";
				UndoToken = null;
				return LocalRedirect(Operation == "delete" ? ListUrl(null) : CurrentUrl);
			case "remove":
				DateTimeOffset? DeletedAt = await BookmarkService.RemoveEntryAsync(SelectedId, BookmarkId, Header.UserId, CancellationToken);
				if (DeletedAt is { } Deleted)
				{
					UndoToken = Protector().Protect($"{Header.UserId:D}|{SelectedId}|{BookmarkId}|{Deleted:O}", TimeSpan.FromMinutes(5));
					Notice = "Removed from this list. You can undo this for five minutes.";
				}
				else
				{
					Notice = "This entry was already removed.";
				}
				return LocalRedirect(CurrentUrl);
			case "undo":
				bool Restored = await UndoAsync(SelectedId, Token, CancellationToken);
				UndoToken = null;
				Notice = Restored ? "Bookmark restored." : "Undo expired or the list has changed.";
				return LocalRedirect(CurrentUrl);
			default:
				return BadRequest();
		}
	}

	/// <summary>Creates a stable, lowercase-query URL, omitting default sort/page values.</summary>
	/// <param name="List">Selected list; null or default uses the base URL.</param>
	/// <param name="Order">Name or recent.</param>
	/// <param name="Page">One-based destination page.</param>
	/// <returns>A local URL safe for links and history updates.</returns>
	public string ListUrl(BookmarkListSummary? List, string? Order = "recent", int Page = 1)
	{
		string Path = $"/@{Uri.EscapeDataString(Header.UserName)}/bookmarks";
		if (List is { IsDefault: false })
			Path += $"/{List.Id.ToString(CultureInfo.InvariantCulture)}";
		List<string> Query = [];
		if (Order == "name")
			Query.Add("sort=name");
		if (Page > 1)
			Query.Add($"pagenumber={Page.ToString(CultureInfo.InvariantCulture)}");
		return Query.Count == 0 ? Path : $"{Path}?{string.Join('&', Query)}";
	}

	/// <summary>Loads only accessible lists, normalizes paging, and prevents caching of private/fragment responses.</summary>
	/// <param name="CancellationToken">Cancels database work.</param>
	/// <returns>An access failure or null when the view is ready.</returns>
	private async Task<IActionResult?> LoadAsync(CancellationToken CancellationToken)
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers.Vary = "Datastar-Request";
		AllowSearchEngineIndexingOfPage = false;
		ProfileHeader? Loaded = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
		if (Loaded is null)
			return NotFound();
		Header = Loaded;
		Lists = await BookmarkService.GetVisibleListsAsync(Header.UserId, Header.IsOwner, CancellationToken);
		SelectedList = ListId is { } Id ? Lists.FirstOrDefault(l => l.Id == Id) : Lists.FirstOrDefault();
		if (ListId.HasValue && SelectedList is null)
			return NotFound();
		Sort = Sort == "name" ? "name" : "recent";
		if (SelectedList is not null)
		{
			Entries = await BookmarkService.GetListPageAsync(SelectedList.Id, Header.UserId, Header.IsOwner, Sort, PageNumber, CancellationToken);
		}
		return null;
	}

	/// <summary>Returns a purpose-isolated expiring protector for membership removals.</summary>
	/// <returns>A protector whose tokens are only accepted by this feature.</returns>
	private ITimeLimitedDataProtector Protector() => DataProtectionProvider.CreateProtector("BookmarkUndo.v1").ToTimeLimitedDataProtector();

	/// <summary>Checks token expiry, owner, selected list, and exact deletion before restoring.</summary>
	/// <param name="SelectedId">Current list.</param>
	/// <param name="Token">User-submitted protected command.</param>
	/// <param name="CancellationToken">Cancels restoration.</param>
	/// <returns>Whether restoration succeeded.</returns>
	private async Task<bool> UndoAsync(int SelectedId, string? Token, CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Token))
			return false;
		try
		{
			string[] Parts = Protector().Unprotect(Token).Split('|');
			return Parts.Length == 4 && Guid.TryParse(Parts[0], out Guid Owner) && Owner == Header.UserId
				&& int.TryParse(Parts[1], out int List) && List == SelectedId
				&& int.TryParse(Parts[2], out int Bookmark)
				&& DateTimeOffset.TryParseExact(Parts[3], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset Deleted)
				&& await BookmarkService.RestoreEntryAsync(SelectedId, Bookmark, Deleted, Owner, CancellationToken);
		}
		catch (CryptographicException)
		{
			return false;
		}
	}
}
