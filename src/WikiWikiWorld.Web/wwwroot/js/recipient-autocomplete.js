// Recipient autocomplete for the private-message compose page (Pages/Profile/NewMessage.cshtml). It suggests account
// names for the final comma-separated token only; the field stays an ordinary text input, so the form works without it.
// Covered by tests/browser/recipient-autocomplete.test.cjs.
document.addEventListener("DOMContentLoaded", function ()
{
	const Input = document.getElementById("recipient-input");
	const List = document.getElementById("autocomplete-list");
	if (!Input || !List) return;

	// The page supplies the site-scoped avatar folder; without it suggestions fall back to initials.
	const AvatarBasePath = Input.getAttribute("data-avatar-base");
	let ActiveIndex = -1;
	let DebounceTimer = 0;
	let CurrentRequest = null;
	Input.setAttribute("role", "combobox");
	Input.setAttribute("aria-autocomplete", "list");
	Input.setAttribute("aria-controls", "autocomplete-list");
	Input.setAttribute("aria-expanded", "false");
	List.setAttribute("role", "listbox");
	List.setAttribute("aria-label", "Recipient suggestions");

	/** Cancel pending work and invalidate even responses whose callbacks were queued before cancellation. */
	function Dismiss()
	{
		clearTimeout(DebounceTimer);
		CurrentRequest?.Controller.abort();
		CurrentRequest = null;
		List.replaceChildren();
		List.classList.remove("show");
		ActiveIndex = -1;
		Input.removeAttribute("aria-activedescendant");
		Input.setAttribute("aria-expanded", "false");
	}

	/** Keep keyboard selection within the currently rendered options, leaving DOM focus in the input. */
	function SetActive(Index)
	{
		const Options = Array.from(List.children);
		if (Options.length === 0) return;
		ActiveIndex = (Index + Options.length) % Options.length;
		Options.forEach((Option, i) =>
		{
			Option.classList.toggle("active", i === ActiveIndex);
			Option.setAttribute("aria-selected", String(i === ActiveIndex));
		});
		Input.setAttribute("aria-activedescendant", Options[ActiveIndex].id);
		Options[ActiveIndex].scrollIntoView({ block: "nearest" });
	}

	Dismiss();
	Input.addEventListener("input", () =>
	{
		Dismiss();
		const Snapshot = Input.value;
		const TokenStart = Snapshot.lastIndexOf(",") + 1;
		const Query = Snapshot.slice(TokenStart).trim();
		if (!Query) return;
		const Request = { Controller: new AbortController() };
		CurrentRequest = Request;
		DebounceTimer = setTimeout(async () =>
		{
			try
			{
				const Response = await fetch(`?handler=SearchUsers&term=${encodeURIComponent(Query)}`, { signal: Request.Controller.signal });
				if (!Response.ok) throw new Error("Recipient search failed.");
				const Users = await Response.json();
				if (CurrentRequest !== Request || Input.value !== Snapshot || document.activeElement !== Input) return;

				List.replaceChildren();
				Users.forEach((User, Index) =>
				{
					const Item = document.createElement("div");
					Item.className = "autocomplete-item";
					Item.id = `recipient-option-${Index}`;
					Item.setAttribute("role", "option");
					Item.setAttribute("aria-selected", "false");
					const Avatar = document.createElement("span");
					Avatar.className = "avatar";
					Avatar.setAttribute("aria-hidden", "true");
					if (User.profilePicGuid && AvatarBasePath)
					{
						const Image = document.createElement("img");
						Image.src = `${AvatarBasePath}${User.profilePicGuid}.png`;
						Image.alt = "";
						Image.width = 32;
						Image.height = 32;
						Avatar.appendChild(Image);
					}
					else
					{
						const Initial = document.createElement("span");
						Initial.className = "avatar-initials";
						Initial.textContent = User.userName.charAt(0).toUpperCase();
						Avatar.appendChild(Initial);
					}
					const Name = document.createElement("span");
					Name.textContent = User.userName;
					Item.append(Avatar, Name);
					Item.addEventListener("click", () =>
					{
						Input.value = Snapshot.slice(0, TokenStart) + (TokenStart > 0 ? " " : "") + User.userName;
						Dismiss();
					});
					List.appendChild(Item);
				});
				List.classList.toggle("show", Users.length > 0);
				Input.setAttribute("aria-expanded", String(Users.length > 0));
			}
			catch (Failure)
			{
				if (CurrentRequest === Request && Failure.name !== "AbortError")
				{
					console.error("Error fetching users:", Failure);
				}
			}
		}, 300);
	});

	Input.addEventListener("keydown", Event =>
	{
		if (Event.isComposing) return;
		if (Event.key === "Escape")
		{
			if (CurrentRequest || List.classList.contains("show")) Event.preventDefault();
			Dismiss();
			return;
		}
		const Options = List.children;
		if (Options.length === 0 || !List.classList.contains("show")) return;
		if (Event.key === "ArrowDown" || Event.key === "ArrowUp")
		{
			Event.preventDefault();
			SetActive(Event.key === "ArrowDown" ? ActiveIndex + 1 : ActiveIndex < 0 ? Options.length - 1 : ActiveIndex - 1);
		}
		else if (Event.key === "Enter" && Options[ActiveIndex])
		{
			Event.preventDefault();
			Options[ActiveIndex].click();
		}
	});

	// Preserve input focus until the option's click selects it; Tab/blur still dismisses normally.
	List.addEventListener("pointerdown", Event => { if (Event.button === 0) Event.preventDefault(); });
	Input.addEventListener("blur", Dismiss);
	document.addEventListener("click", Event =>
	{
		if (Event.target !== Input && !List.contains(Event.target)) Dismiss();
	});
});
