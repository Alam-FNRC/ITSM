import test from 'node:test';
import assert from 'node:assert/strict';

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

test('every offered language has a dictionary with every English key', () => {
    const english = Object.keys(translations.en);
    for (const { id } of LANGUAGES) {
        assert.ok(translations[id], `no dictionary for ${id}`);
        const missing = english.filter(key => !(key in translations[id]));
        assert.deepEqual(missing, [], `${id} is missing keys`);
    }
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
