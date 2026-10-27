import test from 'node:test';
import assert from 'node:assert/strict';
import { loadAuthorizedImages, openAuthorizedLink } from '../../src/ItsTool.Web/wwwroot/js/attachments.js';

function fakeApi(fail = false) {
    const calls = [];
    return {
        calls,
        async fetchFile(path) {
            calls.push(path);
            if (fail) throw new Error('403');
            return new Blob(['file body']);
        }
    };
}

function fakeImage(path) {
    const attributes = new Map([['data-auth-src', path]]);
    return {
        src: '',
        dataset: { authSrc: path },
        removeAttribute(name) { attributes.delete(name); },
        hasAttribute(name) { return attributes.has(name); }
    };
}

function fakeLink(path, { target = '', download = null } = {}) {
    return { target, dataset: { authHref: path }, getAttribute: name => (name === 'download' ? download : null) };
}

function fakeDocument() {
    const clicked = [];
    return {
        clicked,
        body: { appendChild() {} },
        createElement() {
            return { href: '', download: '', click() { clicked.push({ href: this.href, download: this.download }); }, remove() {} };
        }
    };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

test('attachments.js - images load through the API with the token and are fetched once per file', async () => {
    const api = fakeApi();
    const first = fakeImage('/Ticket/1/attachments/10/download');
    const second = fakeImage('/Ticket/1/attachments/10/download');

    loadAuthorizedImages(api, { querySelectorAll: () => [first, second] });
    await settle();

    assert.deepEqual(api.calls, ['/Ticket/1/attachments/10/download']);
    assert.match(first.src, /^blob:/);
    assert.equal(second.src, first.src);
    assert.equal(first.hasAttribute('data-auth-src'), false);
});

test('attachments.js - a failed image keeps no src and is retried on the next render', async () => {
    const api = fakeApi(true);
    const image = fakeImage('/Ticket/1/attachments/11/download');

    loadAuthorizedImages(api, { querySelectorAll: () => [image] });
    await settle();
    loadAuthorizedImages(api, { querySelectorAll: () => [fakeImage('/Ticket/1/attachments/11/download')] });
    await settle();

    assert.equal(image.src, '');
    assert.equal(api.calls.length, 2);
});

test('attachments.js - download links save the file under its own name', async () => {
    const doc = fakeDocument();

    await openAuthorizedLink(fakeApi(), fakeLink('/Ticket/1/attachments/12/download', { download: 'report.pdf' }), {}, doc);

    assert.equal(doc.clicked.length, 1);
    assert.equal(doc.clicked[0].download, 'report.pdf');
    assert.match(doc.clicked[0].href, /^blob:/);
});

test('attachments.js - new-tab links open the file in the tab opened during the click', async () => {
    const tab = { location: { href: '' }, closed: false, close() { this.closed = true; } };
    const win = { open: () => tab };

    await openAuthorizedLink(fakeApi(), fakeLink('/Ticket/1/attachments/13/download', { target: '_blank' }), win, fakeDocument());

    assert.match(tab.location.href, /^blob:/);
});

test('attachments.js - a refused download closes the new tab and reports the error', async () => {
    const tab = { location: { href: '' }, closed: false, close() { this.closed = true; } };
    const win = { open: () => tab };

    await assert.rejects(openAuthorizedLink(fakeApi(true), fakeLink('/Ticket/1/attachments/14/download', { target: '_blank' }), win, fakeDocument()));

    assert.equal(tab.closed, true);
});
