export function syncPdfScroll(pane1, pane2) {
    if (!pane1 || !pane2) return;

    // Wait for iframes to load, then sync their internal scroll
    const setupSync = () => {
        const iframe1 = pane1.querySelector('iframe');
        const iframe2 = pane2.querySelector('iframe');
        if (!iframe1 || !iframe2) return;

        let syncing = false;

        const trySync = () => {
            try {
                const doc1 = iframe1.contentDocument || iframe1.contentWindow?.document;
                const doc2 = iframe2.contentDocument || iframe2.contentWindow?.document;
                if (!doc1 || !doc2) return;

                // For PDF viewer embeds, the scrollable element varies.
                // Try the document's scrollingElement or body.
                const getScrollEl = (doc) => doc.scrollingElement || doc.documentElement || doc.body;

                const el1 = getScrollEl(doc1);
                const el2 = getScrollEl(doc2);

                const onScroll1 = () => {
                    if (syncing) return;
                    syncing = true;
                    // Proportional scroll sync (documents may have different heights)
                    const pct = el1.scrollTop / Math.max(1, el1.scrollHeight - el1.clientHeight);
                    el2.scrollTop = pct * (el2.scrollHeight - el2.clientHeight);
                    syncing = false;
                };

                const onScroll2 = () => {
                    if (syncing) return;
                    syncing = true;
                    const pct = el2.scrollTop / Math.max(1, el2.scrollHeight - el2.clientHeight);
                    el1.scrollTop = pct * (el1.scrollHeight - el1.clientHeight);
                    syncing = false;
                };

                el1.addEventListener('scroll', onScroll1);
                el2.addEventListener('scroll', onScroll2);
            } catch (e) {
                // Cross-origin or not ready yet — PDF viewers may block access
                console.log('PDF scroll sync: iframe not accessible', e.message);
            }
        };

        iframe1.addEventListener('load', trySync);
        iframe2.addEventListener('load', trySync);
        // Also try immediately in case already loaded
        trySync();
    };

    // Small delay to let Blazor render the iframes
    setTimeout(setupSync, 500);
}
