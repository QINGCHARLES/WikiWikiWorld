// The feed stays still while new changes are announced. Navigation remains native.
(() => {
    const Feed = document.querySelector('.firehose');
    if (!Feed) return;
    const Toggle = document.getElementById('fh-live');
    const Status = Feed.querySelector('.fh-update-status');
    const NewChanges = Feed.querySelector('.fh-new');
    // Translated text from the page (data-ui-text); counted phrases carry one pattern per CLDR plural category.
    const Text = JSON.parse(Feed.dataset.uiText || '{}');
    const Plurals = new Intl.PluralRules(document.documentElement.lang || undefined);
    const Fill = (Pattern, Value) => Pattern.replace('{0}', Value);
    const Counted = (Forms, Count) => Fill(Forms[Plurals.select(Count)] ?? Forms.other, Count);
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
            LastCheck = new Date().toLocaleTimeString(document.documentElement.lang || [], { hour: '2-digit', minute: '2-digit' });
            if (!Toggle.checked) return;
            Status.textContent = Fill(Text.checked, LastCheck);
            NewChanges.hidden = Result.count === 0;
            NewChanges.textContent = Counted(Text.newChanges, Result.count);
        } catch {
            if (Toggle.checked) Status.textContent = LastCheck ? Fill(Text.failedSince, LastCheck) : Text.failed;
        } finally {
            Pending = false;
        }
    }
    Toggle.addEventListener('change', () => {
        Status.hidden = false;
        Status.textContent = Toggle.checked ? Text.checking : Text.paused;
        CheckUpdates();
    });
    document.addEventListener('visibilitychange', CheckUpdates);
    window.setInterval(CheckUpdates, 30000);
})();
