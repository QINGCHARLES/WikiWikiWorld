# Category labels and slugs

`{{Category Fashion magazines}}` derives `fashion-magazines` from its label.
`{{Category Fashion magazines|#|fashion-magazines}}` supplies an explicit identity.
Both sides of `|#|` are trimmed. Labels can change independently of explicit slugs.
Explicit slugs are not lowercased, normalized, translated, or derived again.

Shorthand conversion follows Django 5.2's Unicode slugify approach: NFKC
normalization, .NET invariant lowercase, punctuation removal, whitespace/hyphen
collapse, and trimming of edge hyphens/underscores. It preserves native scripts,
numbers, and underscores. Unlike Django's Python word-character filtering, it
deliberately preserves Unicode combining marks. Iteration uses Unicode scalar
values so supplementary-plane letters survive. This is a documented adaptation,
not byte-for-byte Python casing or regex compatibility. The source attribution
and BSD license are shipped in `ThirdPartyNotices/Django-LICENSE.txt`.

Explicit slugs must be nonempty single path segments, not `.` or `..`, and cannot
contain whitespace, control characters, replacement characters, `/`, `\`, `?`,
`#`, `%`, `:`, `|`, braces, angle brackets, or double quotes. Ordinary punctuation
such as parentheses and apostrophes is retained; URLs and labels are escaped by
the renderer. Invalid directives remain source text and declare no membership.
An explicit empty slug does not fall back to derivation. Code examples declare
no membership. Duplicate references remain in source order; membership writers
must deduplicate by slug when that later step is implemented.

## Compatibility before deployment/backfill

The former function only trimmed, replaced ordinary spaces with hyphens, and
lowercased. This implementation changes punctuation-bearing shorthand URLs found
in the repository's existing article seed data, for example:

| Label | Former slug | New shorthand slug |
| --- | --- | --- |
| GQ Style (USA) magazines | gq-style-(usa)-magazines | gq-style-usa-magazines |
| Men's Fashion and Lifestyle | men's-fashion-and-lifestyle | mens-fashion-and-lifestyle |

The owner approved adopting the cleaner slugs for the four affected categories
on 2026-10-22. Independent development and production snapshot audits each found
46 categories, 236 references across 58 articles, four changed category identities
across five articles, no collisions, and no category pages. The affected labels
are `Men’s magazines`, `Men's Fashion and Lifestyle`, `GQ Style (USA) magazines`,
and `Publication issues with covers featuring Robert Downey Jr.`.

The targeted migration uses `scripts/apply_category_slug_plan.py` with a
parser-audited JSON plan, database path, and existing author ID. It defaults to
read-only validation; `--apply` performs all edits in one transaction. It checks
source revision IDs and SHA-256 hashes, permits only the four approved token
replacements, and adds explicit cleaner slugs while preserving labels and old
revision contents. Repeated application skips already-migrated content. The
new parser must be deployed before applying explicit syntax. Keep verified
SQLite backups and the audit plans outside Git. Test on disposable backup copies
before updating each database. Membership indexing/backfill remains pending;
article URLs are unchanged. General category renaming remains out of scope.

Verification: `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~Category`
