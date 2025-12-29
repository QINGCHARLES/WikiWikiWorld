document.addEventListener("DOMContentLoaded", function () {
    const videoContainers = document.querySelectorAll('.video-container');

    videoContainers.forEach(container => {
        container.addEventListener('click', function () {
            const videoId = container.getAttribute('data-id');

            // Preserve the container's height to prevent layout collapse
            const containerHeight = container.clientHeight;
            container.style.height = containerHeight + 'px';

            // Get the thumbnail image and play button for removal later.
            const thumbnail = container.querySelector('img');
            const playButton = container.querySelector('.play-button');

            // Create the YouTube iframe.
            const iframe = document.createElement('iframe');
            iframe.src = `https://www.youtube.com/embed/${videoId}?autoplay=1&rel=0`;
            iframe.setAttribute('frameborder', '0');
            iframe.setAttribute('allowfullscreen', '');
            iframe.setAttribute('allow', 'accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture');

            // Set the iframe to fill the container and overlay the thumbnail.
            iframe.style.width = '100%';
            iframe.style.height = '100%';
            iframe.style.position = 'absolute';
            iframe.style.top = '0';
            iframe.style.left = '0';

            // Append the iframe to the container (it will overlay the thumbnail).
            container.appendChild(iframe);

            let removed = false;
            // Remove the thumbnail and play button once the iframe fully loads.
            iframe.onload = function () {
                if (!removed) {
                    if (thumbnail) thumbnail.remove();
                    if (playButton) playButton.remove();
                    removed = true;
                }
            };

            // Fallback removal after 3 seconds in case onload doesn't fire.
            setTimeout(function () {
                if (!removed) {
                    if (thumbnail) thumbnail.remove();
                    if (playButton) playButton.remove();
                }
            }, 3000);
        });
    });
});
