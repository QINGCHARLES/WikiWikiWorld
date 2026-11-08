const Browser = document.getElementById("bookmark-browser");
const Status = document.getElementById("bookmark-navigation-status");
let Pending = null;

// Server-rendered canonical state is authoritative, including clamped page numbers.
if (Browser) {
	const Content = document.getElementById("bookmark-content");
	// Keep a fragment such as #bookmark-12, which a note save redirects to so the edited row stays in view.
	history.replaceState(null, "", Content.dataset.url + location.hash);

	/** Fetches a Razor fragment through Datastar; only a matching successful patch commits the URL. */
	function Navigate(Url, FromHistory = false) {
		Pending?.controller.abort();
		const Request = { id: crypto.randomUUID(), controller: new AbortController() };
		Pending = Request;
		Status.textContent = "Loading bookmarks…";
		Browser.setAttribute("aria-busy", "true");

		/** Reports a failed navigation without changing the current list or URL. */
		function Fail() {
			if (Pending !== Request) return;
			Pending = null;
			Browser.removeAttribute("aria-busy");
			// Back/Forward already changed the URL. Load it normally so it cannot show stale private results.
			if (FromHistory) {
				location.reload();
				return;
			}
			Status.textContent = "Could not load this list. Your current page is unchanged. Try the link again.";
		}

		/** Commits history only when the server's matching fragment is in the document. */
		function Complete() {
			if (Pending !== Request) return;
			const Updated = document.getElementById("bookmark-content");
			if (Updated?.dataset.navigationId !== Request.id) {
				Fail();
				return;
			}
			Pending = null;
			Browser.removeAttribute("aria-busy");
			const Canonical = Updated.dataset.url;
			if (FromHistory) history.replaceState(null, "", Canonical);
			else if (location.pathname + location.search !== Canonical) history.pushState(null, "", Canonical);
			Status.textContent = document.getElementById("bookmark-range")?.textContent.trim() ?? "List loaded.";
			document.getElementById("bookmark-title")?.focus({ preventScroll: true });
			Updated.scrollIntoView({ block: "start", behavior: "instant" });
		}

		Browser.dispatchEvent(new CustomEvent("bookmark-load", {
			detail: { url: Url, id: Request.id, controller: Request.controller, complete: Complete, fail: Fail }
		}));
	}

	Browser.addEventListener("click", Event => {
		const Link = Event.target.closest("a[data-bookmark-navigation]");
		if (!Link || Event.button !== 0 || Event.ctrlKey || Event.metaKey || Event.shiftKey || Event.altKey) return;
		Event.preventDefault();
		Navigate(Link.href);
	});

	// Invoker commands open and close the new-list dialog natively; this covers browsers without them.
	if (!("command" in HTMLButtonElement.prototype)) {
		Browser.addEventListener("click", Event => {
			const Invoker = Event.target.closest("button[commandfor][command]");
			const Dialog = document.getElementById(Invoker?.getAttribute("commandfor"));
			if (Invoker?.getAttribute("command") === "show-modal") Dialog?.showModal();
			else if (Invoker?.getAttribute("command") === "close") Dialog?.close();
		});
	}

	// A rejected name re-renders the dialog with the open attribute; make it modal so focus and Esc behave.
	const OpenDialog = document.querySelector("#bookmark-new-list[open]");
	if (OpenDialog) {
		OpenDialog.close();
		OpenDialog.showModal();
	}

	window.addEventListener("popstate", () => Navigate(location.href, true));

	const Formatter = new Intl.NumberFormat();

	/** Shows a textarea's length against its maxlength in the matching counter. */
	function UpdateCount(Text) {
		const Counter = Browser.querySelector(`[data-count-for="${CSS.escape(Text.id)}"]`);
		if (Counter) Counter.textContent = `${Formatter.format(Text.value.length)} / ${Formatter.format(Text.maxLength)}`;
	}

	/** Shows or hides a row's inline note editor; cancelling restores the saved text. */
	function SetNoteEditor(Editor, Open) {
		const Toggle = Browser.querySelector(`[data-bookmark-note-edit][aria-controls="${CSS.escape(Editor.id)}"]`);
		const Text = Editor.querySelector("textarea");
		Editor.hidden = !Open;
		Toggle?.setAttribute("aria-expanded", String(Open));
		if (Open) {
			UpdateCount(Text);
			Text.focus();
			Text.setSelectionRange(Text.value.length, Text.value.length);
		} else {
			Text.value = Text.defaultValue;
			Toggle?.focus();
		}
	}

	/** Switches the introduction editor between writing and a server-rendered preview of the draft. */
	async function SetIntroTab(Dialog, Preview) {
		const Text = Dialog.querySelector("textarea");
		const Panel = Dialog.querySelector("#bookmark-intro-preview");
		for (const Tab of Dialog.querySelectorAll("[data-bookmark-intro-tab]")) {
			Tab.setAttribute("aria-selected", String((Tab.dataset.bookmarkIntroTab === "preview") === Preview));
		}
		Text.hidden = Preview;
		Panel.hidden = !Preview;
		if (!Preview) {
			Text.focus();
			return;
		}
		if (!Text.value.trim()) {
			Panel.innerHTML = "<p>Nothing to preview yet.</p>";
			return;
		}
		Panel.textContent = "Loading preview…";
		try {
			const Url = new URL(Text.form.action, location.href);
			Url.searchParams.set("handler", "Preview");
			const Response = await fetch(Url, { method: "POST", body: new FormData(Text.form) });
			if (!Response.ok) throw new Error(String(Response.status));
			// Sanitized by the server's user-content renderer, the same output the saved introduction gets.
			Panel.innerHTML = await Response.text() || "<p>Nothing to preview yet.</p>";
		} catch {
			Panel.textContent = "Could not load the preview. Your text is unchanged.";
		}
	}

	Browser.addEventListener("click", Event => {
		if (!(Event.target instanceof Element)) return;
		const Edit = Event.target.closest("[data-bookmark-note-edit]");
		if (Edit) {
			const Editor = document.getElementById(Edit.getAttribute("aria-controls"));
			if (Editor) SetNoteEditor(Editor, Editor.hidden);
			return;
		}
		const Cancel = Event.target.closest("[data-bookmark-note-cancel]");
		if (Cancel) {
			SetNoteEditor(Cancel.closest("form"), false);
			return;
		}
		const Expand = Event.target.closest("[data-bookmark-note-expand]");
		if (Expand) {
			const Note = document.getElementById(Expand.getAttribute("aria-controls"));
			const Expanded = Expand.getAttribute("aria-expanded") !== "true";
			Note.dataset.clamped = String(!Expanded);
			Expand.setAttribute("aria-expanded", String(Expanded));
			Expand.textContent = Expanded ? "Show less" : "Read more";
			return;
		}
		const IntroTab = Event.target.closest("[data-bookmark-intro-tab]");
		if (IntroTab) {
			SetIntroTab(IntroTab.closest("dialog"), IntroTab.dataset.bookmarkIntroTab === "preview");
			return;
		}
		const RemoveIntro = Event.target.closest("[data-bookmark-intro-remove]");
		if (RemoveIntro) {
			const Form = RemoveIntro.closest("form");
			Form.elements.description.value = "";
			Form.requestSubmit();
			return;
		}
		// Opening the introduction editor always starts on Write with an up-to-date count.
		const OpenIntro = Event.target.closest("[commandfor='bookmark-intro-dialog'][command='show-modal']");
		if (OpenIntro) {
			const Dialog = document.getElementById("bookmark-intro-dialog");
			Dialog.querySelector("[role='tablist']").hidden = false;
			SetIntroTab(Dialog, false);
			UpdateCount(Dialog.querySelector("textarea"));
		}
	});

	Browser.addEventListener("input", Event => {
		if (Event.target instanceof HTMLTextAreaElement) UpdateCount(Event.target);
	});

	Browser.addEventListener("keydown", Event => {
		if (!(Event.target instanceof HTMLTextAreaElement)) return;
		if (Event.key === "Enter" && (Event.ctrlKey || Event.metaKey)) {
			Event.preventDefault();
			Event.target.form.requestSubmit();
		} else if (Event.key === "Escape" && Event.target.closest(".bookmark-note-editor")) {
			Event.preventDefault();
			SetNoteEditor(Event.target.form, false);
		}
	});
}
