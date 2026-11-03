import test from 'node:test';
import assert from 'node:assert/strict';

const stored = new Map([['itsm_lang', 'en']]);
global.localStorage = { getItem: (k) => stored.get(k) ?? null, setItem: (k, v) => stored.set(k, String(v)), removeItem: (k) => stored.delete(k) };
global.document = {
    documentElement: { dataset: {} },
    body: { appendChild() {} },
    addEventListener: () => {},
    querySelectorAll: () => [],
    getElementById: () => null,
    createElement: () => ({ style: {}, dataset: {}, innerHTML: '', appendChild() {}, addEventListener() {}, setAttribute() {}, querySelector: () => null })
};
global.window = {};

await import('../../src/ItsTool.Web/wwwroot/js/api.js');

const refuse = async () => ({ status: 401, ok: false, text: async () => '' });

test('a refused sign-in is answered on the sign-in page, not by reloading it', async () => {
    global.fetch = refuse;
    window.location = { href: 'login page with the typed name' };
    await assert.rejects(window.api.login('someone', 'wrong password'), /Unauthorized/);
    assert.equal(window.location.href, 'login page with the typed name');
});

test('a 401 anywhere else ends the session and sends the user to sign in', async () => {
    global.fetch = refuse;
    window.location = { href: 'tickets page' };
    window.api.setToken('expired');
    await assert.rejects(window.api.request('/tickets'), /Unauthorized/);
    assert.equal(window.location.href, '/login.html');
    assert.equal(localStorage.getItem('jwt_token'), null);
});
