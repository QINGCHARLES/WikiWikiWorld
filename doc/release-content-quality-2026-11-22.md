# Content-quality pass — release 2026-11-22

The owner approved pushing and deploying ("Push and deploy").

## Change

- `74b3327`:
  - Bare `www.` text no longer autolinks in articles, footnotes or timeline descriptions (`BareWwwTextExtension`), so old websites can be written exactly as printed. Explicit links and bare `https://` URLs still link. Discussions, messages and notes are unchanged.
  - New inline `{{YouTubeLink Video=… / Channel=UC… / Handle=@… |#| Label=…}}` renders a nofollow link with a YouTube play mark.
  - Timelines now honour nested directives, both in their properties and in where they end.
  - Saving reports email addresses, YouTube addresses and raw channel IDs in the text, and invalid `{{YouTubeLink}}`, alongside the infobox warnings (`ArticleSaveWarnings`). The API returns them in `Warnings`; the editor shows them under "Saved, but the article needs attention."
- `b7a3cae`, `74b3327`: PROJECT.md authoring rules.
  - Code formatting is for real code only.
  - Websites are written as printed; a dead one is linked to a working Wayback capture when there is one.
  - YouTube is linked with `{{YouTubeLink}}`.
  - Email addresses appear only when notable.
  - Agents read save warnings.

No database change. Nothing under `deploy/` changed.

Before deploying, all 35 production timelines were checked: the new nesting-aware rule ends each one at the same place as the old rule (checker self-tested on a nested case).

## Source and deployment

- Source: `74b3327fa014` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261122T013733Z-74b3327fa014`.
- Production image: `ghcr.io/qingcharles/wikiwikiworld@sha256:9b21d8554d3d73fb4671b60b5700661a76792d8bc43c59b81e5c0997db3875bf`, published with the release tag and `latest`, pulled by tag, run by digest through `/home/deploy/update.sh`. Started `2026-11-22T01:38:16Z`.

## Content edits (MaggieTheBot)

Each article was fetched just before its update and skipped if it had changed since the snapshot. Each change was checked by rendering the old and new text on the development server and comparing the visible text.

- Code spans removed in 11 articles:
  - the-lord-of-the-rings-fan-club-official-movie-magazine, melody-maker, stash, guitar-player, dragon
  - heavy-metal, mojo, american-cinematographer, starlog, stereophile, white-dwarf
- First mentions of dead or reassigned sites now link to a Wayback capture that loads: lotrfanclub.com, fanHQ.com, lotr.fanHQ.com, www.of-the-shire.net, dragonmag.com, cinematographer.com, ascmag.com.
- rural-cooperatives: removed the office email address CoopInfo@usda.gov.
- mojo: the contents-page datelines for Issues 260, 288 and 300 were corrected to "LONDON ✦ MEMPHIS ✦ …", checked against the scans. The text had read "a" and ")" from PDF text extraction.
- After the deploy:
  - the-muse: www.creative-process-films.net restored as printed, and the YouTube channels converted to `{{YouTubeLink}}`.
  - wiki:youtube: replaced with the seed text, which adds "Linking to YouTube in text".
  - wiki:formatting: one index line added to the live text. The seed in `scripts/seed_wiki_docs.py` is behind the live page, which has later edits such as a Redirects entry.
- A scan of all 478 current articles through the new save checks flagged only the-muse, which was fixed after the deploy.

## Verification

- Before shipping:
  - All 425 .NET tests passed.
  - Development server checks covered: www. text in prose, tables, timelines and footnotes; explicit and https links; each `{{YouTubeLink}}` form in text, the infobox and a timeline; an invalid one; and the warnings in the API response.
- Production, anonymous:
  - 200 for the en/ja/ar Magazedia home pages, the-muse, the LOTR fan club magazine, mojo, the sitemap, Cinepedia and CRTR.
  - The new CSS is served.
  - the-muse shows three `youtube-link` links and four plain www.creative-process-films.net mentions.
  - Zero restarts; no `fail:`, `crit:` or exception log lines.
- Not verified: Safari and Android browsers; the editor's warning banner in production, which needs a signed-in save.

## Rollback

The previous image is kept on the host as `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261122T013733Z-74b3327fa014` (`sha256:96dd642e0155…`, the subhead spacing release). Pass it to `/home/deploy/update.sh`; no database restore is needed.

After a rollback, three things in the content would no longer render as intended:
- The `{{YouTubeLink}}` directives in the-muse and wiki:youtube would show as text.
- The www. addresses would autolink again.
