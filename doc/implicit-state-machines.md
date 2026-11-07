# Implicit state-machine inventory

Reviewed 2026-11-05 against the current working tree, including uncommitted and untracked application code.

**Release status:** all five identified defects were fixed and deployed on 2026-11-06 at 20:02 UTC as `release-20261106T195612Z-8f6705dfc19f`, after the additive upload schema upgrade. [Production verification and rollback record](upload-recovery.md#production-application-release-2026-11-06). The inventory describes the original reviewed snapshot unless an update is stated.

This inventory groups related machines into 45 workflow families. A family can contain independent dimensions: for example, an image editor's operation, pointer gesture, and cached export are separate machines. Names such as **Ready** below are descriptions inferred from the code, not existing enum members.

Scope: owned C#, Razor, JavaScript, relevant CSS, models, mappings, schema definitions, and project references under `src/`. Deployment scripts and API client scripts are outside this app-runtime inventory. Vendor internals, generated compiler async/iterator machines, and every ordinary form field's browser state are not enumerated. Pure classification, such as article type or computed profile badge tier, is not treated as a lifecycle. Some infrastructure machines delegate execution to the framework; two ingestion/deployment families are only partially implemented here.

## Domain and server workflows

### D01. Article revision lifecycle

- **State:** `IsCurrent`, `DateDeleted`, the shared `CanonicalArticleId`, and the collection of revisions. A revision is current, superseded, or tombstoned; an article is absent, live, or deleted.
- **Transitions:** create inserts a current revision; edit demotes the current revision and inserts another; revert tombstones the current revision and promotes the latest visible previous revision; delete tombstones all visible revisions and clears their current flags.
- **Owners:** [ArticleRevisionService.cs](../src/WikiWikiWorld.Web/Services/ArticleRevisionService.cs), particularly `UpdateArticleAsync`, `RevertToPreviousRevisionAsync`, and `DeleteArticleAsync`; creation also occurs in [CreateEdit.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs) and [ArticleApiController.cs](../src/WikiWikiWorld.Web/Controllers/Api/ArticleApiController.cs).
- **Important invariant:** exactly one visible current revision per live canonical article. The EF current-slug index is filtered but is not declared unique; it is not a complete database-enforced transition contract. Revert is promotion of an existing row, not creation of a fresh revert revision.

### D02. File revision lifecycle

- **State:** nullable `FileRevision.IsCurrent`, `DateDeleted`, canonical ID, revision ID, and physical bytes. `null` and `false` are both non-current to existing readers.
- **Transitions:** upload demotes the current file and inserts a new current file. Article-only edits retain the canonical file reference. Historical article views select file bytes by timestamp; current views select the current file, with a newest-file fallback.
- **Owners:** [FileApiController.cs](../src/WikiWikiWorld.Web/Controllers/Api/FileApiController.cs), [CreateEdit.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs), [View.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml.cs), [FileRevision.cs](../src/WikiWikiWorld.Data/Models/FileRevision.cs).
- **Boundary:** article revert/delete does not implement a corresponding file-revision revert/delete. These are coupled but distinct lifecycles.

### D03. Upload commit, finalization, and recovery

- **State:** no upload → staged temporary bytes → committed metadata with pending bytes → finalized revision path. Failure before commit leads to cleanup; failure after commit retains staging for startup recovery. Old unclaimed staging can be deleted.
- **State storage:** nullable `StagedUpload`, captured new revision ID, database rows, and filesystem existence; there is no persisted upload-operation state.
- **Owners:** [FileUploadStorageService.cs](../src/WikiWikiWorld.Web/Services/FileUploadStorageService.cs), [FileApiController.cs](../src/WikiWikiWorld.Web/Controllers/Api/FileApiController.cs), [CreateEdit.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs), startup recovery in [Program.cs](../src/WikiWikiWorld.Web/Program.cs).
- **Concrete risk:** recovery considers only current file revisions and chooses the newest staging file matching canonical ID and extension. Staging filenames do not identify the committed revision. See finding F01.

### D04. Article editor mode and attachment workflow

**2026-11-06 update:** mode now follows the loaded/submitted `OriginalUrlSlug`; an owner's missing user page opens creation. Validation errors retain that mode and draft. See resolved F05. The bullets below describe the reviewed snapshot.

- **State:** create/edit, existing attachment/no attachment, submitted replacement/no replacement, revert available/unavailable, and validation/conflict/saved/pending-recovery outcomes.
- **Transitions:** GET derives edit mode from `UrlSlug`; POST derives it from `OriginalUrlSlug`. Selecting an upload changes the article type to File. Editing without an upload delegates to the revision service; editing with one runs its own combined article/file transaction.
- **Owner:** [CreateEdit.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs) and its Razor template.
- **Review point:** mode has two different sources, and revision replacement logic is shared only on the no-upload branch. The user-home creation branch on GET tests a nonempty `UrlSlug` while also requiring `!IsEditMode`, although `IsEditMode` means a nonempty slug; that branch is unreachable under the current definition.

### D05. Article reader and historical-file selection

- **State:** requested revision/current revision/displayed revision, `IsViewingCurrentRevision`, `IsPriorRevision`, title/slug-change flags, canonical redirect, missing page, and user-page recovery.
- **Transitions:** no valid revision timestamp requests the current article; a valid timestamp requests a specific revision, falling back to current if it is missing. Historical mode changes author cutoff, indexing, warning banners, and file selection.
- **Owner:** [View.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml.cs), `OnGetAsync`.
- **Review point:** the displayed revision and the two current/prior booleans are computed separately. Missing or malformed historical references therefore deserve explicit transition cases in future tests.

### D06. User home-page provisioning

- **State:** account absent; account exists without an article; placeholder article exists; authored article exists; provisioning failed.
- **Transitions:** registration attempts best-effort creation; article view retries on demand. Private messaging no longer provisions articles. Profile code distinguishes a placeholder from authored content by comparing the text with `GetDefaultUserArticleText`.
- **Owners:** [ArticleRevisionService.cs](../src/WikiWikiWorld.Web/Services/ArticleRevisionService.cs), [Register.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Account/Register.cshtml.cs), [ProfileService.cs](../src/WikiWikiWorld.Web/Services/ProfileService.cs).
- **Boundary:** login intentionally no longer provisions the page. A profile's “written” state is inferred from content, not stored explicitly.

### D07. Translation-group topology and merge confirmation

- **State:** unlinked, linked to the same group, linked to different groups, same-language collision, merge preview awaiting choices, refused, or successfully changed. Active and removed membership rows are distinguished by `DateDeleted`.
- **Transitions:** two lone articles create a group; a lone article joins a group when its language is free; collisions and group merges require confirmation and clash choices; change replaces one language without silently merging; removal dissolves a group left with one member.
- **Owners:** [ArticleLanguageService.cs](../src/WikiWikiWorld.Web/Services/ArticleLanguageService.cs), [ArticleLanguageModels.cs](../src/WikiWikiWorld.Web/Services/ArticleLanguageModels.cs), [Languages.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/Languages.cshtml.cs).
- **Explicit portion:** `ArticleLanguageOutcome` already names operation outcomes. Group topology and allowed transitions remain implicit in nullable group IDs, member sets, confirmation, and choices. Transactions recompute clash choices before applying a merge.

### D08. Bookmark membership and protected undo

- **State:** absent, active, removed, removed with a usable five-minute undo token, restored, or undo invalidated/expired.
- **Transitions:** set-saved creates an active membership if missing; set-unsaved timestamps deletion; undo clears the exact deletion timestamp or accepts an already-present equivalent membership; clearing a list changes deletion timestamps to invalidate old undo tokens.
- **Owners:** [BookmarkService.cs](../src/WikiWikiWorld.Web/Services/BookmarkService.cs), [BookmarkService.Management.cs](../src/WikiWikiWorld.Web/Services/BookmarkService.Management.cs), [Bookmarks.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Profile/Bookmarks.cshtml.cs).
- **Invariant:** one active membership per list/article, enforced by the partial unique index. Undo is bound to owner, list, membership, deletion instant, and token expiry.

### D09. Bookmark-list existence, identity, and visibility

- **State:** virtual default list with ID 0; persisted default list; custom list; private/public; empty/populated; active/deleted.
- **Transitions:** first use materializes the virtual default; list creation creates a custom list or reuses a same-named list from the article menu; visibility can change; delete clears the default but tombstones custom lists.
- **Owners:** [BookmarkService.cs](../src/WikiWikiWorld.Web/Services/BookmarkService.cs), [BookmarkService.Management.cs](../src/WikiWikiWorld.Web/Services/BookmarkService.Management.cs).
- **Invariant:** default-list identity is permanent once persisted; visitor visibility depends on `IsPublic`. The creation gate coordinates rare list creation within the documented single-instance deployment.

### D10. Bookmark management confirmation and creation dialog

- **State:** browsing, awaiting publish confirmation, awaiting delete/clear confirmation, new-list dialog closed/open, invalid submitted name, mutation notice, undo available.
- **Transitions:** an unconfirmed consequential POST returns a confirmation page; confirmed POST mutates and redirects; failed list creation re-renders the name and visibility; JavaScript upgrades a returned open dialog to modal.
- **Owners:** [Bookmarks.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Profile/Bookmarks.cshtml.cs), [_BookmarkBrowser.cshtml](../src/WikiWikiWorld.Web/Pages/Profile/_BookmarkBrowser.cshtml), [bookmarks.js](../src/WikiWikiWorld.Web/wwwroot/js/bookmarks.js).
- **State storage:** `PendingOperation`, `Confirmed`, `Error`, `Notice`, `UndoToken`, and native dialog state.

### D11. Registration, sign-in, lockout, session, and token validity

- **State:** unregistered/registered, anonymous/authenticated, access-failure count, locked/unlocked, environment-blocked account, token issued/expired/revoked/rejected.
- **Transitions:** registration creates the account and signs in; failed passwords contribute to configured lockout; successful login creates a cookie; token issuance captures the account security stamp; expiry, account deletion, or a changed stamp invalidates a JWT.
- **Owners:** [Register.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Account/Register.cshtml.cs), [Login.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Account/Login.cshtml.cs), [AuthApiController.cs](../src/WikiWikiWorld.Web/Controllers/Api/AuthApiController.cs), [Program.cs](../src/WikiWikiWorld.Web/Program.cs).
- **Framework-managed portion:** Identity implements much of the session and lockout machinery. Application code selects bearer versus cookie handling; an invalid bearer request deliberately does not fall back to cookies.

### D12. Device theme preference

- **State:** system/light/dark preference, plus the effective OS theme when system is selected. Preference lives in a browser cookie, not in account state.
- **Transitions:** settings POST normalizes the value, writes the cookie, and redirects with confirmation; page filter reads the preference; system preference delegates effective color scheme to CSS/browser state.
- **Owners:** [Theme.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Settings/Theme.cshtml.cs), [ThemePageFilter.cs](../src/WikiWikiWorld.Web/Infrastructure/ThemePageFilter.cs), shared layout and color CSS.

### D13. Publication submission and ingestion status — partially implemented

- **Implemented states:** form/validation failure → submitted queue row → redirect showing success. `SubmissionSucceeded` is inferred from `submitted=1`, not from a queue read.
- **Declared later states:** `SUBMITTED`, `DOWNLOADED`, `BEINGCHECKED`, `REJECTED`, `ACCEPTED`, `POSTED`; metadata includes status-change time, moderator, rejection reason, and deletion time.
- **Owners:** [Upload.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Wiki/Upload.cshtml.cs), [PublicationUploadQueue.cs](../src/WikiWikiWorld.Data/Models/PublicationUploadQueue.cs), [PublicationUploadQueueStatus.cs](../src/WikiWikiWorld.Data/Models/PublicationUploadQueueStatus.cs), [Create.sql](../src/WikiWikiWorld.Data/SqliteScripts/Create.sql).
- **Limit:** only insertion into `SUBMITTED` is implemented in the app code found here. The names do not establish a verified transition graph for moderation or ingestion; no such processor was found in this repository.

### D14. Download review, deployment, and redeployment — model only

- **Declared state:** `DownloadUrlStatusId` can refer to `RECEIVED`, `VERIFIED`, `REJECTED`, `DEPLOYED`, `DEPLOYERROR`, or `UNKNOWN`. Independent flags include `FilenameChanged`, `NeedsRedeployment`, nullable `NeedsOcr`, nullable `IsComplete`, copyright status, and soft deletion.
- **Owners:** [DownloadUrl.cs](../src/WikiWikiWorld.Data/Models/DownloadUrl.cs), [DownloadUrlStatus.cs](../src/WikiWikiWorld.Data/Models/DownloadUrlStatus.cs), [Load.sql](../src/WikiWikiWorld.Data/SqliteScripts/Load.sql).
- **Limit:** no app transition handlers were found. [DownloadsBox.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/DownloadsBox.cs) resolves and renders rows by hash without consulting deployment/review status. Do not infer a gating policy or status ordering from lookup names alone.

### D15. Public discussions and separate private conversations

- **Public workflow:** create article subject/root post; replies validate an optional parent in the same subject. Readers omit deleted rows. Unused subject edit metadata is removed; post edit metadata remains rendered.
- **Private workflow:** resolve/deduplicate recipients → atomically save conversation, fixed participants and first message → member-only thread. Reads/replies check site/culture and membership. Inbox/started folders query private conversations, not talk subjects; sending does not create user articles.
- **Owners:** [PrivateMessagingService.cs](../src/WikiWikiWorld.Web/Services/PrivateMessagingService.cs), [NewMessage.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Profile/NewMessage.cshtml.cs), [Conversation.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Profile/Conversation.cshtml.cs), [TalkSubjects.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/TalkSubjects.cshtml.cs), [TalkSubject.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/TalkSubject.cshtml.cs).
- **Limit:** no private edit/delete, member-management, read-receipt or archive state. See [cutover and verification](private-messaging.md).

## Browser interaction workflows

### U01. Article bookmark write requests

- **State:** disabled without enhancement; signed-out prompt; saved/unsaved; idle/pending; server validation message; ambiguous network failure.
- **Transitions:** click sends desired state, pending disables mutation controls, matching Razor patches refresh bookmark state while preserving the open menu; errors instruct reload before another attempt. Creation has its own hidden/visible name field.
- **Owners:** [_Bookmark.cshtml](../src/WikiWikiWorld.Web/Pages/Article/_Bookmark.cshtml), [_BookmarkToggle.cshtml](../src/WikiWikiWorld.Web/Pages/Article/_BookmarkToggle.cshtml), [_BookmarkLists.cshtml](../src/WikiWikiWorld.Web/Pages/Article/_BookmarkLists.cshtml), [_BookmarkNewList.cshtml](../src/WikiWikiWorld.Web/Pages/Article/_BookmarkNewList.cshtml), bookmark handlers in [View.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml.cs).
- **State storage:** Datastar `_bookmarkPending`/`_bookmarkError`, server membership results, native details state. Retry suppression is intentional. The reload instruction after an ambiguous failure is a message, not a separate persistent “retry forbidden” state.

### U02. Bookmark-list navigation and browser history

- **State:** current committed DOM/URL; request pending; superseded request; matching patch committed; failed ordinary navigation; failed history navigation.
- **Transitions:** link or Back/Forward aborts the previous request; completion accepts only the current request and matching `navigationId`; success commits canonical history, focus, and scroll. Ordinary failure keeps the existing view; history failure reloads the browser's destination URL.
- **Owner:** [bookmarks.js](../src/WikiWikiWorld.Web/wwwroot/js/bookmarks.js), with the Datastar adapter in [Bookmarks.cshtml](../src/WikiWikiWorld.Web/Pages/Profile/Bookmarks.cshtml).

### U03. Header search combobox

- **State:** short/eligible query, debounce/fetch, panel hidden/visible/dismissed, input focused/unfocused, no highlight/active option.
- **Transitions:** input clears the active index and controls dismissal; Datastar replaces results; arrows move/wrap selection or reopen; Enter navigates; Escape dismisses; focus and patches synchronize ARIA.
- **Owners:** [search-dropdown.js](../src/WikiWikiWorld.Web/wwwroot/js/search-dropdown.js), [_Layout.cshtml](../src/WikiWikiWorld.Web/Pages/Shared/_Layout.cshtml), [_SearchSuggestions.cshtml](../src/WikiWikiWorld.Web/Pages/Shared/_SearchSuggestions.cshtml).
- **State storage:** `ActiveIndex`, `data-dismissed`, `hidden`, focus, and Datastar request cancellation. There is no application-owned query-generation token in this combobox.

### U04. Message recipient autocomplete

- **State:** empty/typed query; debounce queued; fetch pending; suggestions hidden/visible; `currentFocus`; chosen recipient.
- **Transitions:** input closes the list and schedules search; responses populate it; arrows change the active item; Enter/click selects; outside click closes.
- **Owner:** inline script in [NewMessage.cshtml](../src/WikiWikiWorld.Web/Pages/Profile/NewMessage.cshtml), starting at line 333 in the reviewed snapshot.
- **Confirmed failures:** clearing input does not cancel the previous timer; a stale response can reopen results. Arrow navigation with no items dereferences an absent item. See F02/F03.

### U05. Admin redirect target picker and culture selector

**2026-11-06 update:** site/culture changes now invalidate dependent work and selection immediately. Culture and article requests each have a current owner; culture failures keep search disabled and expose retry. See resolved F04. The bullets below describe the reviewed snapshot.

- **State:** selected site/culture, culture request pending, empty/short/searchable query, debounce/search pending, results/empty results, target selected/cleared, spinner visible/hidden.
- **Transitions:** site changes fetch cultures and eventually clear selection; culture changes clear selection; search fetches candidates; choosing a candidate swaps search controls for selected-target display.
- **Owner:** inline script in [DomainRedirects.cshtml](../src/WikiWikiWorld.Web/Pages/Admin/DomainRedirects.cshtml), starting at line 173.
- **Static finding:** responses are not associated with the current site/culture/query generation. Clearing or changing context does not invalidate in-flight responses. See F04.

### U06. Firehose update polling

- **State:** live/paused, document visible/hidden, request pending/idle, never checked/last checked, no new changes/new changes, failed check awaiting another tick.
- **Transitions:** toggle, visibility event, or 30-second tick attempts a check; pending/hidden/paused guards suppress it; success updates the announcement; failure retains last-check context; finally releases pending.
- **Owner:** [firehose.js](../src/WikiWikiWorld.Web/wwwroot/js/firehose.js).
- **Boundary:** the feed does not live-patch rows; it announces a count and offers native navigation to latest changes.

### U07. Header-helper import/encode/cancel operation

- **State:** unsupported; empty/ready; importing URL; decoding; encoding; cancellation requested; succeeded/failed. Prior image and crop are retained across a failed import.
- **Transitions:** local/drop/paste/URL input starts import; URL input fetches before decode; explicit export creates a crop bitmap and runs encode; cancel aborts fetch or rejects/terminates worker work; finally restores controls.
- **Owners:** [header-helper.js](../src/WikiWikiWorld.Web/wwwroot/js/header-helper.js), [header-helper-worker.js](../src/WikiWikiWorld.Web/wwwroot/js/header-helper-worker.js).
- **State storage:** `Source`, `Busy`, `Cancelled`, `Worker`, `Abort`, `RejectOperation`. Worker internals additionally move through native decode/HEIF fallback or codec load/encode/transfer/error. Every operation gets its own worker.

### U08. Header-helper crop gesture and framing

- **State:** no source; idle; pointer captured for move/one of four resize corners; free/locked aspect; crop rectangle; zoom/fitted surface; preview frame queued/not queued.
- **Transitions:** pointer down snapshots crop, pointer ID, and scale; move derives a clamped rectangle; lost capture releases the gesture; keyboard and numeric input use the same crop rules; reset clears aspect and restores full image; preview redraws coalesce through `requestAnimationFrame`.
- **Owner:** [header-helper.js](../src/WikiWikiWorld.Web/wwwroot/js/header-helper.js), `SetCrop`, `Resize`, `Reset`, and pointer handlers.

### U09. Header-helper cached export and download readiness

- **State:** no encoded output/stale output, output being encoded, cached valid blob URL, valid but above upload limit, slug invalid/valid, markup available/unavailable.
- **Transitions:** crop or quality changes revoke and hide the cached download; explicit download encodes if missing; success exposes a fallback link and auto-clicks only within size limit; slug/focus changes update filename/markup without re-encoding.
- **Owner:** [header-helper.js](../src/WikiWikiWorld.Web/wwwroot/js/header-helper.js), `Invalidate`, `UpdateMarkup`, `DownloadHeader`, `Export`.
- **Boundary:** an invalid slug blocks copy/download, but it does not itself invalidate the encoded pixels. This is a separate state dimension from export freshness.

### U10. Native menus and disclosures

- **State:** native `details.open`; account/Edit/Bookmark menus share the `site-menu` name so the browser coordinates exclusivity. Other disclosures have independent state.
- **Transitions:** native summary activation opens/closes; Escape in [dropdown-menu.js](../src/WikiWikiWorld.Web/wwwroot/js/dropdown-menu.js) closes the open menu and restores trigger focus. CSS supplies outside-click coverage.
- **Instances:** shared account menu, article Edit and Bookmark menus, home category expansion, language-change forms, talk reply forms and thread collapse, profile locked badges, and generated IssueCredits groups.
- **Owners:** the corresponding Razor templates, [IssueCredits.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/IssueCredits.cs), [home.css](../src/WikiWikiWorld.Web/wwwroot/css/pages/home.css), and shared menu CSS. These already use browser-owned state; they do not need a parallel application enum.

### U11. Carousel position and active dot

- **State:** scroll position/currently intersecting slide and the dot carrying `.active`.
- **Transitions:** arrows and dots request smooth scroll; `IntersectionObserver` maps visible slides back to dot state.
- **Owner:** [carousel.js](../src/WikiWikiWorld.Web/wwwroot/js/carousel.js); carousel markup is generated by [CoverCarousel.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/CoverCarousel.cs).

### U12. YouTube activation

- **State:** ordinary external activation link → iframe inserted/loading → iframe loaded/focused. The replacement node is the state marker.
- **Transitions:** validated unmodified activation replaces the link with the embed; load transfers focus. Modified clicks retain ordinary link behavior.
- **Owner:** [article-view.js](../src/WikiWikiWorld.Web/wwwroot/js/article-view.js), with generated markup in [YouTube.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/YouTube.cs).

### U13. Profile sharing feedback

- **State:** unsupported/hidden; native-share capability or copy-only capability; pending browser operation; copied/failed feedback; reset timer queued.
- **Transitions:** share invokes the browser sheet or writes the clipboard; copy success/failure updates label and live region; a two-second timer restores the default. Repeated feedback resets the timer.
- **Owner:** [profile.js](../src/WikiWikiWorld.Web/wwwroot/js/profile.js).

### U14. Header paper-texture enhancement

- **State:** disabled/not applicable; original image visible; image/noise decoding and shader module loading; mounted texture; failed/context lost/disposed fallback.
- **Transitions:** configuration/query choice starts enhancement; successful resource loads mount the shader; failure or WebGL context loss disposes/removes it, exposing the original image.
- **Owner:** [header-paper-texture.js](../src/WikiWikiWorld.Web/wwwroot/js/header-paper-texture.js).

### U15. Search-glass enhancement, ink hysteresis, and lens generation

- **State:** disabled/not applicable/unreadable hero; sampled hero available; light/dark/unselected ink; focused suspension; refresh queued; current/pending/superseded lens generation; installed/no lens.
- **Transitions:** decode enables sampling; scroll/resize/blur coalesce refreshes; ink flips only outside the hysteresis band; size change builds a lens; generation mismatch discards a late result; small surfaces remove the current lens; hero replacement refreshes samples.
- **Owner:** [search-glass.js](../src/WikiWikiWorld.Web/wwwroot/js/search-glass.js).
- **Not just a pure calculation:** `ChooseInk` explicitly depends on its previous output, while `LensGeneration` controls which asynchronous result may be installed.

## Infrastructure, maintenance, and parser workflows

### T01. Deferred site/culture resolution and query mode

- **State:** unresolved; request resolved to content site/culture; resolved culture-selector root; no HTTP context for maintenance. The context captures the selected filtering mode at construction.
- **Transitions:** first access resolves and caches request context; subsequent accesses reuse it. Maintenance/root mode permits cross-site/culture queries; content mode supplies the named filters.
- **Owners:** [SiteContextService.cs](../src/WikiWikiWorld.Web/Services/SiteContextService.cs), [SiteResolverService.cs](../src/WikiWikiWorld.Web/Services/SiteResolverService.cs), [WikiWikiWorldDbContext.cs](../src/WikiWikiWorld.Data/WikiWikiWorldDbContext.cs).

### T02. Application caches and lazy initialization

- **Instances:** redirect-map cache absent/loading/cached/expired/invalidated; sitemap cache absent/generating/cached/expired, with a root-only fallback; full/talk Markdown pipelines uninitialized/initializing/ready/faulted through `Lazy<T>`; timeline's description pipeline has its own lazy instance.
- **Transitions:** cache misses build values, TTL expires them, and redirect create/delete explicitly invalidates its map. First pipeline use builds and retains it. Redirect records separately move active → soft-deleted.
- **Owners:** [ArticleDomainRedirectService.cs](../src/WikiWikiWorld.Web/Services/ArticleDomainRedirectService.cs), [SitemapService.cs](../src/WikiWikiWorld.Web/Services/SitemapService.cs), [MarkdownPipelineFactory.cs](../src/WikiWikiWorld.Web/Services/MarkdownPipelineFactory.cs), [Timeline.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/Timeline.cs).
- **Boundary:** stylesheet selection is a startup snapshot, not a hot-reload lifecycle. Cache/lazy mechanics are largely framework-owned.

### T03. Ambient write durability and connection transaction phase

- **State:** ambient High/Normal, previous nested scope value, disposed/not disposed, connection closed/open, transaction absent/starting/active.
- **Transitions:** entering a scope changes the async-local value; disposing restores the previous value once; interceptors apply the setting on connection open, before transaction start, or before a save on an already-open nontransactional connection.
- **Owners:** [WriteDurabilityScope.cs](../src/WikiWikiWorld.Data/WriteDurabilityScope.cs), [SqliteDurabilityInterceptor.cs](../src/WikiWikiWorld.Data/SqliteDurabilityInterceptor.cs), [write-durability.md](write-durability.md).
- **Invariant:** scope changes do not alter an already-running transaction's durability. Ordering, not just the enum, determines the effective database setting.

### T04. Retrying transaction attempts and tracked-entity recovery

- **State:** attempt running → success/commit; retryable SQLite BUSY/LOCKED failure → delay → new attempt; nonretryable/exhausted/cancelled terminal failure; tracker may still contain entities from an aborted attempt.
- **Owners:** [SqliteRetryExecutionStrategy.cs](../src/WikiWikiWorld.Data/SqliteRetryExecutionStrategy.cs), transaction closures across article/file/message writes, [ArticleLanguageService.cs](../src/WikiWikiWorld.Web/Services/ArticleLanguageService.cs), [BookmarkService.Management.cs](../src/WikiWikiWorld.Web/Services/BookmarkService.Management.cs).
- **Review point:** languages clear the tracker before attempts and bookmark transactions detach their entities on failure. Several article/file/message closures have no equivalent explicit reset. Whether each failure point can safely replay needs targeted fault injection; no uniform guarantee was established in this audit.

### T05. Category membership reconciliation at save

- **State:** added/content-changed/reactivated/unchanged/deactivated revision and its tracked existing/added/removed memberships.
- **Transitions:** save interception selects relevant revisions, loads memberships for existing rows, parses category identities, removes obsolete memberships, retains matching ones, adds missing ones, then lets the enclosing save persist both.
- **Owner:** [CategoryMembershipSaveInterceptor.cs](../src/WikiWikiWorld.Web/Services/CategoryMembershipSaveInterceptor.cs).
- **Invariant:** deactivation/deletion retains historical memberships; reactivation reconciles them. Membership set and EF entity state jointly encode progress, including repeated save attempts.

### T06. Batched category backfill

- **State:** dry-run/apply, fixed initial upper ID, last completed ID, batch read/reconciled/committed/cleared, cancelled/completed.
- **Transitions:** capture upper bound → read up to 100 revisions → reconcile → optionally save → commit → clear tracker → advance cursor. Cancellation leaves already-committed batches intact.
- **Owners:** [CategoryMembershipBackfill.cs](../src/WikiWikiWorld.Web/Services/CategoryMembershipBackfill.cs), [CategoryMembershipBackfillCommand.cs](../src/WikiWikiWorld.Web/Services/CategoryMembershipBackfillCommand.cs).

### T07. HTTP response capture and restoration

- **State:** Datastar bypass; original response stream → capture stream → HTML formatted or non-HTML copied → original stream restored; exception also restores the original stream.
- **Owner:** [HtmlPrettifyMiddleware.cs](../src/WikiWikiWorld.Web/HtmlPrettifyMiddleware.cs), `Invoke`.
- **Invariant:** captured response-body ownership must end in `finally`; fragment requests must never enter document formatting. This is a resource lifecycle, not persisted domain state.

### T08. Directive lexical scanner and nesting

- **State:** ordinary text, escape skipping, matched/unmatched backtick run, nested directive depth, top-level property boundary, matching close/not found.
- **Transitions:** escapes consume the next character; matching backticks skip code spans; `{{`/`}}` change depth; `|#|` splits only at depth zero.
- **Owner:** [DirectiveSyntax.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/DirectiveSyntax.cs), used by infobox, footnote, and citation parsing.
- **Classification:** depth makes this stack-like parsing state, rather than a strictly finite-state machine. It is still an important implicit stateful parser.

### T09. Multiline directive collection

- **Instances:** PublicationIssueInfobox, CoverCarousel, CoverGrid, Timeline, IssueCredits, and IssueContents.
- **State:** outside a block → opened/accumulating → terminated → parsed/renderable, or incomplete/malformed. Raw builders retain accumulated lines. The infobox tracks a matching outer closing position; credits/contents keep `IsClosed`; carousel/grid/timeline rely on Markdig block state and accumulated content.
- **Owners:** the six same-named files in [MarkDigExtensions](../src/WikiWikiWorld.Web/MarkDigExtensions/).
- **Boundary:** Markdig exposes `BlockState`, but each directive's payload lifecycle and terminal validation remain application code. The implementations do not all have the same nesting, closing, or malformed-source behavior.

### T10. Credits and contents group grammar

- **State:** no group/current group; group awaiting entries/nonempty group; valid/invalid property set; contents additionally permits an implicit initial ungrouped list.
- **Transitions:** a Group item starts a new group; subsequent entries attach to the latest group; wrong keys, empty values, missing required values, or empty groups invalidate the whole directive. Credits entries require a prior group; contents can create its initial group automatically.
- **Owners:** `ParseGroups` in [IssueCredits.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/IssueCredits.cs) and [IssueContents.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/IssueContents.cs).

### T11. Footnote and citation numbering/aggregation

- **State:** unnumbered parsed references → numbered references → optional collected footnote/citation block. Citations also track first occurrence/repeated reference and per-occurrence back-reference IDs.
- **Transitions:** footnote reprocessing numbers references and attaches parsed inner blocks; citation reprocessing builds or reuses a citation-map entry and appends its back-reference. Ordering matters: article rendering reprocesses footnotes before collecting citations.
- **Owners:** [Footnotes.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/Footnotes.cs), [Citations.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/Citations.cs), [ArticleRenderer.cs](../src/WikiWikiWorld.Web/Services/ArticleRenderer.cs).
- **Boundary:** AST enrichment for images, headers, covers, downloads, and metadata is another ordered parsed → enriched → rendered lifecycle, coordinated by `ArticleRenderer`; it is not an independent user workflow per directive.

### T12. YouTube timestamp parser

- **State:** accumulating digits, expecting/consuming a unit, previous unit rank, accumulated seconds, accepted/rejected.
- **Transitions:** digits extend the current number; `h`, `m`, `s` consume it in strictly descending unit order; invalid order, missing digits, trailing unitless digits, or overflow reject. Plain integer seconds use a separate acceptance path.
- **Owner:** `TryParseTimeToSeconds` in [YouTube.cs](../src/WikiWikiWorld.Web/MarkDigExtensions/YouTube.cs).

### T13. Image-header scanning

- **State:** format selected from signature; for JPEG, seeking marker → consuming marker/padding → standalone marker or length-bearing segment → SOF dimensions found, truncated/invalid, or end reached.
- **Transitions:** stream position, current marker, and segment length drive parsing; unsupported or failed reads return unavailable dimensions through the helper's error boundary. Other formats use format-specific header/chunk/XML reads.
- **Owner:** [ImageDimensionReader.cs](../src/WikiWikiWorld.Web/Helpers/ImageDimensionReader.cs), especially `ReadJpegDimensions`.

### T14. Legacy-file migration

- **State:** legacy bytes discovered; database match absent/present; target already exists; ready to move; moved; failed. Scan/apply mode controls whether a move occurs.
- **Transitions:** scan resolves a target revision; apply attempts the move; a later run derives state again from physical paths. Lost-history counts are reported separately.
- **Owners:** [FileMigrationService.cs](../src/WikiWikiWorld.Web/Services/FileMigrationService.cs), [MigrateFiles.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Admin/MigrateFiles.cshtml.cs).
- **Explicit portion:** `FileMigrationStatus` already names dispositions. Progress and rerun behavior remain inferred from filesystem and database state, rather than a persisted migration record.

### T15. Nested HTML formatting mode

- **State:** a stack of Block/InlineContainer/InlineElement modes, plus ancestor-derived preformatted context. The private enum names modes, but stack depth and traversal encode the active formatting state.
- **Transitions:** opening tags classify and push a mode; closing tags pop it; text consults the top mode and its preformatted ancestors to choose plain versus pretty output. Empty-stack fallback uses Block mode.
- **Owner:** [InlineAwarePrettyFormatter.cs](../src/WikiWikiWorld.Web/InlineAwarePrettyFormatter.cs), `OpenTag`, `CloseTag`, and `Text`.
- **Invariant:** opening/closing traversal must keep modes balanced, preserving inline spacing and authored preformatted content. Like directive nesting, this is a stack-based stateful processor rather than a strictly finite-state machine.

## Findings and verification limits

### F01. Upload recovery lacks a revision-specific staging claim

**Resolved in source on 2026-11-06; production schema applied on 2026-11-06 at 19:32 UTC and application deployed at 20:02 UTC with runtime verification passed.** Upload writers now commit an exact staging identifier, and recovery includes historical revisions, preserves conflicts/legacy staging, and checks committed claims before cleanup. [Implementation and rollout](upload-recovery.md); regression coverage: [FileUploadRecoveryTests.cs](../tests/WikiWikiWorld.Web.Tests/FileUploadRecoveryTests.cs) and [FileUploadSchemaTests.cs](../tests/WikiWikiWorld.Web.Tests/FileUploadSchemaTests.cs). Both original failure scenarios were reproduced before the fix. The following records the original finding.

Static evidence: `StageUploadedFileAsync` names staging with canonical ID, extension, and a random suffix; `FinalizeCommittedUploadsAsync` queries only current revisions; `FindNewestStagedFile` picks the newest match; `IsTemporaryFileClaimedAsync` likewise only checks current revisions.

If revision A commits but finalization fails, then revision B supersedes it before startup recovery, A is no longer eligible for recovery. Its remaining staged bytes may later be deleted as unclaimed. Multiple staged attempts with the same canonical ID and extension can also make the newest candidate differ from the bytes belonging to the committed revision. These are consequences of the matching rules, not a reproduced filesystem failure in this audit.

Source: [FileUploadStorageService.cs](../src/WikiWikiWorld.Web/Services/FileUploadStorageService.cs), lines 25, 128, 178, 220, and 249 in the reviewed snapshot. The normal post-commit finalize path does use the exact revision ID; the gap is in reconstruction after failure.

### F02. Clearing recipient input leaves an earlier debounce active

**Resolved in source on 2026-11-06.** The message page now invalidates each search on input/clear/dismissal, aborts superseded work, and checks request identity after reading the response body. [Regression tests](../tests/browser/recipient-autocomplete.test.cjs) and [verification scope](../tests/browser/README.md). The following records the original finding.

Confirmed using an isolated Node harness executing the actual inline script. Typing a query, clearing the input before its timer fires, and then resolving the queued search produced an empty input with a visible stale suggestion. The empty-input return occurs before `clearTimeout`, and response handling does not check current query or request identity.

Source: [NewMessage.cshtml](../src/WikiWikiWorld.Web/Pages/Profile/NewMessage.cshtml), lines 344–361 in the reviewed snapshot.

### F03. Recipient arrow navigation can enter a nonexistent selection

**Resolved in source on 2026-11-06.** Arrow and Enter handling now checks the rendered option set, clears obsolete selection on input/dismissal, and preserves normal form handling when nothing is selected. [Regression tests](../tests/browser/recipient-autocomplete.test.cjs) include empty results, wrapping, selection, and result replacement. The following records the original finding.

Confirmed with the same harness: Arrow Down when no suggestion items exist throws `Cannot read properties of undefined (reading 'classList')`. The code checks that the collection exists, but not that it is nonempty, before accessing `x[currentFocus]`.

Source: [NewMessage.cshtml](../src/WikiWikiWorld.Web/Pages/Profile/NewMessage.cshtml), lines 386–410.

### F04. Admin target searches and culture fetches can apply stale responses

**Resolved in source on 2026-11-06.** Both original stale-culture and cleared-query failures reproduced before the fix. Request identity plus site/culture/query checks now guard rendering, errors, and spinners; context changes clear selection immediately. [Thirteen regression cases](../tests/browser/admin-article-picker.test.cjs), [Chromium verification scope](../tests/browser/README.md), and [SQLite server-validation cases](../tests/WikiWikiWorld.Web.Tests/ArticleDomainRedirectTests.cs). The following records the original finding.

Static evidence: both fetch continuations update the current controls without an abort controller or request-generation/current-context check. Changing site/culture or clearing a short query does not invalidate an already-running search. An older culture response can overwrite the selector after a later site change; an older search response can refill results after context changes. Server-side target validation still checks that the selected article matches the submitted site/culture, so this is not evidence of an unchecked cross-site write.

Source: [DomainRedirects.cshtml](../src/WikiWikiWorld.Web/Pages/Admin/DomainRedirects.cshtml), lines 190–277, and validation in [ArticleDomainRedirectService.cs](../src/WikiWikiWorld.Web/Services/ArticleDomainRedirectService.cs).

### F05. Missing-user-page editor fallback is unreachable

**Resolved in source on 2026-11-06.** The missing-owner 404 and creation-mode validation failure reproduced before the fix. GET now offers owner-only creation when that user article is absent; `OriginalUrlSlug` controls rendered and submitted mode. [Eleven SQLite-backed handler cases](../tests/WikiWikiWorld.Web.Tests/UserArticleEditorTests.cs) include forcing viewer provisioning to fail, following its redirect, and successfully creating through the editor. This verifies the handler chain, without an authenticated full-app browser session. The following records the original finding.

Follow-up source trace on 2026-11-06: `CreateEditModel.IsEditMode` is true whenever `UrlSlug` is nonempty. GET therefore attempts to load a current article and returns 404 if it is missing. The later user-page creation branch requires both a nonempty slug and `!IsEditMode`, so it cannot execute. This also breaks the owner fallback from the article viewer when best-effort user-page provisioning returns no article: its redirect supplies a slug to this editor, which returns 404 again. At the time of the original trace, this path had not yet been reproduced end to end.

Source: [CreateEdit.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/CreateEdit.cshtml.cs), lines 96, 136–145, and 166; fallback redirect in [View.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml.cs).

The proposed fixes and verification for F01–F05 are in [the bug-fix plan](state-machine-bug-fix-plan.md).

### How to use this inventory

Start with D03 (upload recovery), D01/D02 (revision invariants), D07 (language groups), D08/D09 (bookmarks), U07–U09 (header helper), and U04/U05 (autocomplete request ownership) when deciding where explicit transition methods or transition-focused tests would pay off. Native details/dialog state and small framework-owned caches do not by themselves justify introducing a state-machine library.

This was a source audit with two isolated JavaScript reproductions. No runtime code was changed. No application server, production database, migration, or deployment was run; browser rendering, codec execution, filesystem fault recovery, and transaction fault injection were not exercised. The state names and graphs are a code-derived model, not a proof of all runtime paths or externally managed ingestion transitions.
