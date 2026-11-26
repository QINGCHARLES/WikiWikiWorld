# Magazedia article prompts

These are the only prompts for writing Magazedia articles. Every agent that writes or rewrites a Magazedia article — Claude, Gemini, or a tool such as the Magazedia Article Writer — uses these files. Do not keep or edit copies elsewhere; if a tool needs the text, it reads it from here.

| File | Version | Use it for |
| --- | --- | --- |
| [magazedia-common.md](magazedia-common.md) | 9 | Every article. Article-only output, titles, sources, prose, romanization, Markdown and formatting (code, websites, YouTube, email), top lines and display title, infoboxes, citations, end matter and categories, publishing, Notes for the editor, shared final checks |
| [magazedia-magazine.md](magazedia-magazine.md) | 28 | An article about a whole publication: a magazine, zine, journal, mook, newsletter, or fictional magazine |
| [magazedia-brand.md](magazedia-brand.md) | 1 | Added to the magazine prompt for an article about a brand with several national editions, titled by its bare name (*GQ*): editions table, brand infobox, edition hatnotes |
| [magazedia-issue.md](magazedia-issue.md) | 5 | An article about one issue of a publication, usually from page scans |

## How an agent writes an article

1. **Read both files, in order, before each article:** `magazedia-common.md`, then the magazine or issue prompt. For a brand article, read `magazedia-brand.md` after the magazine prompt. The common rules apply to every article; the second file adds the rules for its kind of article and points back to common sections by number ("common rules, section 3"). Where the two disagree, the specific prompt wins only where it says it overrides a common rule.
2. **Fill in the inputs.** `[MAGAZINE_NAME]` is the publication's name as requested. The issue prompt's section 1 lists its other inputs (issue designation, page images, known slugs). An agent working without a person supplies what it can establish and records the rest in the Notes for the editor.
3. **Research, write, and verify** as the prompts direct. Inspected scans, including issues in the local archive at `M:\Magazedia\Magazines (Sorted)\`, may be cited without a URL (common rules, section 3).
4. **Publish** through [`scripts/wiki_api.py`](../scripts/wiki_api.py) as `MaggieTheBot` on production, or `DevvyTheBot` on development (common rules, section 11), passing `--prompts` with the versions read in step 1. The tool refuses the save if a prompt has changed on `main` since then; reread it, recheck the draft and save again. Fix every save warning the API returns, then load the page and check it renders.
5. **Report** the Notes for the editor in the reply (common rules, section 12): what was published, title and status decisions, conflicts, omissions, and articles worth adding.

One agent writes one publication's article. To write several, give each publication its own agent.

The prompts assume the agent can browse the web, read files and images, and run Python from the repository root. An agent without those abilities returns the article in a `markdown` code block followed by the Notes for the editor, and a person publishes it.

## What the prompts rely on

The prompts describe the site's actual behaviour. When any of these change, update the prompts in the same commit:

- the Markdown pipeline and directives: [MARKDIG_EXTENSIONS.md](../MARKDIG_EXTENSIONS.md);
- the infobox keys: [`InfoboxSchemas/magazedia.json`](../src/WikiWikiWorld.Web/InfoboxSchemas/magazedia.json);
- the API, display titles and save warnings: [doc/api.md](../doc/api.md);
- the wiki's own help pages (`wiki:formatting` and its sub-pages).

## Changing a prompt

- Put a rule in `magazedia-common.md` when it applies to both kinds of article; put it in the magazine or issue prompt only when it is specific to that kind. Never copy a rule into both.
- Bump the changed file's version and date in its header and in the table above, and add a line to the change log below. Keep the header's `Version N, ` form: `wiki_api.py` reads it, and a bump makes the tool refuse saves from drafts written under the older version until they are rechecked. Push the change, since the tool compares against `main` and `origin/main`. A version number belongs to one file; the common rules are versioned separately from the other two.
- Keep section numbers stable where possible. If a common section is renumbered, update every "common rules, section N" reference in the other two files.
- Commit the change; the Git history is the archive of earlier versions. Do not leave dated copies in other folders.

## Change log

- **26 November 2026 — common 9, magazine 28, brand 1.** Brand articles get their own add-on, `magazedia-brand.md`, read after the common and magazine prompts: it replaces the magazine prompt's structure and editorial-profile rules with a brand lead, an editions table, a brand infobox and the edition-hatnote backfill. Brand saves pass `--prompts common=N,magazine=N,brand=N`.

- **26 November 2026 — common 8, magazine 27.** A brand with several national editions gets an article under its bare name (*GQ* at `/gq`), never "(franchise)". Each edition article carries an edition hatnote pointing to it ("This article is about the US edition. For the magazine's other editions, see *GQ*."), added only once the brand article exists; until then the Notes for the editor name the missing brand article. Whoever publishes a brand article adds the hatnote to every existing edition article.

- **26 November 2026 — common 7.** Publishing passes `--prompts` with the prompt versions followed, and `wiki_api.py` refuses an article save when they are older than `main`, so drafts written under superseded rules are rechecked; read the prompts before each article, not once per batch. New save warnings: catalogue numbers in the text, and pull quotes whose words are not in the prose.

- **25 November 2026 — common 6.** Two pull quotes need at least four body paragraphs between them, normally in different sections; two lines from the same passage get one pull quote, not two. Final check 7 covers it, and saving warns when two pull quotes are closer.
- **25 November 2026 — common 5, magazine 26.** No catalogue data in prose (section 4 and final check 11): ISSNs, ISBNs, barcodes, catalogue and registration numbers, and legal-form suffixes such as Limited or LLC stay out of the lead and body, unless the identifier itself is the fact. A suffix stays when it is part of the familiar name (Time Inc., Future plc) or tells apart two otherwise identical company names; naming a USPS filing as a source is fine, but a postal-registration number is not. The magazine prompt's ISSN rule now points to it, so the rule covers issue articles too.
- **24 November 2026 — common 4.** Trusted domains (section 11): after publishing, propose each official domain the article links to (the publication's or publisher's site, libraries, archives, museums, government) that is not yet trusted or proposed, through `POST api/trusted-domains` with a reason; never open-publishing sites, shops or ordinary secondary sources; a `409` means nothing to do. The Notes for the editor list the domains proposed.
- **23 November 2026 — common 3.** `{{Hatnote}}`: when another Magazedia article shares the name, one line after the Eyebrow and any HeaderImage ("For …, see …." or "Not to be confused with …"), linking only to an existing article; propose the matching hatnote for the other article in the Notes for the editor. Final check 2 covers it.
- **23 November 2026 — common 2, issue 5, magazine 25.**
  - Pull quotes: two in an article with three or more body sections, one in a shorter one, none when nothing qualifies. Each is a verbatim passage of 10 to 40 words that also appears, cited, in the prose of its section; attributed; with one to three bold words; placed between body paragraphs away from other boxes. The issue prompt no longer says to omit them.
  - Paragraphs of 40 to 120 words; a section over about 500 words, or spanning several eras or themes, is divided into named `###` subsections of at least two paragraphs.
  - Common final checks 6 and 7 cover the two rules above.
  - Magazine prompt: the lead's second sentence states the most striking verified fact, plainly and cited. Small tables (editors, owners, cover price, circulation, format changes) when there are at least four cited rows, at most two per article, never repeating the timeline or infobox.
- **22 November 2026 — magazine 24.** Infobox `Website`: the link goes to the full URL with its protocol; the visible text is only the host name, without `www.` but with any other subdomain. Saving warns when a `Website` breaks the rule.
- **22 November 2026 — common 1, magazine 23, issue 4.** Moved the rules the magazine prompt (v22) and issue prompt (v3) shared into `magazedia-common.md`, and moved the prompts into this repository as the single copy. New or changed rules since v22 and v3:
  - Code formatting only for real code; escape Markdown punctuation (including `{…}`) in plain text.
  - Websites written as printed, `www.` included (bare `www.` no longer becomes a link); dead sites linked only to a working Wayback capture, at the first mention.
  - YouTube videos and channels named with `{{YouTubeLink}}`; it also works in infobox values and timeline descriptions.
  - Email addresses only when the address itself is notable; no private contact details.
  - The display title is plain text, never Markdown.
  - Agents publish through `scripts/wiki_api.py`, fix every save warning, and then report the Notes for the editor; the code-block output is the fallback.
  - Inspected scans, including the local archive, may be cited without a URL in magazine articles too (previously issues only).
  - Never cite a search-engine or grounding redirect URL; cite the destination page.
  - Propose a redirect from each former name in the Notes for the editor.
  - Articles are created with `wiki_api.py --form-file Text=article.md`, a new option, because article text is too long for a command line.
  - Magazine prompt: ISSNs belong in the infobox; errata only from the publication itself; length and sections follow the evidence, with no minimum.
  - Issue prompt: the display title is the title with each ` - ` replaced by a line break.
- **19 November 2026 — magazine 22, issue 3.** Infobox placement after the first paragraph and splitting of long infoboxes (previous versions were kept outside the repository).
