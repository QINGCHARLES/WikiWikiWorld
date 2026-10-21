# Issue contents

`{{IssueContents ...}}` displays a manually transcribed publication issue table of contents. It does not generate contents from page headings, query a database, or link entries anywhere. All sections remain visible without JavaScript.

## Example

```markdown
{{IssueContents
Group=Features |#| Featured=true
|~| Title=Wild at Heart |#| Description=The Trinity in the fall collections |#| Credit=Photographs by Steven Meisel
|~| Title=American Anthem |#| Description=Seventh Avenue's new guard |#| Credit=Photographs by Peter Lindbergh
|~| Title=The Shape of Things to Come |#| Description=A 24-page report on the season ahead |#| Credit=Photographs by Herb Ritts
|~| Group=Fashion & Beauty
|~| Title=Grunge & Glory |#| Credit=Styling by Carlyne Cerf de Dudzeele
|~| Title=Bare Essentials |#| Description=The new face, with François Nars
|~| Title=The Coat Question |#| Description=Outerwear for the decade
|~| Group=Regular Features
|~| Title=Letter from the Editor |#| Credit=Anna Wintour
|~| Title=Vogue's View |#| Description=The month in fashion
|~| Title=People Are Talking About |#| Description=Names, faces & openings
|~| Title=Last Look |#| Description=The back page
}}
```

This illustrates the supplied design; verify wording and attribution against the actual issue before publishing.

## Authoring contract

Place the directive on its own line in article Markdown. Single-line and multiline blocks work. Line breaks are whitespace; `|~|` separates items and `|#|` separates properties within an item.

Start a section with a separate `Group=Section title` item. Entries belong to that section until the next group. Add `Featured=true` to a group to span the full available width; regular groups flow into columns where space allows. Any group can be featured. Multiple featured groups and repeated group names are allowed. Source order is preserved, so put featured sections first when they should appear first. Groups must contain at least one entry.

| Entry property | Meaning |
| --- | --- |
| `Title` | Required title as printed. |
| `Description` | Optional descriptive text or subtitle. |
| `Credit` | Optional attribution, including its wording, such as `Photographs by Jane Doe`. |
| `Pages` | Optional printed page number or range, such as `42`, `120–143`, `iv`, or `12, 46–48`. Displayed with a `p.` prefix. Use printed pagination, not the PDF viewer's page index. |

Only `Title` is required for an entry. For an issue without sections, use:

```markdown
{{IssueContents Title=Editor's Letter |#| Pages=3 |~| Title=Feature Story}}
```

Entries before the first explicit group form an unnamed full-width list. No group name is invented. The block heading is “In This Issue”.

All field values are plain text. HTML is escaped, and Markdown formatting, images, and links are not interpreted. Commas, ampersands, and equals signs are preserved. Omit unknown optional properties entirely rather than writing empty values.

The directive name is case-sensitive; property names and Boolean values are case-insensitive. Unknown properties, duplicate properties within an item, missing titles, empty values, invalid Boolean values, and empty groups make the entire block invalid. Invalid source is shown in an error block so entries are not silently discarded. An unclosed block retains the remainder of the article until a closing `}}` is encountered.

The delimiters `|#|`, `|~|`, `{{`, and `}}` are reserved and cannot occur inside values. Nested directives are unsupported. Group headers accept only `Group` and `Featured`; entry items accept only the four properties above.

## Suggested LLM transcription prompt

> Transcribe the magazine's table of contents into one `{{IssueContents ...}}` block using the authoring contract above. Preserve the printed titles, section names, ordering, descriptions, credits, and page labels. Use `Group=` items for sections and separate entries with `|~|`. Separate properties with `|#|`. Every entry needs `Title`; `Description`, `Credit`, and `Pages` are optional. Mark the main feature section `Featured=true` when clearly identifiable; otherwise omit that flag. Do not invent sections, attribution, page numbers, summaries, or links. Omit unknown optional fields. If a title or section is unreadable, report the uncertainty outside the block for human review rather than guessing. Use plain text without HTML or Markdown links, and close the block with `}}`.

## Implementation and verification

The article pipeline registers `IssueContentsExtension`; talk posts do not. Styling lives in `wwwroot/css/components/issue-contents.css` and is registered in `SiteStylesheetCatalog`, including the normal per-site override support. Layout uses intrinsic CSS Grid with no width breakpoints, reordering, animation, or script dependency.

Run `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~IssueContents` for parsing, field preservation, malformed source, HTML escaping, and pipeline boundaries. Check wide and narrow layouts in Chromium, including long titles and page labels.
