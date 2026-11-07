# Browser script regression tests

From the repository root:

```powershell
node --test tests/browser/recipient-autocomplete.test.cjs tests/browser/admin-article-picker.test.cjs
```

These tests use Node's built-in test runner and execute the actual inline scripts from `Pages/Profile/NewMessage.cshtml` and `Pages/Admin/DomainRedirects.cshtml` in an isolated event/DOM harness. No npm packages, frontend build, server, database, or credentials are needed. Node 24.19.0 is verified.

The 14 recipient cases cover clearing before/after the debounce, reordered responses, returning to a previous query, Escape/outside-click/blur dismissal, empty-list keyboard handling, selection/wrapping, result replacement, failed and empty searches, a response body completing after dismissal, and multi-recipient final-token selection without overwriting earlier recipients. Changing an earlier recipient also invalidates pending results. The fake fetch deliberately allows aborted requests to resolve, so the checks exercise response ownership as well as cancellation.

The small DOM harness does not emulate native layout, focus, pointer delivery, or form submission. On 2026-11-06, separate Chromium checks used the actual page script and CSS in an isolated local form fixture with controlled responses. They verified keyboard and pointer selection, touch emulation, input focus, stale-result rejection, Escape/blur dismissal, and normal form data with JavaScript enabled and disabled. No messages were sent and no existing database was changed. This was not an authenticated end-to-end test of the running app or a screen-reader test.

The 13 admin cases cover immediate invalidation of selected targets, disabled dependent controls, reordered cultures and article results (including returning to an earlier context), failed/empty culture responses and retry, culture changes, shortened/cleared queries, stale failures/spinners, search errors, literal result text, and selection/change behavior.

On 2026-11-06, separate Chromium checks of the actual admin script in an isolated native-form fixture verified reordered culture/article responses, keyboard/pointer selection, selected values in `FormData`, change-button focus, failed-culture retry, and clearing during a pending search. A second pass included the actual shared reset/button CSS to verify result styling and retry-button visibility. The fixture does not authenticate or create real redirects. Three SQLite service cases independently verify valid redirects and refusal of wrong-site/wrong-culture targets.

The editor fallback is covered separately by 11 SQLite-backed Razor handler cases in `UserArticleEditorTests`, including a deliberately failed provisioning save followed through the viewer redirect to successful editor creation. These tests compile the Razor UI but do not exercise its authenticated browser rendering.

The replacement private-messaging flow also received authenticated Chromium checks with four synthetic accounts and a fresh disposable SQLite database on ports 17443/17080. Both recipients could read the thread; outsiders received 404 for GET and POST; missing antiforgery tokens received 400. Native compose/reply worked without JavaScript, private pages returned `no-store,no-cache`, and the 390px conversation fit without horizontal page overflow. No real development or production accounts were used. See [private messaging](../../doc/private-messaging.md) for schema and cutover checks.

After Razor changes, also compile and run the application tests:

```powershell
dotnet test tests/WikiWikiWorld.Web.Tests -c Release
```
