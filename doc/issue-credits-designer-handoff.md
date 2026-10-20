# Issue credits: designer handoff

The C# Markdig extension renders a credits card at the `{{IssueCredits ...}}` position in an article. It supplies semantic HTML and CSS hooks; this change adds no CSS or JavaScript. The reference image guides the grouping and content hierarchy, not fixed typography or spacing.

Read [the authoring guide](issue-credits.md) for a complete example. Open [the unstyled HTML fixture](issue-credits-example.html) to try the controls and develop styles.

## Behavior to preserve

- The card has a “Credits” heading.
- Featured groups use a regular section and heading. They are always visible and must not look like clickable disclosures.
- Other groups use native `details` with a `summary` as the first child. They default closed. Authors may opt into `open` on any group.
- Multiple groups can be open simultaneously. There is no exclusive accordion or JavaScript state.
- Each group can contain role/name entries, simple name lists, or both. Group and contributor order follows the source.
- Contributor names may be links or contain emphasis. Each person has a separate list item, allowing stacked or wrapping inline layouts without editing the renderer.
- No slash or dot separators are baked into the HTML. CSS can provide decorative separators between list items; keep actual names and links intact.

## Class contract

| Selector | Element / purpose |
| --- | --- |
| `.issue-credits` | `aside`: entire card |
| `.issue-credits-heading` | `h2`: “Credits” |
| `.issue-credits-group` | `details` or `section`: one group |
| `.issue-credits-group-featured` | Additional class on always-visible `section` |
| `.issue-credits-title` | `summary` for collapsible groups; `h3` for featured groups |
| `.issue-credits-body` | `div`: group contents |
| `.issue-credits-entry` | `dl`: one role and its contributors |
| `.issue-credits-role` | `dt`: role label |
| `.issue-credits-people` | `dd`: names belonging to the role |
| `.issue-credits-names` | `ul`: list of contributors; directly in the body when no role is supplied |
| `.issue-credits-name` | `li`: one contributor, optionally containing an `a`, `em`, or `strong` |
| `.issue-credits-error` | `pre`: visible source for a malformed directive |

Use `.issue-credits-group[open]` for expanded disclosure styling and `.issue-credits-group-featured` for featured styling. Avoid styling all `.issue-credits-title` elements as interactive: only `summary` is clickable.

## Interaction and layout checks

Retain a visible keyboard focus indicator on summaries and contributor links. Native summaries support keyboard and touch without custom handlers. If replacing the browser's disclosure marker, supply a clear collapsed/expanded indicator; keep it decorative. Do not add manual `aria-expanded` attributes, hide featured bodies, or override the native closed state.

Allow long roles, long names, many contributors, and several featured groups to wrap at narrow widths. Preserve DOM reading order, link visibility, and list semantics. If adding animation, respect reduced-motion preferences. The unstyled fixture includes featured, closed, and initially open groups.

The existing project loads shared CSS through `SiteStylesheetCatalog`. When the stylesheet is ready, register it in that catalog and follow the existing optional site-override convention described in `PROJECT.md`.
