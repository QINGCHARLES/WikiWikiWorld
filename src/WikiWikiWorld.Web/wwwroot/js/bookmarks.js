const Browser = document.getElementById("bookmark-browser");
const Status = document.getElementById("bookmark-navigation-status");
let Pending = null;

// Server-rendered canonical state is authoritative, including clamped page numbers.
if (Browser) {
	const Content = document.getElementById("bookmark-content");
	history.replaceState(null, "", Content.dataset.url);

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
}
