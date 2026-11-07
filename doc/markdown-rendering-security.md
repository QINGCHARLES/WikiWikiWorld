# Markdown rendering security

## Rendering contract

Citation metadata is literal text, encoded with Markdig's HTML writer. Citation links accept absolute HTTP/HTTPS URLs only; unsupported destinations remain readable text. DOI path segments are URL-encoded independently. Existing bibliography typography, numbering, backlink and approved-domain behavior remain intact.

The shared `MarkdownSafetyExtension` filters authored generic attributes and ordinary Markdown destinations before trusted link attributes are added. It runs for article and talk documents, including nested footnote parse passes and infobox facts. The exact allowed attributes and schemes are documented in [MARKDIG_EXTENSIONS.md](../MARKDIG_EXTENSIONS.md). IDs, CSS classes, accessibility metadata and supported links remain usable. Unsafe destinations become empty `href`/`src` values; text and image alt text remain visible. Raw HTML stays disabled and the existing Datastar ignore boundaries remain required.

This release addresses citation HTML injection and native Markdown event attributes, including related executable link schemes. It changes rendering only: no schema migration, content rewrite or message deletion is required. It does not address active uploaded SVG content, which remains a separate audit item.

## Verification

Run `dotnet test tests/WikiWikiWorld.Web.Tests -c Release`. `CitationTests` and `MarkdownSafetyTests` exercise malicious text/attributes, encoded and mixed-case schemes, all citation fields, URL/DOI escaping, nested rendering, safe formatting and the real article renderer plus HTML prettifier. Existing issue-credit and infobox coverage remains part of the full suite.

On 2026-11-07, all 373 .NET tests and 27 existing JavaScript interaction tests passed. Chromium probes against the real rendered/prettified output passed all 20 Markdown cases and 14 citation cases. Native handlers were removed, script URL probes remained inert, and formatting controls retained IDs, classes and inert metadata. The pre-fix reproduction used isolated synthetic content; no probe was written into production content.

## Deployment and recovery

Publish the SDK Linux amd64 chiseled container, retain its source commit and immutable digest, and verify the actual image with an isolated consistent SQLite backup before replacing the production container. Use the existing host update script after successfully pulling the immutable image. Production data and certificates retain their existing mounts. Verify HTTPS, representative article pages and anonymous authentication gates after restart. Rollback uses the previous immutable image; these rendering changes require no database rollback.

## Production operation: 2026-11-07

- Source commit: `5262c63e96af` (citation and Markdown safety fixes, tests and contracts). All accumulated changes were committed and pushed to `main` before publishing.
- Release: `release-20261107T152847Z-5262c63e96af`, SDK-published Linux amd64 `10.0-noble-chiseled-extra` with ICU enabled. The transferred archive's SHA-256 was verified before loading on the host.
- Deployed immutable image: `ghcr.io/qingcharles/wikiwikiworld@sha256:d4cad61bf13c676193f0ce2efd7dc725058a0ebf84ad93bedfd4722026e14f8f`. Both the immutable release tag and `latest` were pushed successfully.
- Before replacement, the actual image ran as UID 1654 against a consistent, isolated SQLite backup of production. All 32 synthetic stored article cases and the stored talk-post case passed, safe formatting worked without JavaScript, and representative citation/talk/formatting cases passed again after restarting the fixture. No browser script errors occurred. Synthetic probes were never inserted into production; the isolated database was removed after verification.
- The immutable image was pulled successfully before the existing `/home/deploy/update.sh` replaced and restarted `wikiwikiworld`. Existing data/certificate mounts were retained. No schema changes or production content writes were performed.
- After restart, eight TLS-validated HTTPS pages returned 200: the English and selector Magazedia homepages, Japanese Magazedia, English Cinepedia, Fanfare, CRTR, the GQ Style USA Summer 2016 article and `/wiki:header-helper`. Anonymous `/message/new` returned 302 to login. Chromium verified the representative article, authentication gate and JavaScript-disabled homepage with no script errors. The new production container reported zero restarts.
- Rollback image retained locally on the host as `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261107T152847Z-5262c63e96af`; previous immutable image: `ghcr.io/qingcharles/wikiwikiworld@sha256:fe49857481e2df3b29c67ae3722737baf7946b99806910dd4187a9896c96771f`. To roll back, successfully pull that digest, then invoke `/home/deploy/update.sh` with it. No database restore is needed for this release.
- Build limitation: existing ImageSharp NU1902/NU1903 advisory warnings remain; package versions were not changed by this rendering fix.

## Shared user-content renderer release: 2026-11-07

- Source commit: `892b002c3811a3ef30bfac4c87871cc46cac1d46`. All pending source changes were committed and pushed to `main` before publishing.
- Release: `release-20261107T181946Z-892b002c3811`, SDK-published Linux amd64 `10.0-noble-chiseled-extra` with ICU enabled. The transferred archive SHA-256 was verified before loading: `eca483716a2601d75e1638532437818e0fa35d147f47a62fb9dfc40c5a02a715`.
- Deployed image: `ghcr.io/qingcharles/wikiwikiworld@sha256:c641d9d47797ef78acd85e2dcfda42c81468f007104eacaeb9722618c189452c`. Both the immutable release tag and `latest` were pushed to GHCR. The image was successfully pulled by digest before the host update script replaced the production container at 18:25:18 UTC.
- `RenderUserContentMarkdown`, `GetUserContentPipeline` and `BuildUserContentPipeline` replace the talk-specific names. Discussion/private-message callers, tests and references use the new names. Pipeline behavior is unchanged. Bookmark-note storage and editing are still planned.
- The release also commits the already-applied download-note foreign-key repair, its tests/runbook, and fresh creation-script corrections matching the existing non-STRICT tables, integer enum columns and additive-upgrade column order. The production repair was applied earlier at 18:03:52 UTC; no migration was rerun and no database schema/content change was required by this application deployment. See [the operation record and backups](download-url-notes.md#applied-operation-2026-11-07).
- Verification: all 373 .NET tests, 14 Python maintenance tests and 27 JavaScript tests passed. The actual image ran as UID 1654 against a consistent, isolated production backup; table strictness, columns, foreign keys and indexes matched fresh `Create.sql`. All 32 synthetic stored article cases, discussion URL/attribute restrictions, preserved article formatting and Japanese culture rendering passed, including after a container restart. The isolated login form generated persisted data-protection keys in the mounted data directory.
- After deployment, eight TLS-validated HTTPS pages returned 200: English/root/Japanese/Arabic Magazedia, English Cinepedia, Fanfare, CRTR and the header helper. Anonymous `/message/new` returned 302 to login. The production container reported zero restarts and retained its existing data/certificate mounts. The isolated container, database, keys and synthetic probes were removed; synthetic content was never inserted into production.
- Rollback image retained on the host as `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261107T181946Z-892b002c3811`; previous digest: `ghcr.io/qingcharles/wikiwikiworld@sha256:d4cad61bf13c676193f0ce2efd7dc725058a0ebf84ad93bedfd4722026e14f8f`. Pull that digest successfully, then pass it to `/home/deploy/update.sh`. No database restore is needed.
- Existing ImageSharp NU1902/NU1903 advisory warnings remain. The running Windows development process was left running; production image verification used separate build outputs and an isolated Linux fixture.
