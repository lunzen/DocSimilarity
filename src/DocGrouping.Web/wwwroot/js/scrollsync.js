export function syncScroll(pane1, pane2) {
    if (!pane1 || !pane2) return;

    let syncing = false;

    pane1.addEventListener("scroll", () => {
        if (syncing) return;
        syncing = true;
        pane2.scrollTop = pane1.scrollTop;
        syncing = false;
    });

    pane2.addEventListener("scroll", () => {
        if (syncing) return;
        syncing = true;
        pane1.scrollTop = pane2.scrollTop;
        syncing = false;
    });
}
