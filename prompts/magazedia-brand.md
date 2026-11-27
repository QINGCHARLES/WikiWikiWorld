# Magazedia brand article add-on

Version 2, 27 November 2026 (version 1 with the lead example bolding the brand name). Read `magazedia-common.md`, then `magazedia-magazine.md`, then this file; it applies only to an article about a brand with several national or language editions, titled by the bare name (common rules, section 2). Where this file and the magazine prompt differ, this file wins for a brand article. This is prompt metadata, not article text.

You are writing the article about **[BRAND_NAME]** as a whole: what the brand is, where it began, who owns it, and which editions it has had. Each edition's own history, editorial profile and issues belong in that edition's article, such as *GQ (USA)*.

## 1. What changes from the magazine prompt

- **Research (magazine prompt, section 1)** applies in full. Keep the brand and each edition distinct: a brand-wide claim such as "published in 21 countries" does not establish any one edition's launch date, publisher or status. Source each edition's facts separately, preferably from the edition itself, its publisher, or trade coverage of its launch or closure.
- **Editorial profile (magazine prompt, section 2)** does not apply. Describe the shared concept: the original edition's founding idea, what every edition carries, and documented differences between editions. Do not summarize what individual issues contained.
- **Article structure (magazine prompt, section 3)** is replaced by section 2 below. The lead hook, small-table, ending and category rules still apply.
- **Infobox (magazine prompt, section 4)**: use `Founder`, the brand owner as `Publisher`, the original edition's `FirstIssue` and `Country`, and the brand's `Website` if it has one. Leave out `ISSN`, `Frequency`, `Circulation` and `Editor`, which belong to the editions.
- **Timeline**, if used, holds brand milestones: the original launch, the first international edition, ownership changes, and major launches or closures of editions.

## 2. Structure

1. **Lead:** the first sentence defines the brand: "***GQ*** is an American men's magazine brand owned by Condé Nast, published in … editions." The second states the most striking verified fact about the brand as a whole. Then summarize its origin and spread in a sentence or two.
2. **History:** founding of the original edition and how the brand spread, with ownership and licensing changes. Keep each edition's detail to a sentence; link its article.
3. **Editions:** one table, one row per edition with at least a launch year and status. Link editions that have Magazedia articles, cite every row, and leave out a row you cannot support. Add a row for an edition only from a source about that edition. The rows below show the form only; take every value from your sources.

```markdown
| Edition | Launched | Publisher | Status |
| --- | --- | --- | --- |
| [*GQ (USA)*](/gq-usa) | 1957{{Citation …}} | Condé Nast | Continuing |
| *GQ (UK)* | 1988{{Citation …}} | Condé Nast Britain | Continuing |
```

4. Optional sections from the magazine prompt where the evidence supports them at brand level: Ownership and management, Reception and impact, Digital presence.

A brand article is often short. Do not pad it with edition detail to reach a length.

## 3. After publishing

- Add the edition hatnote (common rules, section 7) to every existing edition article of this brand, as separate revisions with their own revision reasons. Merge it with any existing shared-name hatnote into one sentence.
- In the Notes for the editor, list the hatnotes added, the editions in the table that have no article yet, and any edition articles whose facts conflict with your sources.
- Publish with `--prompts common=N,magazine=N,brand=N`, giving all three versions you read.
