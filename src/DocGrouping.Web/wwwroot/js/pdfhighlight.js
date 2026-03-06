import * as pdfjsLib from '/lib/pdfjs/pdf.min.mjs';

pdfjsLib.GlobalWorkerOptions.workerSrc = '/lib/pdfjs/pdf.worker.min.mjs';

/**
 * Renders a PDF into a container. Returns per-page canvas data for pixel comparison.
 */
async function renderPdf(container, pdfUrl) {
    container.innerHTML = '';
    container.style.overflowY = 'auto';

    const loadingDiv = document.createElement('div');
    loadingDiv.className = 'text-center text-muted p-3';
    loadingDiv.textContent = 'Loading PDF...';
    container.appendChild(loadingDiv);

    const pdf = await pdfjsLib.getDocument(pdfUrl).promise;
    container.innerHTML = '';

    const pages = [];

    for (let pageNum = 1; pageNum <= pdf.numPages; pageNum++) {
        const page = await pdf.getPage(pageNum);
        const baseViewport = page.getViewport({ scale: 1 });
        const scale = Math.max((container.clientWidth - 20) / baseViewport.width, 0.5);
        const viewport = page.getViewport({ scale });

        // Page wrapper
        const pageDiv = document.createElement('div');
        pageDiv.className = 'pdf-page-wrapper';
        pageDiv.style.position = 'relative';
        pageDiv.style.width = viewport.width + 'px';
        pageDiv.style.height = viewport.height + 'px';
        pageDiv.style.margin = '0 auto 8px auto';

        // Canvas — the crisp PDF rendering
        const canvas = document.createElement('canvas');
        const dpr = window.devicePixelRatio || 1;
        canvas.width = viewport.width * dpr;
        canvas.height = viewport.height * dpr;
        canvas.style.width = viewport.width + 'px';
        canvas.style.height = viewport.height + 'px';
        canvas.style.position = 'absolute';
        canvas.style.top = '0';
        canvas.style.left = '0';
        pageDiv.appendChild(canvas);

        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);
        await page.render({ canvasContext: ctx, viewport }).promise;

        // Highlight overlay canvas (transparent, drawn on top)
        const overlayCanvas = document.createElement('canvas');
        overlayCanvas.width = viewport.width * dpr;
        overlayCanvas.height = viewport.height * dpr;
        overlayCanvas.style.width = viewport.width + 'px';
        overlayCanvas.style.height = viewport.height + 'px';
        overlayCanvas.style.position = 'absolute';
        overlayCanvas.style.top = '0';
        overlayCanvas.style.left = '0';
        overlayCanvas.style.pointerEvents = 'none';
        overlayCanvas.className = 'pdf-highlight-overlay';
        pageDiv.appendChild(overlayCanvas);

        pages.push({ pageDiv, viewport, canvas, overlayCanvas });
        container.appendChild(pageDiv);
    }

    return pages;
}

/**
 * Pixel-based page comparison.
 * Renders both pages at a normalized size, compares blocks of pixels,
 * and draws highlight rectangles over regions that differ.
 *
 * Works regardless of font embedding issues — compares what's actually visible.
 */
function computePixelDiff(pages1, pages2) {
    const dpr = window.devicePixelRatio || 1;
    // Block size in CSS pixels — each block is checked as a unit
    const blockSize = 10;
    const blockPx = blockSize * dpr;
    // Luminance difference threshold — filters out sub-pixel font rendering noise
    const lumaThreshold = 80;
    // What fraction of pixels in a block must differ to mark the block as changed
    const blockDiffFraction = 0.20;

    const pageCount = Math.min(pages1.length, pages2.length);

    for (let p = 0; p < pageCount; p++) {
        const page1 = pages1[p];
        const page2 = pages2[p];
        const c1 = page1.canvas;
        const c2 = page2.canvas;

        // Get pixel data at the rendered resolution
        const ctx1 = c1.getContext('2d', { willReadFrequently: true });
        const ctx2 = c2.getContext('2d', { willReadFrequently: true });

        // Use the smaller dimensions to avoid out-of-bounds
        const w = Math.min(c1.width, c2.width);
        const h = Math.min(c1.height, c2.height);

        const data1 = ctx1.getImageData(0, 0, w, h).data;
        const data2 = ctx2.getImageData(0, 0, w, h).data;

        const blocksX = Math.ceil(w / blockPx);
        const blocksY = Math.ceil(h / blockPx);

        const diffBlocks1 = [];
        const diffBlocks2 = [];

        for (let by = 0; by < blocksY; by++) {
            for (let bx = 0; bx < blocksX; bx++) {
                const startX = bx * blockPx;
                const startY = by * blockPx;
                const endX = Math.min(startX + blockPx, w);
                const endY = Math.min(startY + blockPx, h);

                let diffPixels = 0;
                let totalPixels = 0;

                for (let y = startY; y < endY; y += 2) { // sample every other row for speed
                    for (let x = startX; x < endX; x += 2) { // sample every other col
                        const idx = (y * w + x) * 4;
                        // Use luminance to avoid font hinting / sub-pixel rendering noise
                        const luma1 = data1[idx] * 0.299 + data1[idx + 1] * 0.587 + data1[idx + 2] * 0.114;
                        const luma2 = data2[idx] * 0.299 + data2[idx + 1] * 0.587 + data2[idx + 2] * 0.114;
                        totalPixels++;
                        if (Math.abs(luma1 - luma2) > lumaThreshold) {
                            diffPixels++;
                        }
                    }
                }

                if (totalPixels > 0 && diffPixels / totalPixels > blockDiffFraction) {
                    // Convert back to CSS pixel coordinates
                    const rect = {
                        x: startX / dpr,
                        y: startY / dpr,
                        w: (endX - startX) / dpr,
                        h: (endY - startY) / dpr
                    };
                    diffBlocks1.push(rect);
                    diffBlocks2.push(rect);
                }
            }
        }

        // Merge adjacent diff blocks into larger rectangles for cleaner highlights
        const merged1 = mergeRects(diffBlocks1);
        const merged2 = mergeRects(diffBlocks2);

        // Draw on overlay canvases
        drawPixelHighlights(page1.overlayCanvas, merged1, 'rgba(220, 53, 69, 0.3)', 'rgba(220, 53, 69, 0.8)');
        drawPixelHighlights(page2.overlayCanvas, merged2, 'rgba(40, 167, 69, 0.3)', 'rgba(40, 167, 69, 0.8)');
    }
}

/**
 * Merges nearby rectangles by snapping to a grid and flood-filling connected components,
 * then computing bounding boxes for each component.
 */
function mergeRects(rects) {
    if (rects.length === 0) return [];

    // Use block coordinates — each rect is already one block
    const blockSize = rects.length > 0 ? rects[0].w : 12;
    const grid = new Map();

    for (const r of rects) {
        const gx = Math.round(r.x / blockSize);
        const gy = Math.round(r.y / blockSize);
        grid.set(`${gx},${gy}`, { gx, gy, r });
    }

    const visited = new Set();
    const merged = [];

    for (const [key, { gx, gy }] of grid) {
        if (visited.has(key)) continue;

        // Flood fill to find connected component
        const component = [];
        const stack = [{ gx, gy }];
        while (stack.length > 0) {
            const { gx: cx, gy: cy } = stack.pop();
            const ck = `${cx},${cy}`;
            if (visited.has(ck) || !grid.has(ck)) continue;
            visited.add(ck);
            component.push(grid.get(ck).r);
            // Check 8-connected neighbors
            for (let dx = -1; dx <= 1; dx++) {
                for (let dy = -1; dy <= 1; dy++) {
                    if (dx === 0 && dy === 0) continue;
                    stack.push({ gx: cx + dx, gy: cy + dy });
                }
            }
        }

        // Compute bounding box of component
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const r of component) {
            minX = Math.min(minX, r.x);
            minY = Math.min(minY, r.y);
            maxX = Math.max(maxX, r.x + r.w);
            maxY = Math.max(maxY, r.y + r.h);
        }
        merged.push({ x: minX, y: minY, w: maxX - minX, h: maxY - minY });
    }

    return merged;
}

function drawPixelHighlights(overlayCanvas, rects, fillColor, strokeColor) {
    const dpr = window.devicePixelRatio || 1;
    const ctx = overlayCanvas.getContext('2d');
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, overlayCanvas.width, overlayCanvas.height);
    ctx.scale(dpr, dpr);

    const pad = 3;
    for (const r of rects) {
        // Fill with semi-transparent color
        ctx.fillStyle = fillColor;
        roundRect(ctx,
            r.x - pad,
            r.y - pad,
            r.w + pad * 2,
            r.h + pad * 2,
            4
        );
        // Draw a visible border around the diff region
        ctx.strokeStyle = strokeColor;
        ctx.lineWidth = 2;
        roundRectStroke(ctx,
            r.x - pad,
            r.y - pad,
            r.w + pad * 2,
            r.h + pad * 2,
            4
        );
    }
}

function roundRectPath(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + r);
    ctx.lineTo(x + w, y + h - r);
    ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
    ctx.lineTo(x + r, y + h);
    ctx.quadraticCurveTo(x, y + h, x, y + h - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
}

function roundRect(ctx, x, y, w, h, r) {
    roundRectPath(ctx, x, y, w, h, r);
    ctx.fill();
}

function roundRectStroke(ctx, x, y, w, h, r) {
    roundRectPath(ctx, x, y, w, h, r);
    ctx.stroke();
}

function clearHighlights(pages) {
    for (const page of pages) {
        const ctx = page.overlayCanvas.getContext('2d');
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, page.overlayCanvas.width, page.overlayCanvas.height);
    }
}

function syncScroll(container1, container2) {
    let syncing = false;
    container1.addEventListener('scroll', () => {
        if (syncing) return;
        syncing = true;
        const pct = container1.scrollTop / Math.max(1, container1.scrollHeight - container1.clientHeight);
        container2.scrollTop = pct * (container2.scrollHeight - container2.clientHeight);
        syncing = false;
    });
    container2.addEventListener('scroll', () => {
        if (syncing) return;
        syncing = true;
        const pct = container2.scrollTop / Math.max(1, container2.scrollHeight - container2.clientHeight);
        container1.scrollTop = pct * (container1.scrollHeight - container1.clientHeight);
        syncing = false;
    });
}

/**
 * Main entry point. Renders both PDFs, compares pixels, draws highlight rectangles.
 */
export async function renderPdfComparison(container1, container2, pdfUrl1, pdfUrl2, enableHighlights) {
    try {
        const [pages1, pages2] = await Promise.all([
            renderPdf(container1, pdfUrl1),
            renderPdf(container2, pdfUrl2),
        ]);

        if (enableHighlights) {
            computePixelDiff(pages1, pages2);
        }

        syncScroll(container1, container2);
    } catch (err) {
        console.error('PDF comparison error:', err);
        if (container1.children.length === 0) {
            container1.innerHTML = `<div class="text-danger p-3">Failed to load PDF: ${err.message}</div>`;
        }
        if (container2.children.length === 0) {
            container2.innerHTML = `<div class="text-danger p-3">Failed to load PDF: ${err.message}</div>`;
        }
    }
}
