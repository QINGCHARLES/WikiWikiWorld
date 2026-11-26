# Site layouts and Markdown profiles

Start with one custom footer for Cinepedia. The target is independent Razor markup for each site's header, footer, and entire homepage, plus a separate choice of article Markdown features. Use the site ID the application already resolves and ship these changes through the existing application image.

## What a site owns

| Surface | Proposed ownership |
| --- | --- |
| Header and footer | The site owns the markup and can change the order, grouping, and placement of navigation, branding, search, and account controls. Shared components provide working search and account controls. |
| Content homepage | The site owns everything inside the main landmark, including its sections, their order, and the queries that supply them. A homepage can be entirely static. |
| Article reading page | Use the shared view initially. Add an optional reading template when a concrete site design needs it. |
| Profile, admin, editor, search, history, discussion, and other application pages | One shared body and interaction model on every site, framed by the site's header and footer. Keep site-specific layout CSS scoped to the surfaces it owns. |
| Culture selector | Keep the existing standalone page on selector-only roots such as `cinepedia.wiki`. The custom homepage lives on the content host, such as `en.cinepedia.wiki`. Single-culture sites serve their homepage at the root. |

## Razor and homepage data

1. **Introduce a small C# registry keyed by `SiteId`.** Each entry names its compiled header and footer partials, homepage component, and article Markdown profile. Sites can share any selection and override one independently. Use explicit defaults for omitted selections. A misspelled explicit selection should fail verification. Keep templates, brand assets, and profile definitions under source control and ship them in the existing image.
2. **Extract the shared shell first.** Keep document language and direction, theme preference, page head and script sections, and the main landmark in the existing layout. Extract the current header and footer intact. Pull the native search form and suggestions, and the signed-in account menu with its permission checks, into reusable controls. A site can place them anywhere in its header. Keep design-specific font preloads and effects with the design that uses them. Reuse the existing stylesheet catalog and check broad selectors such as `body > header` against both designs. [Current layout](../src/WikiWikiWorld.Web/Pages/Shared/_Layout.cshtml) · [Stylesheet catalog](../src/WikiWikiWorld.Web/Services/SiteStylesheetCatalog.cs)
3. **Give each homepage its own data needs.** Keep the existing Index route thin and invoke only the selected homepage. Move the category pills and cover-carousel query together into the Magazedia homepage. A Cinepedia homepage could lead with a featured film and recent film articles, including articles without a cover directive. Each dynamic homepage can use a Razor view component backed by a site- and culture-filtered query and its own typed view model. This avoids an Index model that fetches every site's possible sections. [Current homepage query](../src/WikiWikiWorld.Web/Pages/Index.cshtml.cs) · [ASP.NET Core view components](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/view-components)
4. **Defer the article template until a design calls for it.** Start with ordinary current articles. Let a site arrange the title, hero, byline, actions, and rendered body. Keep the shared route and page model responsible for revision selection, redirects, permissions, and canonical and language metadata. Reuse shared controls for actions and the shared wrapper for rendered Markdown. File and category pages, prior revisions, and editing can continue using the standard presentation. [Current reading view](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml)

## Per-site article Markdown

Change the article factory from `GetPipeline()` to an explicit site-aware lookup. Build and cache one article pipeline per distinct profile. Start every existing site on the exact current feature set, including registration order. Then give a pilot site an explicit list of its supported editorial directives and built-in Markdig features. For example, Magazedia can enable issue credits, issue contents, and cover carousels, while Cinepedia can enable infoboxes with a film schema and choose its own media features. Its layout and its Markdown profile can evolve in separate releases. [Pipeline factory](../src/WikiWikiWorld.Web/Services/MarkdownPipelineFactory.cs) · [Extension reference](../MARKDIG_EXTENSIONS.md)

Keep raw HTML disabled, authored attributes and URLs filtered, and trusted-domain decisions live across profiles. Keep the existing short-content pipeline for discussions, messages, and notes. Treat redirect handling and category membership as shared wiki capabilities initially. Both have application behavior beyond visible HTML, including redirect validation and persisted category membership.

Apply the profile wherever article source is interpreted. This includes rendering and enrichment, web and API save warnings, publication metadata, profile article taglines, and category indexing. The cross-site category backfill currently selects only revision ID and text. Include the revision's site ID so maintenance uses the same rules as a normal save. Check direct syntax readers as well as factory callers. For example, the current warning code can recommend a YouTube directive by scanning plain text, even if its parser is absent. Site-aware authoring guidance should recommend only enabled syntax. [Article renderer](../src/WikiWikiWorld.Web/Services/ArticleRenderer.cs) · [Save warnings](../src/WikiWikiWorld.Web/Services/ArticleSaveWarnings.cs) · [Membership backfill](../src/WikiWikiWorld.Web/Services/CategoryMembershipBackfill.cs)

For an unsupported editorial directive, preserve the source and give the author a nonblocking warning about recognized but disabled syntax. Its site-specific rendering and side effects should be inactive. Before dropping an existing feature, inventory current **and historical** revisions on that site. If it is in use, retain read support until there is an explicit decision about how those revisions should display. Test nested syntax and paired directives together.

### Organizing the extension source

Group implementations under `MarkDigExtensions/` by who owns the syntax. Keep the enabled list for each site explicit in its C# Markdown profile. A shared directive can be optional, and several profiles can use the same implementation.

| Proposed folder | Examples | Reason |
| --- | --- | --- |
| `Shared/` | `MarkdownSafetyExtension.cs`, `DirectiveSyntax.cs`, `Category.cs`, `Image.cs`, `Infobox.cs` | These provide platform rules, common syntax, or reusable directives. The infobox parser already reads site-specific type definitions from `InfoboxSchemas/`. |
| `Sites/Magazedia/` | `IssueContents.cs`, `IssueCredits.cs` | Issue contents and masthead credits express magazine editorial conventions. Keep each directive's parser, renderer, and registration together. |
| `Sites/Cinepedia/` | A future film-specific credits directive | Create this folder when Cinepedia introduces its own syntax. A film infobox alone only needs a schema for the shared parser. |

Review mixed cases individually. For example, the pull-quote renderer is reusable, while its spacing and quotation warnings encode Magazedia's editorial guidance. Keep the rendering shared and select those checks through Magazedia's profile when profiles diverge.

Make the initial file moves a behavior-preserving change. Keep the existing namespace, registration order, and enabled features during the move, update source-path references, and run the extension tests. Change what each site accepts in a subsequent profile change so reviewers can see the author-facing effect.

## Implementation stages

Each stage can be reviewed and shipped independently. Stage 1 is the smallest test. Stages 2 and 3 complete the required visual surfaces. Stages 4 through 6 cover Markdown and can proceed independently of the homepage and header work.

| Stage | Change | Acceptance check |
| --- | --- | --- |
| **1. Cinepedia footer** | Extract the current footer into a shared partial. In the layout, select a custom Cinepedia partial for site 4 and the shared partial for every other site. Give Cinepedia a film tagline and a single row of links that wraps on small screens. | In the same build, visit `/` and `/Search` on English Cinepedia and Magazedia. Both Cinepedia pages show the new footer. Magazedia renders its original footer on both. The search page bodies match. |
| **2. Independent homepage** | Move the magazine category list, cover query, and grid together. Give Cinepedia its own homepage component and site- and culture-filtered data. With two selectable surfaces, consolidate the selections in the small registry described above. | A Cinepedia article without a cover directive can appear on its homepage. Magazedia still shows its cover grid. Each site's root-domain culture selector still works. |
| **3. Independent header and shared defaults** | Extract the current header, factor out search and account controls, and compose a Cinepedia header. Provide neutral defaults for new sites. Keep explicit compatibility selections for existing sites, including Makers. | Exercise signed-in and anonymous navigation, search, theme selection, RTL, narrow screens, and keyboard use. Compare the shared profile, editor, and admin page bodies and permission-dependent actions across sites. |
| **4. Organize extension source** | Move shared and site-owned implementations to the folders described above. Update path references in documentation and tooling. | Build and run the existing tests with the same extension registrations and registration order as before the move. |
| **5. Site-aware Markdown with parity** | Introduce the cached article profiles and pass the owning site ID through every consumer, including cross-site maintenance. Point every configured site to the existing full feature set. | Compare rendering, web and API warnings, metadata, and category rows on two sites with the same input. Exercise saves and backfill on disposable SQLite data and confirm short-content rendering. |
| **6. One real Markdown difference** | Inventory current and historical usage, then select one editorial directive for Magazedia and omit it on Cinepedia. Update author guidance and warnings for the two profiles. | Render and save the same source on both sites. Verify the enabled directive works, the disabled directive retains its source without side effects, and old revisions meet the compatibility decision. |
| **7. Optional article reading layout** | When a concrete design needs it, add an ordinary current-article template to the registry. Build on the existing page model and shared actions. | Compare two current-article layouts, then check historical revisions, metadata, bookmarks, editing links, file pages, and category pages against their established behavior. |

For each release, publish the reviewed code and assets through the normal image workflow. Stage 1 can be reverted with the previous image and needs no data changes. Before shipping newly supported syntax in a later stage, verify how the prior image will display articles that use it.

**Stage 1 verified locally.** Chromium served by one Release build showed the Cinepedia and Magazedia footers on their respective home and search pages. The English search pages had identical main markup. Checks passed at desktop width with JavaScript enabled and at mobile width with it disabled, including the Japanese and Arabic Cinepedia search footers and the standalone culture selector. All 436 .NET tests and the localization completeness check passed.
