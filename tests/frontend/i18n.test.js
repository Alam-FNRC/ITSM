import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const stored = new Map();
global.localStorage = {
    getItem: (key) => (stored.has(key) ? stored.get(key) : null),
    setItem: (key, value) => stored.set(key, String(value)),
    removeItem: (key) => stored.delete(key)
};

const translatable = [];
global.document = {
    documentElement: {},
    querySelectorAll: (selector) => (selector === '[data-i18n]' ? translatable : [])
};
global.Node = { TEXT_NODE: 3 };

const { translations, LANGUAGES, t, getCurrentLanguage, getTextDirection, getLocale, applyTranslations } =
    await import('../../src/ItsTool.Web/wwwroot/js/i18n.js');

function useLanguage(lang) {
    if (lang === null) stored.delete('itsm_lang');
    else stored.set('itsm_lang', lang);
}

function translatableElement(key) {
    const textNode = { nodeType: 3, nodeValue: 'default text' };
    const el = { dataset: { i18n: key }, childNodes: [textNode] };
    translatable.push(el);
    return textNode;
}

test('every offered language has a dictionary with exactly the English keys', () => {
    const english = Object.keys(translations.en);
    for (const { id } of LANGUAGES) {
        assert.ok(translations[id], `no dictionary for ${id}`);
        const missing = english.filter(key => !(key in translations[id]));
        const unknown = Object.keys(translations[id]).filter(key => !(key in translations.en));
        assert.deepEqual(missing, [], `${id} is missing keys`);
        assert.deepEqual(unknown, [], `${id} has keys English does not`);
    }
});

test('translations keep the placeholders and HTML tags of the English text', () => {
    const marks = (text) => (String(text).match(/\{\d\}|<\/?[a-z]+>/g) ?? []).sort().join(' ');
    for (const { id } of LANGUAGES) {
        for (const [key, english] of Object.entries(translations.en)) {
            assert.equal(marks(translations[id][key]), marks(english), `${id}.${key}`);
        }
    }
});

test('every key the pages use is defined', () => {
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const files = [
        ...fs.readdirSync(root).filter(f => f.endsWith('.html')),
        ...fs.readdirSync(path.join(root, 'js')).filter(f => f.endsWith('.js') && !f.includes('.min.')).map(f => path.join('js', f))
    ];
    const undefinedKeys = [];
    for (const file of files) {
        const text = fs.readFileSync(path.join(root, file), 'utf8');
        for (const m of text.matchAll(/data-i18n="([^"$]+)"|\bt\('([a-z0-9_]+)'/g)) {
            const key = m[1] ?? m[2];
            // Keys ending in _ are prefixes completed at run time, such as db_ + a status name
            if (!key.endsWith('_') && !(key in translations.en)) undefinedKeys.push(`${file}: ${key}`);
        }
    }
    assert.deepEqual(undefinedKeys, []);
});

test('Arabic is offered, written right to left, with Latin digits', () => {
    assert.equal(getTextDirection('ar'), 'rtl');
    assert.equal(getLocale('ar'), 'ar-AE-u-nu-latn');
    useLanguage('ar');
    applyTranslations();
    assert.equal(document.documentElement.lang, 'ar');
    assert.equal(document.documentElement.dir, 'rtl');
    assert.equal(t('nav_tickets'), 'التذاكر');
});

test('the language defaults to English, also when the saved one is not offered', () => {
    useLanguage(null);
    assert.equal(getCurrentLanguage(), 'en');
    useLanguage('xx');
    assert.equal(getCurrentLanguage(), 'en');
    useLanguage('__proto__');
    assert.equal(getCurrentLanguage(), 'en');
    useLanguage('tr');
    assert.equal(getCurrentLanguage(), 'tr');
});

test('t() uses the current language, then English, then the fallback, then the key', () => {
    useLanguage('tr');
    assert.equal(t('topbar_language'), translations.tr.topbar_language);

    translations.en.only_in_english = 'Only in English';
    try {
        assert.equal(t('only_in_english', 'fallback'), 'Only in English');
    } finally {
        delete translations.en.only_in_english;
    }

    assert.equal(t('no_such_key', 'fallback'), 'fallback');
    assert.equal(t('no_such_key'), 'no_such_key');
});

test('applyTranslations sets the page language and text direction', () => {
    useLanguage('tr');
    applyTranslations();
    assert.equal(document.documentElement.lang, 'tr');
    assert.equal(document.documentElement.dir, 'ltr');
    assert.equal(getTextDirection('en'), 'ltr');
    assert.equal(getTextDirection('xx'), 'ltr');
});

test('applyTranslations shows English for a key the language does not have yet', () => {
    translations.en.only_in_english = 'Only in English';
    try {
        useLanguage('tr');
        const node = translatableElement('only_in_english');
        applyTranslations();
        assert.equal(node.nodeValue, 'Only in English');
    } finally {
        delete translations.en.only_in_english;
        translatable.length = 0;
    }
});

test('dates and numbers use the locale of the current language', () => {
    useLanguage('tr');
    assert.equal(getLocale(), 'tr-TR');
    useLanguage(null);
    assert.equal(getLocale(), 'en-US');
});

test('data-i18n-target="title" translates the tooltip and leaves the content alone', () => {
    useLanguage('tr');
    const icon = { nodeType: 1 };
    const button = { dataset: { i18n: 'login_toggle_password', i18nTarget: 'title' }, childNodes: [icon], title: 'Show/Hide Password' };
    translatable.push(button);
    try {
        applyTranslations();
        assert.equal(button.title, translations.tr.login_toggle_password);
        assert.deepEqual(button.childNodes, [icon]);
    } finally {
        translatable.length = 0;
    }
});

test('data-i18n-target="aria-label" translates the accessible name of an icon button', () => {
    useLanguage('ar');
    const attributes = {};
    const button = { dataset: { i18n: 'btn_close', i18nTarget: 'aria-label' }, childNodes: [{ nodeType: 1 }], setAttribute: (name, value) => { attributes[name] = value; } };
    translatable.push(button);
    try {
        applyTranslations();
        assert.equal(attributes['aria-label'], translations.ar.btn_close);
    } finally {
        translatable.length = 0;
    }
});

test('computed keys pass their fallback to t(), not after ||', () => {
    // t() returns the key when the dictionaries lack it, so `t('db_' + name) || name` showed "db_..." for
    // statuses and priorities an administrator added; `t('db_' + name, name)` shows the name.
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const files = [
        ...fs.readdirSync(root).filter(f => f.endsWith('.html')),
        ...fs.readdirSync(path.join(root, 'js')).filter(f => f.endsWith('.js') && !f.includes('.min.')).map(f => path.join('js', f))
    ];
    const found = [];
    for (const file of files) {
        const text = fs.readFileSync(path.join(root, file), 'utf8');
        for (const m of text.matchAll(/\bt\((?:'[a-z_]+'\s*\+|[a-zA-Z]+Key\))[^;\n]*?\)\s*\|\|/g)) found.push(`${file}: ${m[0]}`);
    }
    assert.deepEqual(found, []);
});
