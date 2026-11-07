const Assert = require('node:assert/strict');
const { readFileSync: ReadFile } = require('node:fs');
const Path = require('node:path');
const { test: Test } = require('node:test');
const Vm = require('node:vm');

const Page = ReadFile(Path.join(__dirname, '../../src/WikiWikiWorld.Web/Pages/Profile/NewMessage.cshtml'), 'utf8');
const Script = Page.match(/<script>([\s\S]*?)<\/script>/)[1];

/** Minimal event-driven DOM for deterministic query ordering; Chromium checks cover native pointer/focus behavior. */
function CreateHarness() {
	class Target {
		Listeners = new Map();
		addEventListener(Name, Handler) {
			if (!this.Listeners.has(Name)) this.Listeners.set(Name, []);
			this.Listeners.get(Name).push(Handler);
		}
		dispatchEvent(Event) {
			Object.defineProperty(Event, 'target', { value: this, configurable: true });
			for (const Handler of this.Listeners.get(Event.type) ?? []) Handler.call(this, Event);
			return !Event.defaultPrevented;
		}
	}
	class Element extends Target {
		constructor(Tag = 'div') {
			super();
			this.tagName = Tag;
			this.children = [];
			this.value = '';
			this.Attributes = new Map();
			this.Classes = new Set();
			this.classList = {
				add: (...Names) => Names.forEach(Name => this.Classes.add(Name)),
				remove: (...Names) => Names.forEach(Name => this.Classes.delete(Name)),
				contains: Name => this.Classes.has(Name),
				toggle: (Name, Enabled) => Enabled ? this.Classes.add(Name) : this.Classes.delete(Name)
			};
		}
		set className(Value) { this.Classes = new Set(Value.split(' ')); }
		get firstChild() { return this.children[0]; }
		appendChild(Child) { this.children.push(Child); Child.parentElement = this; return Child; }
		append(...Children) { Children.forEach(Child => this.appendChild(Child)); }
		replaceChildren(...Children) { this.children = []; this.append(...Children); }
		removeChild(Child) { this.children.splice(this.children.indexOf(Child), 1); }
		getElementsByTagName(Tag) { return this.children.flatMap(Child => [ ...(Child.tagName === Tag ? [Child] : []), ...Child.getElementsByTagName(Tag) ]); }
		setAttribute(Name, Value) { this.Attributes.set(Name, String(Value)); }
		getAttribute(Name) { return this.Attributes.get(Name) ?? null; }
		removeAttribute(Name) { this.Attributes.delete(Name); }
		contains(Child) { return Child === this || this.children.some(Item => Item.contains(Child)); }
		focus() { Document.activeElement = this; }
		scrollIntoView() {}
		click() { this.dispatchEvent(new Event('click', { cancelable: true })); }
	}
	const Input = new Element('input');
	const List = new Element();
	const Document = new Target();
	Document.activeElement = Input;
	Document.getElementById = Id => Id === 'recipient-input' ? Input : List;
	Document.createElement = Tag => new Element(Tag);
	const Timers = new Map();
	const Requests = [];
	let TimerId = 0;
	Vm.runInNewContext(Script, {
		document: Document, AbortController, console: { error() {} },
		setTimeout: Callback => { Timers.set(++TimerId, Callback); return TimerId; },
		clearTimeout: Id => Timers.delete(Id),
		// Deliberately resolve even aborted requests: cancellation cannot replace checking response ownership.
		fetch: Url => new Promise((Resolve, Reject) => Requests.push({ Url, Resolve, Reject }))
	});
	Document.dispatchEvent(new Event('DOMContentLoaded'));
	return {
		Input, List, Requests,
		Type(Value) { Input.value = Value; Input.dispatchEvent(new Event('input')); },
		RunTimers() { const Pending = [...Timers.values()]; Timers.clear(); Pending.forEach(Callback => Callback()); },
		Key(Key) {
			const KeyEvent = new Event('keydown', { cancelable: true });
			Object.defineProperties(KeyEvent, { key: { value: Key }, keyCode: { value: { ArrowDown: 40, ArrowUp: 38, Enter: 13, Escape: 27 }[Key] } });
			Input.dispatchEvent(KeyEvent);
			return KeyEvent;
		},
		async Respond(Index, Names) {
			Requests[Index].Resolve({ ok: true, json: async () => Names.map(Name => ({ userName: Name })) });
			await new Promise(Resolve => setImmediate(Resolve));
		},
		OutsideClick() { Document.dispatchEvent(new Event('click')); },
		Blur() { Document.activeElement = null; Input.dispatchEvent(new Event('blur')); },
		async Fail(Index, Response = null) {
			if (Response) Requests[Index].Resolve(Response);
			else Requests[Index].Reject(new Error('Network unavailable'));
			await new Promise(Resolve => setImmediate(Resolve));
		}
	};
}

Test('clearing before the debounce prevents the old search from reopening suggestions', async () => {
	const Harness = CreateHarness();
	Harness.Type('Old');
	Harness.Type('');
	Harness.RunTimers();
	for (let Index = 0; Index < Harness.Requests.length; Index++) await Harness.Respond(Index, ['OldResult']);
	Assert.equal(Harness.List.classList.contains('show'), false);
	Assert.equal(Harness.List.children.length, 0);
});

Test('arrow keys and Enter with no suggestions preserve native form handling', () => {
	const Harness = CreateHarness();
	for (const Key of ['ArrowDown', 'ArrowUp', 'Enter']) {
		Assert.equal(Harness.Key(Key).defaultPrevented, false);
	}
});

Test('clearing after a request starts ignores its eventual response', async () => {
	const Harness = CreateHarness();
	Harness.Type('Old'); Harness.RunTimers();
	Harness.Type('');
	await Harness.Respond(0, ['OldResult']);
	Assert.equal(Harness.List.children.length, 0);
	Assert.equal(Harness.Input.getAttribute('aria-expanded'), 'false');
});

Test('an older response cannot replace newer results', async () => {
	const Harness = CreateHarness();
	Harness.Type('Old'); Harness.RunTimers();
	Harness.Type('New'); Harness.RunTimers();
	await Harness.Respond(1, ['NewResult']);
	await Harness.Respond(0, ['OldResult']);
	Assert.equal(Harness.List.children.length, 1);
	Assert.equal(Harness.List.children[0].children[1].textContent, 'NewResult');
});

Test('returning to the same query cannot revive an earlier request for that query', async () => {
	const Harness = CreateHarness();
	Harness.Type('Same'); Harness.RunTimers();
	Harness.Type('Different');
	Harness.Type('Same'); Harness.RunTimers();
	await Harness.Respond(1, ['CurrentResult']);
	await Harness.Respond(0, ['ObsoleteResult']);
	Assert.equal(Harness.List.children[0].children[1].textContent, 'CurrentResult');
});

for (const Dismissal of ['Escape', 'outside click', 'blur']) {
	Test(`${Dismissal} prevents a pending response from reopening suggestions`, async () => {
		const Harness = CreateHarness();
		Harness.Type('Pending'); Harness.RunTimers();
		if (Dismissal === 'Escape') Harness.Key('Escape');
		else if (Dismissal === 'outside click') Harness.OutsideClick();
		else Harness.Blur();
		await Harness.Respond(0, ['LateResult']);
		Assert.equal(Harness.List.classList.contains('show'), false);
		Assert.equal(Harness.List.children.length, 0);
	});
}

Test('keyboard selection wraps through actual options and Enter selects without submitting', async () => {
	const Harness = CreateHarness();
	Harness.Type('User'); Harness.RunTimers();
	await Harness.Respond(0, ['First', 'Second']);
	Assert.equal(Harness.Key('Enter').defaultPrevented, false);
	Assert.equal(Harness.Key('ArrowUp').defaultPrevented, true);
	Assert.equal(Harness.Input.getAttribute('aria-activedescendant'), 'recipient-option-1');
	Harness.Key('ArrowDown');
	Assert.equal(Harness.Input.getAttribute('aria-activedescendant'), 'recipient-option-0');
	Assert.equal(Harness.List.children[0].getAttribute('aria-selected'), 'true');
	Assert.equal(Harness.Key('Enter').defaultPrevented, true);
	Assert.equal(Harness.Input.value, 'First');
	Assert.equal(Harness.List.children.length, 0);
	Assert.equal(Harness.Key('Enter').defaultPrevented, false);
});

Test('new input clears the previous highlight before replacing results with fewer options', async () => {
	const Harness = CreateHarness();
	Harness.Type('Many'); Harness.RunTimers();
	await Harness.Respond(0, ['First', 'Second']);
	Harness.Key('ArrowUp');
	Harness.Type('One'); Harness.RunTimers();
	await Harness.Respond(1, ['Only']);
	Assert.equal(Harness.Input.getAttribute('aria-activedescendant'), null);
	Assert.equal(Harness.Key('Enter').defaultPrevented, false);
	Harness.Key('ArrowDown'); Harness.Key('Enter');
	Assert.equal(Harness.Input.value, 'Only');
});

Test('failed or empty searches leave typing usable and the next successful search can select a recipient', async () => {
	const Harness = CreateHarness();
	Harness.Type('Failed'); Harness.RunTimers();
	await Harness.Fail(0);
	Harness.Type('Rejected'); Harness.RunTimers();
	await Harness.Fail(1, { ok: false });
	Harness.Type('Empty'); Harness.RunTimers();
	await Harness.Respond(2, []);
	Assert.equal(Harness.List.classList.contains('show'), false);
	Assert.equal(Harness.Key('ArrowDown').defaultPrevented, false);
	Harness.Type('Recovered'); Harness.RunTimers();
	await Harness.Respond(3, ['Found']);
	Harness.List.children[0].click();
	Assert.equal(Harness.Input.value, 'Found');
	Assert.equal(Harness.List.classList.contains('show'), false);
});

Test('dismissing while JSON is still being read prevents a queued body result from reopening the list', async () => {
	const Harness = CreateHarness();
	Harness.Type('Pending'); Harness.RunTimers();
	let CompleteBody;
	Harness.Requests[0].Resolve({ ok: true, json: () => new Promise(Resolve => CompleteBody = Resolve) });
	await new Promise(Resolve => setImmediate(Resolve));
	Harness.Key('Escape');
	CompleteBody([{ userName: 'TooLate' }]);
	await new Promise(Resolve => setImmediate(Resolve));
	Assert.equal(Harness.List.children.length, 0);
});

Test('multi-recipient suggestions search only the final token and preserve selected recipients', async () => {
	const Harness = CreateHarness();
	Harness.Type('First, @Sec'); Harness.RunTimers();
	Assert.equal(Harness.Requests[0].Url, '?handler=SearchUsers&term=%40Sec');
	await Harness.Respond(0, ['Second']);
	Harness.Key('ArrowDown'); Harness.Key('Enter');
	Assert.equal(Harness.Input.value, 'First, Second');
	Harness.Type('First, Second, '); Harness.RunTimers();
	Assert.equal(Harness.Requests.length, 1);
});

Test('changing an earlier recipient invalidates a response even when the final token is unchanged', async () => {
	const Harness = CreateHarness();
	Harness.Type('First, Sec'); Harness.RunTimers();
	Harness.Type('Other, Sec'); Harness.RunTimers();
	await Harness.Respond(0, ['Obsolete']);
	Assert.equal(Harness.List.children.length, 0);
	await Harness.Respond(1, ['Second']);
	Harness.List.children[0].click();
	Assert.equal(Harness.Input.value, 'Other, Second');
});
