const Assert = require('node:assert/strict');
const { readFileSync: ReadFile } = require('node:fs');
const Path = require('node:path');
const { test: Test } = require('node:test');
const Vm = require('node:vm');

const Page = ReadFile(Path.join(__dirname, '../../src/WikiWikiWorld.Web/Pages/Admin/DomainRedirects.cshtml'), 'utf8');
const Script = Page.match(/<script>([\s\S]*?)<\/script>/)[1];

/** Executes the actual picker with manually ordered fetches, even when a request's signal has been aborted. */
function CreateHarness() {
	class Element {
		constructor(Tag = 'div') { this.Tag = Tag; this.children = []; this.Listeners = new Map(); this.style = {}; this.value = ''; this.disabled = false; this.hidden = false; }
		addEventListener(Name, Handler) { if (!this.Listeners.has(Name)) this.Listeners.set(Name, []); this.Listeners.get(Name).push(Handler); }
		Dispatch(Name) { for (const Handler of this.Listeners.get(Name) ?? []) Handler.call(this, { target: this, preventDefault() {} }); }
		set innerHTML(Value) { this.Html = Value; this.children = []; if (this.Tag === 'select') this.value = ''; }
		get innerHTML() { return this.Html ?? ''; }
		replaceChildren(...Children) { this.children = []; if (this.Tag === 'select') this.value = ''; this.append(...Children); }
		appendChild(Child) { this.children.push(Child); if (this.Tag === 'select' && this.children.length === 1) this.value = Child.value; return Child; }
		append(...Children) { Children.forEach(Child => this.appendChild(Child)); }
		setAttribute(Name, Value) { this[Name] = String(Value); }
		click() { this.Dispatch('click'); }
		focus() { this.Focused = true; }
	}
	const Elements = Object.fromEntries([
		'InputSiteId', 'InputCulture', 'article-search-input', 'article-results-list', 'search-spinner',
		'InputCanonicalArticleId', 'SelectedArticleTitle', 'selected-article-display', 'selected-article-text',
		'selected-article-guid', 'article-search-container', 'clear-article-btn', 'article-picker-status', 'retry-cultures'
	].map(Id => [Id, new Element(Id === 'InputSiteId' || Id === 'InputCulture' ? 'select' : 'div')]));
	Elements.InputSiteId.value = '1'; Elements.InputCulture.value = 'en';
	Elements['article-results-list'].style.display = 'none';
	Elements['search-spinner'].style.display = 'none';
	Elements['retry-cultures'].style.display = 'none';
	const Requests = [], Timers = new Map();
	let TimerId = 0;
	const Document = {
		getElementById: Id => Elements[Id], createElement: Tag => new Element(Tag), createTextNode: Text => ({ textContent: Text }),
		addEventListener: (Name, Handler) => { if (Name === 'DOMContentLoaded') Handler(); }
	};
	Vm.runInNewContext(Script, {
		document: Document, AbortController, URLSearchParams,
		setTimeout: Handler => { Timers.set(++TimerId, Handler); return TimerId; }, clearTimeout: Id => Timers.delete(Id),
		fetch: (Url, Options) => new Promise((Resolve, Reject) => Requests.push({ Url, Options, Resolve, Reject }))
	});
	return {
		Elements, Requests,
		ChangeSite(Value) { Elements.InputSiteId.value = Value; Elements.InputSiteId.Dispatch('change'); },
		ChangeCulture(Value) { Elements.InputCulture.value = Value; Elements.InputCulture.Dispatch('change'); },
		Type(Value) { Elements['article-search-input'].value = Value; Elements['article-search-input'].Dispatch('input'); },
		RunTimers() { const Pending = [...Timers.values()]; Timers.clear(); Pending.forEach(Callback => Callback()); },
		async Respond(Index, Data, Ok = true) { Requests[Index].Resolve({ ok: Ok, json: async () => Data }); await new Promise(Resolve => setImmediate(Resolve)); },
		async Fail(Index) { Requests[Index].Reject(new Error('Network unavailable')); await new Promise(Resolve => setImmediate(Resolve)); }
	};
}

const Article = (Title = 'Article') => ({ CanonicalArticleId: 'some-guid', Title, UrlSlug: 'article' });

Test('a late culture response cannot replace the cultures of the newer selected site', async () => {
	const Harness = CreateHarness();
	Harness.ChangeSite('2'); Harness.ChangeSite('3');
	await Harness.Respond(1, ['de']); await Harness.Respond(0, ['ja']);
	Assert.equal(Harness.Elements.InputCulture.value, 'de');
});

Test('clearing a query while searching prevents old results from reopening the list', async () => {
	const Harness = CreateHarness();
	Harness.Type('Old'); Harness.RunTimers(); Harness.Type('');
	await Harness.Respond(0, [Article('Old result')]);
	Assert.equal(Harness.Elements['article-results-list'].style.display, 'none');
	Assert.equal(Harness.Elements['article-results-list'].children.length, 0);
});

Test('site changes immediately clear the selected target and disable dependent controls', async () => {
	const { Elements, ...Harness } = CreateHarness();
	Elements.InputCanonicalArticleId.value = 'old-id'; Elements.SelectedArticleTitle.value = 'Old title';
	Harness.Type('Old'); Harness.RunTimers(); Harness.ChangeSite('2');
	Assert.equal(Elements.InputCanonicalArticleId.value, '');
	Assert.equal(Elements.SelectedArticleTitle.value, '');
	Assert.equal(Elements['selected-article-display'].style.display, 'none');
	Assert.equal(Elements['article-search-input'].value, '');
	Assert.equal(Elements.InputCulture.value, '');
	Assert.equal(Elements.InputCulture.disabled, true);
	Assert.equal(Elements['article-search-input'].disabled, true);
	Assert.equal(Harness.Requests[0].Options.signal.aborted, true);
	Harness.Type('Unavailable'); Harness.RunTimers();
	Assert.equal(Harness.Requests.length, 2);
	await Harness.Respond(0, [Article()]);
	Assert.equal(Elements['article-results-list'].style.display, 'none');
	await Harness.Respond(1, ['ja', 'en']);
	Assert.equal(Elements.InputCulture.value, 'ja');
	Assert.equal(Elements.InputCulture.disabled, false);
	Assert.equal(Elements['article-search-input'].disabled, false);
});

Test('returning to an earlier site cannot revive its earlier culture request', async () => {
	const Harness = CreateHarness();
	Harness.ChangeSite('2'); Harness.ChangeSite('3'); Harness.ChangeSite('2');
	await Harness.Respond(0, ['stale']);
	Assert.equal(Harness.Elements.InputCulture.value, '');
	Assert.equal(Harness.Elements.InputCulture.disabled, true);
	await Harness.Respond(2, ['ja']); await Harness.Fail(1);
	Assert.equal(Harness.Elements.InputCulture.value, 'ja');
	Assert.equal(Harness.Elements['retry-cultures'].style.display, 'none');
	Assert.equal(Harness.Elements['article-picker-status'].textContent, '');
});

Test('culture failures keep search disabled until an explicit retry succeeds', async () => {
	const Harness = CreateHarness();
	Harness.ChangeSite('2'); await Harness.Fail(0);
	Assert.equal(Harness.Elements['retry-cultures'].style.display, 'inline-flex');
	Assert.match(Harness.Elements['article-picker-status'].textContent, /Could not load cultures/);
	Harness.Type('Blocked'); Harness.RunTimers();
	Assert.equal(Harness.Requests.length, 1);
	Harness.Elements['retry-cultures'].click();
	Assert.equal(Harness.Elements['retry-cultures'].style.display, 'none');
	await Harness.Respond(1, ['ja']);
	Harness.Type('Ready'); Harness.RunTimers();
	const Parameters = new URL(Harness.Requests[2].Url, 'http://fixture').searchParams;
	Assert.equal(Parameters.get('siteid'), '2');
	Assert.equal(Parameters.get('culture'), 'ja');
	Assert.equal(Parameters.get('q'), 'Ready');
});

for (const [Name, Reply, Ok] of [['empty', [], true], ['HTTP error', [], false]]) {
	Test(`${Name} culture responses offer retry without reusing the old culture`, async () => {
		const Harness = CreateHarness();
		Harness.ChangeSite('2'); await Harness.Respond(0, Reply, Ok);
		Assert.equal(Harness.Elements.InputCulture.value, '');
		Assert.equal(Harness.Elements['article-search-input'].disabled, true);
		Assert.equal(Harness.Elements['retry-cultures'].style.display, 'inline-flex');
	});
}

Test('newer query results survive a late older response even when the query returns to the same text', async () => {
	const Harness = CreateHarness();
	Harness.Type('First'); Harness.RunTimers();
	Harness.Type('Other'); Harness.RunTimers();
	Harness.Type('First'); Harness.RunTimers();
	await Harness.Respond(2, [Article('Current')]);
	await Harness.Respond(0, [Article('Stale')]); await Harness.Respond(1, [Article('Other')]);
	Assert.equal(Harness.Elements['article-results-list'].children[0].children[0].textContent, 'Current');
});

Test('a culture change clears selection and prevents pending results from appearing', async () => {
	const Harness = CreateHarness();
	Harness.Elements.InputCanonicalArticleId.value = 'old-id';
	Harness.Type('Old'); Harness.RunTimers(); Harness.ChangeCulture('ja');
	Assert.equal(Harness.Elements.InputCanonicalArticleId.value, '');
	await Harness.Respond(0, [Article('English')]);
	Assert.equal(Harness.Elements['article-results-list'].style.display, 'none');
	Harness.Type('New'); Harness.RunTimers(); await Harness.Respond(1, [Article('Japanese')]);
	Assert.equal(Harness.Elements['article-results-list'].children[0].children[0].textContent, 'Japanese');
});

Test('shortening a query cancels its debounce before any fetch starts', () => {
	const Harness = CreateHarness();
	Harness.Type('Wait'); Harness.Type('W'); Harness.RunTimers();
	Assert.equal(Harness.Requests.length, 0);
	Assert.equal(Harness.Elements['search-spinner'].style.display, 'none');
});

Test('an older failure cannot hide the newer search spinner or report an error', async () => {
	const Harness = CreateHarness();
	Harness.Type('Old'); Harness.RunTimers(); Harness.Type('New'); Harness.RunTimers();
	await Harness.Fail(0);
	Assert.equal(Harness.Elements['search-spinner'].style.display, 'block');
	Assert.equal(Harness.Elements['article-picker-status'].textContent, '');
	await Harness.Respond(1, []);
	Assert.equal(Harness.Elements['search-spinner'].style.display, 'none');
	Assert.equal(Harness.Elements['article-results-list'].textContent, 'No matching articles found.');
});

Test('article HTTP errors are actionable and a subsequent search can select a target', async () => {
	const Harness = CreateHarness();
	Harness.Type('Try'); Harness.RunTimers(); await Harness.Respond(0, [], false);
	Assert.match(Harness.Elements['article-picker-status'].textContent, /Type your search again/);
	Assert.equal(Harness.Elements['search-spinner'].style.display, 'none');
	Harness.Type('Retry'); Harness.RunTimers(); await Harness.Respond(1, [Article('<Literal title>')]);
	const Row = Harness.Elements['article-results-list'].children[0];
	Assert.equal(Row.Tag, 'button'); Assert.equal(Row.type, 'button');
	Assert.equal(Row.children[0].textContent, '<Literal title>');
	Row.click();
	Assert.equal(Harness.Elements.InputCanonicalArticleId.value, 'some-guid');
	Assert.equal(Harness.Elements.SelectedArticleTitle.value, '<Literal title>');
	Assert.equal(Harness.Elements['article-search-container'].style.display, 'none');
	Assert.equal(Harness.Elements['selected-article-display'].style.display, 'flex');
	Harness.Elements['clear-article-btn'].click();
	Assert.equal(Harness.Elements.InputCanonicalArticleId.value, '');
	Assert.equal(Harness.Elements['article-search-input'].Focused, true);
});

Test('changing the selected article invalidates a pending search', async () => {
	const Harness = CreateHarness();
	Harness.Type('Pending'); Harness.RunTimers(); Harness.Elements['clear-article-btn'].click();
	await Harness.Respond(0, [Article('Late')]);
	Assert.equal(Harness.Elements['article-results-list'].style.display, 'none');
	Assert.equal(Harness.Elements.InputCanonicalArticleId.value, '');
});
