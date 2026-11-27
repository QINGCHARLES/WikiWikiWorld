const Assert = require("node:assert/strict");
const { readFileSync: ReadFile } = require("node:fs");
const Path = require("node:path");
const { test: Test } = require("node:test");
const Vm = require("node:vm");

const Script = ReadFile(Path.join(__dirname, "../../src/WikiWikiWorld.Web/wwwroot/js/article-view.js"), "utf8");

/** Minimal DOM harness exercising the actual article-view.js click delegation and clipboard/history handling. */
function CreateHarness({
	Href = "#history",
	PageUrl = "https://en.magazedia.com.localhost:7126/wired",
	ClipboardAvailable = true,
	ClipboardShouldReject = false,
	CopiedText = "Link copied",
	CopyFailedText = "Copy failed"
} = {})
{
	class DomElement
	{
		constructor(Tag)
		{
			this.tagName = Tag.toUpperCase();
			this.parentElement = null;
			this.dataset = {};
			this.textContent = "";
			this.Attributes = new Map();
			this.Classes = new Set();
		}

		setAttribute(Name, Value)
		{
			this.Attributes.set(Name, String(Value));
		}

		getAttribute(Name)
		{
			return this.Attributes.get(Name) ?? null;
		}

		removeAttribute(Name)
		{
			this.Attributes.delete(Name);
		}

		closest(Selector)
		{
			let Current = this;
			while (Current)
			{
				if (Matches(Current, Selector))
				{
					return Current;
				}
				Current = Current.parentElement;
			}
			return null;
		}
	}

	class HTMLAnchorElement extends DomElement
	{
		constructor()
		{
			super("a");
		}
	}

	function Matches(Node, Selector)
	{
		if (Selector === "article") return Node.tagName === "ARTICLE";
		if (Selector === "a.heading-anchor") return Node.tagName === "A" && Node.Classes.has("heading-anchor");
		if (Selector === ".youtube-embed__play") return Node.Classes.has("youtube-embed__play");
		if (Selector === ".heading-anchor-status") return Node.Classes.has("heading-anchor-status");
		return false;
	}

	const Article = new DomElement("article");
	Article.dataset.copiedText = CopiedText;
	Article.dataset.copyFailedText = CopyFailedText;

	const Heading = new DomElement("h2");
	Heading.parentElement = Article;

	const Anchor = new HTMLAnchorElement();
	Anchor.Classes.add("heading-anchor");
	Anchor.setAttribute("href", Href);
	Anchor.parentElement = Heading;

	const Status = new DomElement("span");
	Status.Classes.add("heading-anchor-status");

	const ClickHandlers = [];
	const Document = {
		addEventListener(Name, Handler)
		{
			if (Name === "click") ClickHandlers.push(Handler);
		},
		querySelector(Selector)
		{
			return Matches(Status, Selector) ? Status : null;
		},
		createElement(Tag)
		{
			return new DomElement(Tag);
		}
	};

	const CopiedUrls = [];
	const ReplacedUrls = [];
	const Timers = new Map();
	let NextTimerId = 0;

	const Navigator = ClipboardAvailable
		? {
			clipboard: {
				writeText(Value)
				{
					CopiedUrls.push(Value);
					return ClipboardShouldReject
						? Promise.reject(new Error("Clipboard denied"))
						: Promise.resolve();
				}
			}
		}
		: {};

	const Window = {
		location: { href: PageUrl },
		history: {
			replaceState(_State, _Title, Url)
			{
				ReplacedUrls.push(Url);
			}
		}
	};

	Vm.runInNewContext(Script, {
		document: Document,
		window: Window,
		navigator: Navigator,
		Element: DomElement,
		HTMLAnchorElement,
		URL,
		Number,
		WeakMap,
		setTimeout(Callback, DelayMs)
		{
			const Id = ++NextTimerId;
			Timers.set(Id, { Callback, DelayMs });
			return Id;
		},
		clearTimeout(Id)
		{
			Timers.delete(Id);
		}
	});

	function Click(Overrides = {})
	{
		let Prevented = false;
		const ClickEvent = {
			target: Anchor,
			button: 0,
			ctrlKey: false,
			metaKey: false,
			shiftKey: false,
			altKey: false,
			get defaultPrevented()
			{
				return Prevented;
			},
			preventDefault()
			{
				Prevented = true;
			},
			...Overrides
		};

		for (const Handler of ClickHandlers)
		{
			Handler(ClickEvent);
		}

		return ClickEvent;
	}

	function RunAllTimers()
	{
		for (const [Id, Entry] of [...Timers.entries()])
		{
			Timers.delete(Id);
			Entry.Callback();
		}
	}

	return { Anchor, Status, CopiedUrls, ReplacedUrls, Timers, Click, RunAllTimers };
}

Test("copies resolved section URL, updates hash, and resets copied state after timeout", async () =>
{
	const Harness = CreateHarness({
		Href: "#歴史",
		PageUrl: "https://ja.magazedia.com.localhost:7126/popeye",
		CopiedText: "リンクをコピーしました"
	});

	const Event = Harness.Click();
	Assert.equal(Event.defaultPrevented, true);
	Assert.deepEqual(Harness.ReplacedUrls, ["#%E6%AD%B4%E5%8F%B2"]);
	Assert.deepEqual(Harness.CopiedUrls, ["https://ja.magazedia.com.localhost:7126/popeye#%E6%AD%B4%E5%8F%B2"]);

	await Promise.resolve();
	Assert.equal(Harness.Anchor.dataset.copied, "true");
	Assert.equal(Harness.Anchor.getAttribute("title"), "リンクをコピーしました");
	Assert.equal(Harness.Status.textContent, "リンクをコピーしました");

	Harness.RunAllTimers();
	Assert.equal(Harness.Anchor.dataset.copied, undefined);
	Assert.equal(Harness.Anchor.getAttribute("title"), null);
	Assert.equal(Harness.Status.textContent, "");
});

Test("leaves modified and non-primary clicks to native browser navigation", () =>
{
	const Harness = CreateHarness();
	for (const Override of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }])
	{
		const Event = Harness.Click(Override);
		Assert.equal(Event.defaultPrevented, false);
	}
	Assert.equal(Harness.CopiedUrls.length, 0);
	Assert.equal(Harness.ReplacedUrls.length, 0);
});

Test("falls back to native anchor navigation when Clipboard API is unavailable", () =>
{
	const Harness = CreateHarness({ ClipboardAvailable: false });
	const Event = Harness.Click();
	Assert.equal(Event.defaultPrevented, false);
	Assert.equal(Harness.ReplacedUrls.length, 0);
});

Test("updates URL hash and announces failure without copied badge when clipboard write rejects", async () =>
{
	const Harness = CreateHarness({ ClipboardShouldReject: true });
	const Event = Harness.Click();
	Assert.equal(Event.defaultPrevented, true);
	Assert.deepEqual(Harness.ReplacedUrls, ["#history"]);

	await Promise.resolve();
	await Promise.resolve();
	Assert.equal(Harness.Anchor.dataset.copied, undefined);
	Assert.equal(Harness.Status.textContent, "Copy failed");
});
