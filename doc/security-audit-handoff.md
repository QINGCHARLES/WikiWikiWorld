# Security audit handoff

Prepared 2026-11-07 for continuing the outstanding findings with Claude or another engineer. Start with item 4. This is a focused handoff, not a completed security assessment.

## Repository and completed work

- Checkout: `E:/source/WikiWikiWorld`, branch `main`. Latest observed commit: `39484d33c4263904a81db752ff61bf961f6e8e5a`. The tracked working tree was clean before this document was added. Other work advanced the branch during this investigation; read current source and do not reset to an earlier audit commit.
- Read `AGENTS.md` and `PROJECT.md` before editing. Stack: .NET 10 / C# 14, ASP.NET Core Razor Pages, EF Core 10 / SQLite, manually managed schema, SDK-published chiseled Linux containers.
- Item 1, private-message exposure, was addressed with separate private-conversation, participant and message tables. Public discussion remains public. See `doc/private-messaging.md`.
- Item 2, citation HTML injection, and item 3, native Markdown event attributes / executable links, were fixed, tested, committed and deployed. See `doc/markdown-rendering-security.md`. The shared short-content methods now use **user-content** names rather than **talk** names.
- The latest documented production release is `release-20261107T181946Z-892b002c3811`, image `ghcr.io/qingcharles/wikiwikiworld@sha256:c641d9d47797ef78acd85e2dcfda42c81468f007104eacaeb9722618c189452c`. This comes from the current runbook, not a fresh production inspection in this investigation.
- The preceding release verification recorded 373 .NET tests, 27 JavaScript tests and 14 Python maintenance tests passing. These are previous results; rerun relevant checks after new changes.

## 4. SVG uploads retain active content — current investigation

### What has actually been established

1. `Helpers/ImageValidationHelper.cs:36` accepts `.svg` and `image/svg+xml`. Its signature helper explicitly trusts SVG extension/MIME metadata around line 132; it does not parse or sanitize SVG content.
2. `Services/FileUploadStorageService.cs:20` stages uploaded bytes; line 34 copies the original stream. Dimension extraction in `Helpers/ImageDimensionReader.cs` reads the root element's dimensions, with DTD resolution disabled. That protects this XML read from external resolution; it does not remove browser-executable SVG content.
3. `Program.cs:474` serves the upload root through `/sitefiles` using static-file middleware and a 30-day public cache. The application CSP around line 448 addresses framing, with no script restriction. Verify the actual response headers during the pending browser test.
4. `Pages/Article/View.cshtml` wraps the file image in an anchor to the original file URL. A reader can therefore go from an image preview to a directly opened SVG document.
5. A local harness called the **real** validation and dimension helpers. Both a `<script>` SVG and an `onload` SVG were accepted with no validation error and dimensions 300 × 90. A safe control was also accepted. Results are in `.local/svg-upload-repro/validation-results.json`.

**Reproduced end to end on 2026-11-07** against the disposable fixture, built from `39484d3`. Results are in `.local/svg-upload-repro/upload-results.json`.

- `POST /api/article/{slug}` as `SvgReproUploader` accepted all three SVGs (HTTP 200). Slugs: `svg-repro-{script,onload,safe-control}-190700`.
- `/sitefiles/1/images/<guid>/<n>.svg` served each file byte-for-byte unchanged, with `Content-Type: image/svg+xml`, no `Content-Disposition`, no `X-Content-Type-Options`, `Cache-Control: public, max-age=2592000`, and only the global CSP `frame-ancestors 'none'; frame-src https://www.youtube-nocookie.com`.
- In the browser, signed in as `SvgReproReader` through the real login form, the `<img>` preview on `/file:{slug}` did not execute. The image's link goes to the raw `.svg`. Opening that URL directly:
  - script probe: executed with origin `https://en.magazedia.com.localhost:7127`. Its same-origin request to `/message/new` returned the reader's signed-in form, including the antiforgery token (`data-svg-authenticated-read=true`).
  - onload probe: the handler executed.
  - safe control: stayed inert.
- **Impact:** any account that can upload a file can store a script. That script runs with the site's full authority in any signed-in reader who opens the image. It can read authenticated pages and their antiforgery tokens, and so can act as the reader. The probe deliberately did not send messages or exfiltrate data. Responses are publicly cached for 30 days. The production configuration matches by source, but production itself was not tested.
- Browser note: the fixture was restarted with the machine's already-trusted `CN=en.magazedia.com.localhost` dev certificate, read-only from the CurrentUser store, because the browser pane rejects the self-signed `fixture.pfx`. Cookies are not port-scoped, so the pane may still hold a fixture sign-in cookie for `en.magazedia.com.localhost`. The 7126 dev app has different data-protection keys and will reject it.

### Fix (implemented 2026-11-07, uncommitted, not deployed)

The owner chose the sandbox-header approach.

- `Infrastructure/UploadedFileResponseHeaders.cs` is now the `/sitefiles` `OnPrepareResponse` handler. Every uploaded file gets `X-Content-Type-Options: nosniff` and the existing 30-day cache. Any SVG, matched by `image/svg+xml` content type or a `.svg`/`.svgz` name, also gets `Content-Security-Policy: sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'; frame-ancestors 'none'`.
- The global `OnStarting` header writer in `Program.cs` now sets its CSP only when none is present, so the upload policy is not overwritten. Ordinary pages still receive the unchanged global CSP; no other component sets a CSP.
- Stored files and the database are unchanged, and existing uploads are covered because the headers are applied when the file is served.

Verification on the rebuilt fixture:

- 373 .NET and 27 JS tests pass. The build used a separate artifacts path because the running development app locks `bin/Release`.
- New uploads: the served headers match the policy above and the bytes are unchanged (`.local/svg-upload-repro/upload-results-fixed.json`).
- As the signed-in reader, directly opening the script and onload probes produced Chromium's console error "Blocked script execution … the document's frame is sandboxed". No probe marker was set.
- SVG `<img>` previews, including the site logo `/sitefiles/1/logo.svg`, still render.

**Remaining exposure: browser caches.** The pre-fix `1.svg`, opened again after the fix, was served from the browser cache (`transferSize` 0) with its old headers, and the script ran again (`data-svg-authenticated-read=true`). A server change cannot reach responses already in browsers. Before this, that browser had opened the URL directly; the cache entry was observed only after that direct navigation. Reuse of an entry cached by an `<img>` load for a later navigation is **inferred** from Chromium keying its cache by URL and top-level site; it was not observed. If that inference holds, a reader who viewed an SVG file page in the 30 days before deployment could still execute that SVG until the entry expires.

Other checks: no controller, page or helper streams upload bytes outside `/sitefiles` (no `PhysicalFile`/`File(...)`/`SendFileAsync` results). The development `data/sitefiles` holds only `.jpg`, `.webp`, `.avif`, `.png`, `.jpeg` and 8 `.svg` files, so scoping the policy to SVG covers the active types present. The development app on 7126 still runs pre-fix code until it is rebuilt and restarted. The owner confirmed on 2026-11-07 that no malicious files were uploaded before that date, so no cache or legacy-file remediation is needed. No regression test was added, because the owner's standing rule requires explicit instruction for new unit/integration tests.

### Local reproduction material and state

The following are Git-ignored local files; send them separately if Claude cannot access this checkout:

- `.local/svg-upload-repro/Program.cs` and `SvgUploadRepro.csproj`: prepare a fresh database with `EnsureCreated`, two synthetic accounts (`SvgReproUploader` and `SvgReproReader`), three SVG files and helper-validation results. The throwaway password is in this local program. Do not use production credentials.
- `script.svg`: sets `data-svg-probe=script` and records the current origin. It also makes a read-only same-origin request to `/message/new` and records a boolean indicating whether the synthetic reader's signed-in form can be read. It sends no messages and exports no data.
- `onload.svg`: sets a harmless DOM marker from an SVG load handler.
- `safe-control.svg`: ordinary image control with no active content.
- `data/app.db`: fresh fixture database, not a copy of development or production data. Synthetic fixture material only.
- `build.log`, `restore.log`, `server.log`: local diagnostic logs. Built outputs are under `.local/svg-validation`.

The disposable application process (PID 59316) was verified and **stopped** for handoff. It used HTTPS 7127 / HTTP 5098 and the fresh fixture database. Its self-signed certificate remains at `.local/svg-upload-repro/fixture.pfx`; do not install or trust it globally. Do not confuse this fixture with the ordinary development app on HTTPS 7126 / HTTP 5097.

The setup program deliberately refuses to overwrite an existing fixture database. Reuse this fixture or choose a new scratch directory; do not rerun it expecting a reset. Build only from local dependencies where possible:

```powershell
dotnet restore .local/svg-upload-repro/SvgUploadRepro.csproj --artifacts-path .local/svg-validation --source C:/Users/Qing/.nuget/packages -p:NuGetAudit=false
dotnet build .local/svg-upload-repro/SvgUploadRepro.csproj -c Release --artifacts-path .local/svg-validation --no-restore
```

To restart the existing fixture, run the built web DLL with content root `src/WikiWikiWorld.Web`, environment `Development`, `ApplicationStartup:DataPath` and `ConnectionStrings:DefaultConnection` both pointing to the fixture, and port overrides 7127 / 5098. Set `Kestrel__Certificates__Default__Path` to the fixture PFX and use a deliberately nonexistent `DevelopmentHttpsCertificateSubjectName` so the configured default certificate is used. Limit browser certificate-error bypass to this fixture.

### Why the test did not finish

The audit skill requires a fully isolated execution sandbox. Available Linux isolation lacked the Windows browser used here, so the user expressly approved the narrower disposable local test: **“Use the disposable local instance (Recommended).”** That exception is limited to harmless local probes, fresh synthetic data and no production access.

Subsequent failures were harness errors, not recorded cyber-safety denials:

- API JSON preserves PascalCase (`Program.cs` sets `PropertyNamingPolicy = null`). The first client read `.token` instead of `.Token`, submitted `Bearer undefined`, and received an invalid-token response. Read `.Token`, `.Url`, `.MimeType`, etc.
- A retry assumed that a previous Playwright browser context was still present. It was absent, so accessing its pages failed. Create a fresh context after reconnecting; do not assume custom context properties survive tool/session boundaries.

### Complete this reproduction next

1. Start the disposable fixture. Create a fresh browser context, bypass its self-signed certificate only there, and restrict browser requests to `en.magazedia.com.localhost:7127`.
2. Obtain a token as the synthetic uploader with `POST /api/auth/token`; read `Token` without logging it.
3. Upload each SVG through `POST /api/article/{slug}` using multipart fields `Title`, `Type=File`, `Text`, `RevisionReason`, and `File` with MIME `image/svg+xml`. Use new synthetic slugs. An authenticated file replacement through `POST /api/file/{slug}` is another relevant entry point.
4. Read `GET /api/file/{slug}`, then fetch its `Url`. Record status, content type, disposition, CSP and exact byte preservation. Do not print tokens or cookies.
5. In a separate fresh context, log in as the synthetic reader through the actual login form. View `/file:{slug}` and confirm the `<img>` preview does not execute the marker. Then follow its image link or navigate directly to the stored URL. Check both SVG probes for their markers and origin. For the script probe, check the read-only signed-in-form boolean. The safe control must remain inert.
6. Stop after that minimum result. No real accounts, production uploads, private-message reads or destructive actions are needed. Stop the fixture and close its browser contexts afterward.

### Fix planning after reproduction

Agree the invariant: uploaded images must not become executable documents with the application's authority. Evaluate a response policy specifically for untrusted SVGs that blocks scripts and isolates document origin while preserving legitimate image rendering; test direct navigation, events, SVG links, embedded HTML and safe graphics. The existing global `OnStarting` CSP writer must not overwrite a more restrictive upload policy. Account for existing files and cached responses, not just future uploads.

Other options are converting uploads to an inert raster format, rejecting SVG uploads, or serving them from a separate origin without application credentials. Those change behavior or architecture and require the owner's choice. Do not introduce a hand-written blacklist and claim all SVG active content is covered. Add targeted regression tests for the chosen boundary, then obtain current authorization before a new production release.

## Remaining audit queue

These are preliminary leads until independently reproduced against current code:

| Number | Lead | Next investigation |
| --- | --- | --- |
| 5 | Mutations can omit another user's home-page ownership check | **Reproduced and fixed 2026-11-07 (uncommitted, not deployed)**. See the section below. |
| 6 | Redirect administration requires login but no operator permission | `Pages/Admin/DomainRedirects.cshtml.cs:18` currently has `[Authorize]`. Test a normal synthetic account's GET, create, delete and helper handlers. Establish the intended operator policy, enforce it server-side, and cover all handlers. |
| 7 | Logout link has no matching handler | `_Layout.cshtml` links `/Account/Logout`; no corresponding page handler was found. Verify routing and actual cookie termination. If confirmed, implement the established Identity sign-out flow with an antiforgery-protected POST and test that the session ends. |
| 8 | Potentially expensive directive scanning | Revisit `MarkDigExtensions/DirectiveSyntax.cs` and callers. Use bounded offline inputs and Release measurements to determine whether malformed/repeated delimiters cause disproportionate work. No availability tests against shared development or production services. |

## 5. User-page ownership — reproduced and fixed

**Reproduction** (disposable fixture, synthetic accounts). With an API token, `SvgReproUploader` rewrote and then deleted `@SvgReproReader`'s user page: `PUT /api/article/@SvgReproReader` returned 200 and changed the text, `DELETE` returned 200, and a later GET returned 404.

The source showed the same gap in every entry point:

- Razor edit checked only the *submitted new* slug. Posting `OriginalUrlSlug=@victim` with an ordinary new slug therefore renamed or rewrote the page.
- The file-attach edit branch converted the page into a File article without passing through `UpdateArticleAsync`.
- Razor revert, the Razor Delete page, API PUT and API DELETE had no ownership check.
- API POST create would let anyone claim an unprovisioned `@name`.

**Rule now enforced.** An `@UserName` page belongs to the account it names.

- Only that account may create, edit, revert or delete it.
- It cannot move to another address and must keep the User type.
- No other page may move onto an `@` address or take the User type.
- All other pages remain collaboratively editable.
- There is no operator override; moderation of user pages would need a separate decision.

The development database already satisfies the invariant: every current `@` page has type User, and no other page does.

**Implementation.**

- `ArticleRevisionService` gained:
  - `IsUserPageSlug`, an owner check that compares the acting account's username, case-insensitively, with the slug after `@`;
  - `EnsureMayCreateAsync`, for the two creation paths that write their own revisions;
  - checks inside `UpdateArticleAsync`, `RevertToPreviousRevisionAsync` and `DeleteArticleAsync`. The last two now take the acting user ID; revert checks both the current and the restored slug.
- New failure kinds: `Forbidden` (API 403, Razor `Forbid()`) and `Invalid` (API 400, Razor form error).
- The Razor file-attach edit branch refuses any user page.
- Two existing test call sites were updated for the new signatures; no tests were added.

**Verification.** 373 .NET tests pass. On the rebuilt fixture:

- **API:**
  - The other account's PUT, DELETE and `@SvgReproNobody` create returned 403.
  - User-type create or edit of an ordinary page returned 400.
  - Collaborative create and edit of an ordinary page by different accounts, and the owner's edit of their own page, returned 200.
- **Razor, signed in through the login form as the other account, sending forged posts with a valid antiforgery token:**
  - Rename-away, same-slug edit, revert and delete were all refused (`Forbid()`).
  - File conversion was refused with the form error.
  - An ordinary edit still redirected to the article.
- **Afterwards:** the victim page was unchanged (`@SvgReproReader`, type User, the owner's text), and neither hijack slug existed.

**Known limitation (pre-existing).** Cookie `Forbid()` redirects to `/Account/AccessDenied`, which has no page and returns 404. Not changed here.

Do not silently renumber the queue or re-report items 1–3 as unfixed. The two previously noted missing cover images were explicitly deprioritized by the owner and are outside this work.

## Completion expectations

For each issue: reproduce safely, explain observed impact and limits, propose the smallest complete fix, implement when authorized, run meaningful regression checks, and document the result. Keep database changes explicit and preserve data. Existing ImageSharp advisory warnings are a separate known issue; do not silently upgrade packages as part of SVG investigation. Do not commit local fixture databases, certificates, credentials, build outputs or tokens. A deployment instruction for an earlier fix is not blanket approval to deploy every remaining audit item.
