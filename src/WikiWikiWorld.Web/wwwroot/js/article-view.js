document.addEventListener("click", function (Event) {
	if (!(Event.target instanceof Element))
	{
		return;
	}

	const ActivationLink = Event.target.closest(".youtube-embed__play");
	if (!(ActivationLink instanceof HTMLAnchorElement) ||
		Event.defaultPrevented ||
		Event.ctrlKey ||
		Event.metaKey ||
		Event.shiftKey ||
		Event.altKey)
	{
		return;
	}

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
	Iframe.title = `YouTube video: ${VideoTitle}`;
	Iframe.loading = "lazy";
	Iframe.referrerPolicy = "strict-origin-when-cross-origin";
	Iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share";
	Iframe.setAttribute("allowfullscreen", "");
	Iframe.src = `https://www.youtube-nocookie.com/embed/${VideoId}?autoplay=1${StartParameter}`;
	Iframe.addEventListener("load", function () {
		Iframe.focus();
	}, { once: true });

	ActivationLink.replaceWith(Iframe);
});
