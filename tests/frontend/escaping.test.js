import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

function element() {
    const children = new Map();
    return {
        style: {},
        dataset: {},
        innerHTML: '',
        textContent: '',
        appendChild() {},
        remove() {},
        focus() {},
        addEventListener() {},
        setAttribute() {},
        querySelector(selector) {
            if (!children.has(selector)) children.set(selector, element());
            return children.get(selector);
        }
    };
}

const container = element();
global.localStorage = { getItem: () => null, setItem: () => {}, removeItem: () => {} };
global.document = {
    documentElement: { dataset: {} },
    body: element(),
    addEventListener: () => {},
    querySelectorAll: () => [],
    getElementById: (id) => (id === 'toastContainer' ? container : null),
    createElement: () => element()
};
global.window = {};

const { getAvatar, showToast } = await import('../../src/ItsTool.Web/wwwroot/js/ui.js');

const hostile = 'Sara" onmouseover="alert(1)';

test('avatars escape the name and the photo address', () => {
    assert.ok(!getAvatar(1, hostile).includes('" onmouseover="'));
    assert.ok(!getAvatar(1, 'Sara', 'x" onerror="alert(1)').includes('" onerror="'));
    assert.ok(getAvatar(1, '<b>').includes('&lt;'));
});

test('toasts show the message as text', () => {
    let shown;
    const realCreate = document.createElement;
    document.createElement = () => { shown = element(); return shown; };
    try {
        showToast('<img src=x onerror=alert(1)>', 'error');
    } finally {
        document.createElement = realCreate;
    }
    assert.ok(!shown.innerHTML.includes('<img'));
    assert.equal(shown.querySelector('.toast-message').textContent, '<img src=x onerror=alert(1)>');
});

test('inline handlers only put fixed values inside JS strings', () => {
    // HTML-escaping does not make a value safe inside a JS string in an onclick attribute:
    // the browser decodes &#039; back to ' before the script runs. Pass data through data- attributes instead.
    const fixedValues = new Set(['modalId', 'toastId', 'textareaId', 'a.status', 'title', 'bgColor']);
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const files = [
        ...fs.readdirSync(root).filter(f => f.endsWith('.html')),
        ...fs.readdirSync(path.join(root, 'js')).filter(f => f.endsWith('.js')).map(f => path.join('js', f))
    ];
    const found = [];
    for (const file of files) {
        const text = fs.readFileSync(path.join(root, file), 'utf8');
        for (const handler of text.matchAll(/\bon[a-z]+="([^"]*)"/g)) {
            for (const value of handler[1].matchAll(/'\$\{([^}]+)\}'/g)) {
                if (!fixedValues.has(value[1].trim())) found.push(`${file}: ${value[1]}`);
            }
        }
    }
    assert.deepEqual(found, []);
});
