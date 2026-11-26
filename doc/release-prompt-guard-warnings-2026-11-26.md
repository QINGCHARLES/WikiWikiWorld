# Prompt-version check and new save warnings — release 2026-11-26

The owner asked how to make sure article-writing agents always follow the latest prompts, chose both safeguards
offered, then asked to deploy.

## Change

- `ac5e242`:
  - `scripts/wiki_api.py` refuses to create or update a Magazedia article without `--prompts common=N,magazine=N`
    (or `issue=N`). After fetching `origin`, it compares those against the prompt headers on `main` and
    `origin/main` and refuses older versions. `--prompts skip` is for saves that are not article writing. This
    needs no deploy; it applies wherever the updated script runs.
  - New save warnings: catalogue numbers in the text (`ISSN`, `ISBN`, `USPS`, `LCCN` or `OCLC` with a number,
    `Company No.`), and a `{{PullQuote}}` whose words are not in the article's own text.
  - `prompts/magazedia-common.md` version 7 documents both; `prompts/README.md`, `MARKDIG_EXTENSIONS.md` and
    `PROJECT.md` updated.

No database change. Nothing under `deploy/` changed.

## Expected warnings on existing articles

Checked against a snapshot of all 205 production articles before release:

- Catalogue numbers: 10, all allowed or harmless: Dragon's ISSN change, Spin-Off's misprint, Star Wars Insider's
  regional ISSNs, Stash's two ISSNs, the Lord of the Rings fan-club ISSN correction, and LCCN records listed under
  External links in Fine Cooking and Saveur.
- Pull quotes not in the prose: 44 quotes in 40 articles. Each needs checking against its source before its quote is
  added to the prose or the pull quote removed.

## Source and deployment

- Source: `ac5e242dfe38` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261126T004624Z-ac5e242dfe38`, published with `latest`. A created container confirmed the image
  has no `appsettings.Development.json`.
- Image ID on the host: `sha256:9fa33a3c79d72912baf65d4fb23297fdd237cb7b2dc6acb8c63eb6d2e388a817`. Started
  `2026-11-26T00:47:05Z`.

## Verification

- All 436 tests passed.
- `wiki_api.py`: a missing, malformed or outdated `--prompts` is refused with exit code 2, including `common=6` once
  version 7 was on `main`; `skip` passes.
- Development: a test article with an ISSN in its lead and a pull quote absent from the prose got both warnings with
  the right line numbers, and no warning for a pull quote whose words were in the prose with different emphasis
  and apostrophes. The test article was then deleted.
- Production:
  - 200 for the en and ja home pages, the sitemap, `/craft` and `/spin-off`; a missing page returns 404.
  - The API answers with MaggieTheBot's token.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines in the first minutes.
- The new warnings were not exercised in production, which would need a write; it is the same build tested in
  development.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T004624Z-ac5e242dfe38` (the pull quote
spacing release, `sha256:43afc9e3…`) to `/home/deploy/update.sh`, then point `latest` at it. Rolling back the image
does not undo the `wiki_api.py` check; revert `ac5e242`'s script change for that.
