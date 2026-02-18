export function initDropZone(dropZoneElement, inputFileElement) {
    if (!dropZoneElement) return;

    dropZoneElement.addEventListener("dragover", (e) => {
        e.preventDefault();
        dropZoneElement.classList.add("drag-over");
    });

    dropZoneElement.addEventListener("dragleave", (e) => {
        e.preventDefault();
        dropZoneElement.classList.remove("drag-over");
    });

    dropZoneElement.addEventListener("drop", (e) => {
        e.preventDefault();
        dropZoneElement.classList.remove("drag-over");

        // Forward dropped files to the hidden InputFile element
        if (inputFileElement && e.dataTransfer.files.length > 0) {
            inputFileElement.files = e.dataTransfer.files;
            inputFileElement.dispatchEvent(new Event("change", { bubbles: true }));
        }
    });
}
