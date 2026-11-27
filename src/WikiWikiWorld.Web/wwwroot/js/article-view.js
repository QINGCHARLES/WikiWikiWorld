(function ()
{
	const AnchorResetTimers = new WeakMap();
	let StatusResetTimer = 0;

	document.addEventListener("click", function (Event)
	{
		if (!(Event.target instanceof Element) ||
			Event.defaultPrevented ||
			Event.button !== 0 ||
			Event.ctrlKey ||
			Event.metaKey ||
			Event.shiftKey ||
			Event.altKey)
		{
			return;
		}

		const HeadingAnchor = Event.target.closest("a.heading-anchor");
		if (HeadingAnchor instanceof HTMLAnchorElement)
		{
			HandleHeadingAnchorClick(Event, HeadingAnchor);
			return;
		}

		const ActivationLink = Event.target.closest(".youtube-embed__play");
		if (ActivationLink instanceof HTMLAnchorElement)
		{
			HandleYouTubeActivation(Event, ActivationLink);
		}
	});

	function HandleHeadingAnchorClick(Event, Anchor)
	{
		const Href = Anchor.getAttribute("href");
		if (!Href || !Href.startsWith("#") || Href.length < 2)
		{
			return;
		}

		if (typeof navigator.clipboard?.writeText !== "function")
		{
			return;
		}

		let TargetUrl;
		try
		{
			TargetUrl = new URL(Href, window.location.href);
		}
		catch
		{
			return;
		}

		Event.preventDefault();
		window.history.replaceState(null, "", TargetUrl.hash);

		const Article = Anchor.closest("article");
		const CopiedText = Article?.dataset.copiedText || "Link copied";
		const CopyFailedText = Article?.dataset.copyFailedText || "Copy failed";

		navigator.clipboard.writeText(TargetUrl.href).then(
			function ()
			{
				MarkAnchorCopied(Anchor, CopiedText);
				AnnounceStatus(CopiedText);
			},
			function ()
			{
				AnnounceStatus(CopyFailedText);
			});
	}

	function MarkAnchorCopied(Anchor, CopiedText)
	{
		const ExistingTimer = AnchorResetTimers.get(Anchor);
		if (ExistingTimer)
		{
			clearTimeout(ExistingTimer);
		}

		Anchor.dataset.copied = "true";
		Anchor.setAttribute("title", CopiedText);

		const TimerId = setTimeout(function ()
		{
			delete Anchor.dataset.copied;
			Anchor.removeAttribute("title");
			AnchorResetTimers.delete(Anchor);
		}, 1500);

		AnchorResetTimers.set(Anchor, TimerId);
	}

	function AnnounceStatus(Message)
	{
		const Status = document.querySelector(".heading-anchor-status");
		if (!Status)
		{
			return;
		}

		Status.textContent = Message;
		clearTimeout(StatusResetTimer);
		StatusResetTimer = setTimeout(function ()
		{
			Status.textContent = "";
		}, 2000);
	}

	function HandleYouTubeActivation(Event, ActivationLink)
	{
		const VideoId = ActivationLink.getAttribute("data-youtube-video-id");
		if (!VideoId || !/^[A-Za-z0-9_-]{11}$/.test(VideoId))
		{
			return;
		}

		const StartSecondsText = ActivationLink.getAttribute("data-youtube-start-seconds");
		const StartSeconds = StartSecondsText && /^\d+$/.test(StartSecondsText)
			? Number.parseInt(StartSecondsText, 10)
			: 0;
		if (!Number.isSafeInteger(StartSeconds) || StartSeconds < 0)
		{
			return;
		}

		Event.preventDefault();

		const TitleElement = ActivationLink.querySelector(".youtube-embed__title");
		const VideoTitle = TitleElement?.textContent?.trim() || "YouTube video";
		const Iframe = document.createElement("iframe");
		const StartParameter = StartSeconds > 0 ? `&start=${StartSeconds}` : "";

		Iframe.className = "youtube-embed__frame";
		Iframe.width = "480";
		Iframe.height = "270";
		Iframe.title = ActivationLink.dataset.frameTitle || `YouTube video: ${VideoTitle}`;
		Iframe.loading = "lazy";
		Iframe.referrerPolicy = "strict-origin-when-cross-origin";
		Iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
		Iframe.setAttribute("allowfullscreen", "");
		Iframe.src = `https://www.youtube-nocookie.com/embed/${VideoId}?autoplay=1${StartParameter}`;
		Iframe.addEventListener("load", function ()
		{
			Iframe.focus();
		}, { once: true });

		ActivationLink.replaceWith(Iframe);
	}
})();
