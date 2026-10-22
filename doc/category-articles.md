# Automatic category article lists

Create an article with **Type: Category** and URL slug `category:fashion-magazines`.
Choose any display title, then put this marker on its own line:

```markdown
{{CategoryArticles}}
```

It lists articles containing `{{Category Fashion magazines}}` or any explicit
reference to that identity, such as `{{Category Fashion periodicals|#|fashion-magazines}}`.
The category page's title has no effect on membership. The `category:` prefix is
removed from its stored slug for the lookup. Existing Category-type pages with a
bare slug also work, but category reference links point to `/category:<slug>`, so
use that prefixed slug when creating a new category page.

The list contains current, non-deleted members of the same site and language,
including file and category articles when they explicitly belong to the category.
It uses plain-text article titles and the application's canonical article links.
Titles and URLs are escaped. Ordering is SQLite's default title ordering, then
revision ID to resolve ties; it is deterministic, not language-specific collation.

Results are paged at 100 links using `?CategoryPage=2` and ordinary Previous/Next
links. Numeric values outside the available range are clamped. Empty categories
show a short empty-state message. Pagination needs no JavaScript and works with
the site's ordinary list/link styles. Membership is always current, even while
reading an old revision of the category page; pagination links open the current
category page.

On other article types, the marker displays a placement message. Malformed markers,
inline code, fenced code, and indented code do not create lists. Repeated valid
markers share a single query result. No membership query runs without the marker.

Each render performs an indexed membership count and a bounded page query joined
to scoped current revisions. It does not load or scan member article bodies or
write membership rows. Existing databases need the membership schema, save-time
indexing, and backfill before this is useful; see [deployment procedure](category-membership-schema.md).

The query uses parameterized SQL with SQLite's `CROSS JOIN` to preserve membership-first
lookup order. Query-plan inspection showed ordinary LINQ joins starting from the
site's article index instead. The SQL explicitly enforces site, culture, current,
and non-deleted scope; SQLite integration tests cover those conditions. Named
parameters avoid a paging parameter collision observed with EF-generated names.

Replace the marker with curated content whenever the category page is ready, or
keep it alongside the curated introduction as an automatically maintained index.

Verification: `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~CategoryArticlesTests`.
