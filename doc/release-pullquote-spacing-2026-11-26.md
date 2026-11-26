# Pull quote spacing — release 2026-11-26

The owner reported pull quotes crowding each other, often with one paragraph or none between them, and asked for the
save warning to be deployed.

## Change

- `0e05aff`:
  - `PullQuoteExtension.FindProblems` reports a `{{PullQuote}}` with fewer than four prose paragraphs since the
    previous one. Headings, lists, tables and paragraphs made only of directives do not count.
  - `ArticleSaveWarnings` includes it, so the API returns it in `Warnings` and the editor shows it after saving.
  - `prompts/magazedia-common.md` version 6 requires four paragraphs between two pull quotes. Two lines from the same
    passage get one pull quote, not two.
  - `MARKDIG_EXTENSIONS.md` documents the warning.

No database change. Nothing under `deploy/` changed.

## Content repair (before the release, through the API as MaggieTheBot)

35 production articles had two pull quotes with fewer than four paragraphs between them, 3 with none. Each quote was
kept in its own subsection: the one whose prose quotes it, or else the one it was in.

- American Frontiersman and Fast Company: a quote moved to sit after the paragraph that cites it.
- The other 33 lost one quote: the one not repeated in the prose when only one was, otherwise the second.
- Revision reason: "Space pull quotes at least four paragraphs apart, keeping one where two crowd a section".
  Any removed quote can be restored from the article history.
- Not changed: the GQ Style Summer 2016 and Summer 2017 issue articles (a contents list sits between their quotes,
  and the new warning will flag them on their next save), and Game Informer (not crowded once paragraphs written
  directly under headings are counted).
- Found but not fixed: 69 of the 136 pull quotes on the site do not appear in their article's prose, as the prompt
  requires. Each needs checking against its source.

## Source and deployment

- Source: `0e05aff309eb` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261126T000033Z-0e05aff309eb`, published with `latest`. A created container confirmed the image
  has no `appsettings.Development.json`.
- Image ID on the host: `sha256:43afc9e32b5d35bc999d3651acec11753a2c8806d56732f553c0886fa200aa70`. Started
  `2026-11-26T00:01:17Z`.

## Verification

- All 436 tests passed. In development, saving a test article warned for a quote one paragraph after the previous one
  and not for one four paragraphs after; the test article was then deleted.
- Production:
  - 200 for the en and ja home pages, the sitemap, `/craft` and `/cinefex`; a missing page returns 404.
  - `/craft` and `/cinefex` each show one pull quote.
  - The API answers with MaggieTheBot's token.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines in the first minutes.
- The warning was not exercised in production, which would need a write; it is the same build tested in development.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T000033Z-0e05aff309eb` (the sitemap cache
release, `sha256:07c89444…`) to `/home/deploy/update.sh`, then point `latest` at it.
