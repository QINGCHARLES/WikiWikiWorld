// Profile share button: opens the native share sheet where the browser has one and copies the profile link
// otherwise. The button ships hidden, so without JavaScript (or without either API) it never appears.
(function ()
{
	const ShareButton = document.querySelector(".profile-share");
	if (!(ShareButton instanceof HTMLButtonElement))
	{
		return;
	}

	const CanShare = typeof navigator.share === "function";
	const CanCopy = typeof navigator.clipboard?.writeText === "function";
	if (!CanShare && !CanCopy)
	{
		return;
	}

	const Label = ShareButton.querySelector(".profile-share-label");
	const Status = document.querySelector(".profile-share-status");
	const DefaultLabel = Label?.textContent ?? "Share";
	const ShareUrl = ShareButton.dataset.shareUrl ?? location.href;
	let ResetTimer = 0;

	ShareButton.hidden = false;

	ShareButton.addEventListener("click", async function ()
	{
		if (CanShare)
		{
			try
			{
				await navigator.share({ title: ShareButton.dataset.shareTitle ?? document.title, url: ShareUrl });
			}
			catch
			{
				// Dismissing the share sheet rejects the promise; there is nothing to report.
			}
			return;
		}

		try
		{
			await navigator.clipboard.writeText(ShareUrl);
			Announce("Link copied");
		}
		catch
		{
			Announce("Copy failed");
		}
	});

	// Shows the result in the button for two seconds and repeats it in the live region for screen readers.
	function Announce(Message)
	{
		if (Label)
		{
			Label.textContent = Message;
		}
		if (Status)
		{
			Status.textContent = Message;
		}

		clearTimeout(ResetTimer);
		ResetTimer = setTimeout(function ()
		{
			if (Label)
			{
				Label.textContent = DefaultLabel;
			}
			if (Status)
			{
				Status.textContent = "";
			}
		}, 2000);
	}
})();
