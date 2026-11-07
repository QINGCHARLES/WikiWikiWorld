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

Production release details are recorded below after deployment.
