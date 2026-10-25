// Header search typeahead. Datastar fetches and patches #search-suggestions (see the input's data-on:input);
// this script only handles the combobox behaviour: arrow keys move the highlighted option, Enter opens it,
// Escape closes the panel. Without JavaScript the form submits to /Search as before.
(function ()
{
	const Form = document.querySelector("form.search-form");
	const Input = document.getElementById("header-search-input");
	const Status = document.getElementById("search-suggestions-status");
	if (!Form || !Input)
	{
		return;
	}

	// Combobox semantics only apply once the script can drive them; without JavaScript it is a plain search box.
	Input.setAttribute("role", "combobox");
	Input.setAttribute("aria-autocomplete", "list");
	Input.setAttribute("aria-controls", "search-suggestions-list");
	Input.setAttribute("aria-expanded", "false");

	const MinQueryLength = 2;
	let ActiveIndex = -1;

	/** Returns the current panel; Datastar may replace the element on each patch. */
	function Panel()
	{
		return document.getElementById("search-suggestions");
	}

	/** Returns the selectable suggestion links in the current panel. */
	function Options()
	{
		return Array.from(Panel()?.querySelectorAll("[role='option']") ?? []);
	}

	/** Whether the panel has content for the current input and has not been dismissed. */
	function IsOpen()
	{
		const Current = Panel();
		return !!Current && !Current.hidden && !Form.hasAttribute("data-dismissed") && document.activeElement === Input;
	}

	/** Highlights the option at Index (or none for -1) and points aria-activedescendant at it. */
	function SetActive(Index)
	{
		const All = Options();
		ActiveIndex = Index;
		All.forEach((Option, i) => Option.setAttribute("aria-selected", i === Index ? "true" : "false"));
		const Active = All[Index];
		if (Active)
		{
			Input.setAttribute("aria-activedescendant", Active.id);
			Active.scrollIntoView({ block: "nearest" });
		}
		else
		{
			Input.removeAttribute("aria-activedescendant");
		}
	}

	/** Mirrors the visible state into aria-expanded. */
	function SyncExpanded()
	{
		Input.setAttribute("aria-expanded", IsOpen() && Options().length > 0 ? "true" : "false");
	}

	/** Hides the panel until the reader types again. */
	function Dismiss()
	{
		Form.setAttribute("data-dismissed", "");
		SetActive(-1);
		SyncExpanded();
	}

	Input.addEventListener("input", () =>
	{
		SetActive(-1);
		// Short input clears the panel immediately; a late response for an older query stays hidden.
		if (Input.value.trim().length < MinQueryLength)
		{
			Form.setAttribute("data-dismissed", "");
		}
		else
		{
			Form.removeAttribute("data-dismissed");
		}
		SyncExpanded();
	});

	Input.addEventListener("keydown", Event =>
	{
		const All = Options();
		if (Event.key === "ArrowDown" || Event.key === "ArrowUp")
		{
			if (All.length === 0)
			{
				return;
			}
			Event.preventDefault();
			if (!IsOpen())
			{
				Form.removeAttribute("data-dismissed");
				SyncExpanded();
				return;
			}
			const Step = Event.key === "ArrowDown" ? 1 : -1;
			// Wraps through "no highlight" so the reader can return to plain typing.
			const Next = ActiveIndex + Step;
			SetActive(Next < -1 ? All.length - 1 : Next >= All.length ? -1 : Next);
		}
		else if (Event.key === "Enter" && IsOpen() && All[ActiveIndex])
		{
			Event.preventDefault();
			window.location.assign(All[ActiveIndex].href);
		}
		else if (Event.key === "Escape" && IsOpen() && Panel()?.childElementCount)
		{
			// Leave the native clear-on-Escape for a second press once the panel is closed.
			Event.preventDefault();
			Dismiss();
		}
	});

	// Keep focus in the input when a suggestion is pressed, so the panel stays rendered until the click lands
	// (Safari does not focus links on click, which would otherwise hide the panel first).
	Form.addEventListener("pointerdown", Event =>
	{
		if (Event.target instanceof Element && Event.target.closest("#search-suggestions"))
		{
			Event.preventDefault();
		}
	});

	Form.addEventListener("focusin", SyncExpanded);
	Form.addEventListener("focusout", () => queueMicrotask(SyncExpanded));

	// Each Datastar patch replaces the panel's content: reset the highlight and announce the result count.
	new MutationObserver(Records =>
	{
		// Writing the status text is itself a mutation inside the form; ignore it.
		if (Records.every(Record => Status?.contains(Record.target)))
		{
			return;
		}
		SetActive(-1);
		SyncExpanded();
		const Current = Panel();
		if (!Status || !Current || Current.hidden)
		{
			return;
		}
		const Count = Options().length;
		Status.textContent = Count > 0
			? `${Count} suggestion${Count === 1 ? "" : "s"} available. Use the arrow keys to choose.`
			: (Current.querySelector(".search-suggestions-empty")?.textContent.trim() ?? "");
	}).observe(Form, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
})();
