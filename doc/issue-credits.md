The `{{IssueCredits ...}}` directive displays magazine issue contributors in author-defined groups. Featured groups stay visible; other groups can be expanded by the reader and start closed unless `Open=true` is specified.

## Complete example

```markdown
{{IssueCredits
Group=Editor-in-Chief |#| Featured=true
|~| Name=[Anna Wintour](/anna-wintour)
|~| Group=Editorial Leadership
|~| Role=Creative Director |#| Name=[Grace Coddington](/grace-coddington)
|~| Role=Art Director |#| Name=Roxanne Behr
|~| Group=Cover Models
|~| Name=[Linda Evangelista](/linda-evangelista) |#| Name=Naomi Campbell |#| Name=Christy Turlington
|~| Group=Photography |#| Open=true
|~| Role=Cover |#| Name=Patrick Demarchelier
|~| Role=Editorial |#| Name=Steven Meisel |#| Name=Peter Lindbergh |#| Name=Herb Ritts
|~| Group=Production & Styling
|~| Role=Styling |#| Name=Example Stylist
}}
```

Use a standalone block in an article. Single-line and multiline forms work. Line breaks are for readability: `|~|` separates items and `|#|` separates properties.

## Group headers

Start each group with its own item. All following credit entries belong to that group until the next `Group=` item.

| Property | Meaning |
| --- | --- |
| `Group` | Required plain-text title. Any section or role title is allowed. |
| `Featured` | Optional `true` or `false`; defaults to `false`. A featured group is always visible and has no collapse control. |
| `Open` | Optional `true` or `false`; defaults to `false`. Opens a collapsible group initially. Ignored for featured groups. |

Group headers cannot also contain `Name` or `Role`. Each group must have at least one credit entry. Multiple featured groups are allowed, anywhere in the block. Groups are independent: opening one does not close another. Repeating a group title creates a separate group.

## Credit entries

| Property | Meaning |
| --- | --- |
| `Role` | Optional plain-text role, such as `Art Director` or `Cover`. |
| `Name` | Required; repeat once for each contributor. |

Names support inline Markdown, including emphasis and wiki article links: `[Anna Wintour](/anna-wintour)`. Use the destination article's actual URL slug; the extension does not look up people, create articles, or infer slugs. There is no special `[[...]]` syntax. Plain-text names also work.

Names are never split on commas, slashes, or dots. Repeated `Name=` properties preserve each contributor separately, in source order. Omit `Role` for a simple list, such as cover models. Group titles and roles are HTML-escaped plain text.

## Rules and troubleshooting

- The directive name `IssueCredits` is case-sensitive. Property names and Boolean values are case-insensitive.
- Only `Name` is repeatable within an item. Duplicate other properties, unknown properties, empty values, missing names, and empty groups make the whole block invalid.
- Invalid or unclosed blocks display their source in an error block instead of silently losing credits. Close every directive with `}}`; an unclosed directive collects the remaining article text.
- The reserved delimiters `|#|`, `|~|`, and `}}` cannot appear inside values. Nested custom directives are unsupported.
- Contributor names are intended for inline content. Block Markdown is displayed as plain text. Raw HTML is escaped; non-HTTP(S) absolute link targets and targets containing control characters are replaced with an empty target. External links follow the site's approved-domain policy.
- This extension is enabled in articles, not talk posts. No JavaScript or new stylesheet is required for its expand/collapse behavior.

*Return to the [Formatting Guide](/wiki:formatting).*
