# Infobox Website warning and article history — release 2026-11-23

The owner asked to push and deploy, warning that other work had been committed elsewhere and had to be merged.

## Merge

`origin/main` had not moved (`bdb31eb`). Production, though, was running `release-20261123T003921Z-2c6ffb1a3ac6`, built
from the local branch `codex/article-history-release` (`f4c5c2f`, `2c6ffb1`), which was on neither `main` nor `origin`.
Deploying `main` alone would have removed those changes, so the branch was merged into `main` as `cd1cb47`. It touched
no file that `main` had changed since their common base, `74b3327`.

Two uncommitted copies of that branch's files in the main checkout (`History.cshtml.cs`, identical to `2c6ffb1`;
`RedirectTests.cs`, identical to `f4c5c2f`) were stashed, not discarded, so the merge could run. They are still in
`git stash list`.

## Change

- `f4aacc5`: schema fields gain an optional `Format` (`Text` by default, or `Website`). Magazedia's Publication
  `Website` field uses `Website`. Saving reports a value that is not one Markdown link to a full `http`/`https` URL
  whose text is the URL's host without a leading `www.` (or a `{{YouTubeLink}}`), with the exact value to write
  instead. The app refuses to start on an unknown `Format`.
- `7db5ae0`: the rule in the magazine prompt (v24), the `wiki:infobox` help text and the schema description.
- `d947952`, `72003b8`: the retired `{{PublicationIssueInfobox}}` name dropped from the issue prompt and help page.
- `7bb6ddc`: `prompts/husni_magazines.json`, research data; not part of the image.
- From the Codex branch, already live before this release: article history follows renames (`f4c5c2f`), and older
  revisions have a read-only Markdown source page at `/{slug}/revision/{timestamp}/source` (`2c6ffb1`).

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `cd1cb475c9c6` on `main`, pushed to `origin/main` (`bdb31eb..cd1cb47`) before publishing.
- Release: `release-20261123T012903Z-cd1cb475c9c6`, published with `latest`, family `resolute-chiseled-extra`.
- Production image: `ghcr.io/qingcharles/wikiwikiworld@sha256:62ec45589bf50d97cd2ee7ec22996c947e0dd6031dc705f8fd2e980ece3acc37`,
  pulled by tag and run by digest through `/home/deploy/update.sh`, which matches the repository copy. Started
  `2026-11-23T01:29:38Z`. Before the swap, a created (not started) container confirmed the image's `magazedia.json`
  has `"Format": "Website"`.

## Verification

- Merged tree: Release build with only the existing ImageSharp advisories; all 426 tests passed.
- Development, before the merge: six wrong Website forms (bare URL; `https://`, `www.` or a path in the text; a link
  without a protocol; plain text) were reported with the fix; good values and `{{YouTubeLink}}` were not; the 20
  Website values on production raised nothing.
- Production, anonymous: 200 for the en/ja/ar Magazedia home pages, george, sight-and-sound, wiki:infobox, the
  sitemap, the new category page, Cinepedia, fast-company's history, an older revision and its `/source` page (which
  shows that revision's Markdown). Running, zero restarts, no `fail:`, `crit:` or exception log lines.
- After the deploy, the live `wiki:infobox` sentence on save warnings now mentions the Website check (matching the seed).
- Not verified in production: a save that triggers the Website warning, since no live article breaks the rule.

## Rollback

The previous image is kept on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261123T012903Z-cd1cb475c9c6`
(`sha256:b373a83f3e08…`, the article-history release). Pass it to `/home/deploy/update.sh`; no database restore is
needed. The schema file is inside the image, so the old image starts with its own copy and simply has no Website
warning.
