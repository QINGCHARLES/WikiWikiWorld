// Escape closes an open dropdown (account, Edit or Bookmark) and returns focus to its
// trigger. Clicking outside is handled in CSS by a full-screen layer on the open trigger.
document.addEventListener("keydown", function (Event) {
	if (Event.key !== "Escape" || Event.defaultPrevented)
	{
		return;
	}

	const OpenMenu = document.querySelector("details.dropdown-menu[open]");
	if (!(OpenMenu instanceof HTMLDetailsElement))
	{
		return;
	}

	OpenMenu.open = false;
	OpenMenu.querySelector("summary")?.focus();
});

