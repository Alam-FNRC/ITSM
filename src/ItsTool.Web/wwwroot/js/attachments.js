// Attachment downloads need the login token, which <img src> and <a href> cannot send.
// Mark elements with data-auth-src / data-auth-href set to an API path (for example
// /Ticket/1/attachments/2/download); this module fetches the file with the token and
// points the element at a blob: URL instead.

const blobUrls = new Map();

function blobUrlFor(api, path) {
    if (!blobUrls.has(path)) {
        const pending = api.fetchFile(path).then(blob => URL.createObjectURL(blob));
        // Forget failures so the next render tries again
        pending.catch(() => blobUrls.delete(path));
        blobUrls.set(path, pending);
    }
    return blobUrls.get(path);
}

export function loadAuthorizedImages(api, root) {
    for (const img of root.querySelectorAll('img[data-auth-src]')) {
        const path = img.dataset.authSrc;
        img.removeAttribute('data-auth-src');
        // A failed image keeps showing its alt text; no error toast on every re-render
        blobUrlFor(api, path).then(url => { img.src = url; }, () => {});
    }
}

export async function openAuthorizedLink(api, link, win = window, doc = document) {
    // Open the tab now, while still handling the click, or the browser blocks it as a pop-up
    const tab = link.target === '_blank' ? win.open('', '_blank') : null;
    try {
        const url = await blobUrlFor(api, link.dataset.authHref);
        if (tab) {
            tab.location.href = url;
            return;
        }
        const anchor = doc.createElement('a');
        anchor.href = url;
        anchor.download = link.getAttribute('download') || '';
        doc.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
    } catch (err) {
        if (tab) tab.close();
        throw err;
    }
}

export function initAuthorizedAttachments(api, onLinkError = () => {}) {
    loadAuthorizedImages(api, document);
    new MutationObserver(() => loadAuthorizedImages(api, document))
        .observe(document.body, { childList: true, subtree: true });

    document.addEventListener('click', event => {
        const link = event.target.closest?.('a[data-auth-href]');
        if (!link) return;
        event.preventDefault();
        openAuthorizedLink(api, link).catch(onLinkError);
    });
}
