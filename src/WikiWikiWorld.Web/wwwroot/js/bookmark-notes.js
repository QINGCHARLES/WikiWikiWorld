// Article page: opens the shared bookmark note dialog from a list row's note button (or the post-save prompt) and
// handles its keyboard shortcuts. Saving is a Datastar form post declared in _Bookmark.cshtml.
const Dialog = document.getElementById("article-note-dialog");

if (Dialog) {
	const Form = Dialog.querySelector("form");
	const Text = Dialog.querySelector("textarea");
	const Count = Dialog.querySelector("[data-count-for]");
	const Formatter = new Intl.NumberFormat();
	const Limit = Formatter.format(Text.maxLength);

	/** Shows the character count against the limit. */
	function UpdateCount() {
		Count.textContent = `${Formatter.format(Text.value.length)} / ${Limit}`;
	}

	/** Fills the dialog from a row's note button and opens it modally over the closed menu. */
	function Open(Button) {
		const Notes = Button.dataset.notes ?? "";
		const ListName = Button.dataset.listName ?? "";
		Form.elements.BookmarkId.value = Button.dataset.bookmarkId;
		Dialog.querySelector("#article-note-eyebrow").textContent = `Note · ${ListName}`;
		Dialog.querySelector("#article-note-title").textContent = Notes.trim() ? "Edit your note" : `Why save it to ${ListName}?`;
		Dialog.querySelector("#article-note-visibility").dataset.public = Button.dataset.public;
		Dialog.querySelector("[data-bookmark-note-delete]").hidden = !Notes.trim();
		Text.value = Notes;
		UpdateCount();
		const Menu = document.querySelector("#article-bookmark details");
		if (Menu) Menu.open = false;
		Dialog.showModal();
		Text.focus();
	}

	document.addEventListener("click", Event => {
		if (!(Event.target instanceof Element)) return;
		const Prompt = document.getElementById("article-bookmark-prompt");
		const RowButton = Event.target.closest("[data-bookmark-note]");
		const PromptButton = Event.target.closest("[data-bookmark-note-for]");
		if (RowButton) {
			Open(RowButton);
		} else if (PromptButton) {
			const Target = document.querySelector(`[data-bookmark-note][data-list-id="${CSS.escape(PromptButton.dataset.bookmarkNoteFor)}"]`);
			if (Target) Open(Target);
		}
		// The post-save prompt lasts until the next click anywhere else.
		if (Prompt && !Prompt.contains(Event.target)) Prompt.replaceChildren();
	});

	Text.addEventListener("input", UpdateCount);
	Text.addEventListener("keydown", Event => {
		if (Event.key === "Enter" && (Event.ctrlKey || Event.metaKey)) {
			Event.preventDefault();
			Form.requestSubmit();
		}
	});

	Dialog.querySelector("[data-bookmark-note-delete]").addEventListener("click", () => {
		Text.value = "";
		Form.requestSubmit();
	});

	// Return focus to the menu trigger: the row button that opened the dialog is inside the now-closed menu.
	Dialog.addEventListener("close", () => document.getElementById("article-bookmark-trigger")?.focus());

	// Invoker commands close the dialog natively; this covers browsers without them.
	if (!("command" in HTMLButtonElement.prototype)) {
		Dialog.addEventListener("click", Event => {
			if (Event.target instanceof Element && Event.target.closest("button[command='close']")) Dialog.close();
		});
	}
}
