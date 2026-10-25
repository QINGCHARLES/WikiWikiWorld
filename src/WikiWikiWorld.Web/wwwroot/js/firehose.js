// The feed stays still while new changes are announced. Navigation remains native.
(() => {
    const Feed = document.querySelector('.firehose');
    if (!Feed) return;
    const Toggle = document.getElementById('fh-live');
    const Status = Feed.querySelector('.fh-update-status');
    const NewChanges = Feed.querySelector('.fh-new');
    let Pending = false;
    let LastCheck = null;
    Toggle.closest('label').hidden = false;

    async function CheckUpdates() {
        if (!Toggle.checked || document.hidden || Pending) return;
        Pending = true;
        Status.hidden = false;
        try {
            const Response = await fetch(Feed.dataset.updatesUrl, {
                cache: 'no-store', signal: AbortSignal.timeout(10000)
            });
            if (!Response.ok) throw new Error('Update check failed');
            const Result = await Response.json();
            LastCheck = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            if (!Toggle.checked) return;
            Status.textContent = `Checked ${LastCheck} · checks every 30 seconds`;
            NewChanges.hidden = Result.count === 0;
            NewChanges.textContent = `${Result.count} new ${Result.count === 1 ? 'change' : 'changes'} — show latest ↑`;
        } catch {
            if (Toggle.checked) Status.textContent = `Couldn't check for changes.${LastCheck ? ` Last checked ${LastCheck}.` : ''} Retrying in 30 seconds; you can also refresh the page.`;
        } finally {
            Pending = false;
        }
    }
    Toggle.addEventListener('change', () => {
        Status.hidden = false;
        Status.textContent = Toggle.checked ? 'Checking for new changes…' : 'Auto-update paused.';
        CheckUpdates();
    });
    document.addEventListener('visibilitychange', CheckUpdates);
    window.setInterval(CheckUpdates, 30000);
})();
