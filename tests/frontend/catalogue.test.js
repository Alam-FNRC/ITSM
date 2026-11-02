import test from 'node:test';
import assert from 'node:assert/strict';

const stored = new Map([['itsm_lang', 'en']]);
global.localStorage = { getItem: (k) => stored.get(k) ?? null, setItem: (k, v) => stored.set(k, String(v)), removeItem: (k) => stored.delete(k) };
global.document = { documentElement: { dataset: {} }, querySelectorAll: () => [], getElementById: () => null };
global.window = {};

const { categoryGroups, categoryOptionsHtml, categoryPath, serviceOf, defaultTypeFor } = await import('../../src/ItsTool.Web/wwwroot/js/catalogue.js');

// /api/lookup sends active services in catalogue order
const services = [
    { id: 1, name: 'Email', family: 'Communication', description: 'Outlook and shared mailboxes' },
    { id: 2, name: 'Network', family: null, description: null }
];
const categories = [
    { id: 10, name: 'Mailbox full', serviceId: 1, defaultTypeId: null },
    { id: 11, name: 'New shared mailbox', serviceId: 1, defaultTypeId: 2 },
    { id: 12, name: 'Wi-Fi', serviceId: 2 },
    { id: 13, name: 'Printer jam', serviceId: null },
    { id: 14, name: 'Old phones', serviceId: 99 } // its service was switched off
];

test('categories are grouped under their service, the rest under Other', () => {
    const groups = categoryGroups(categories, services);

    assert.deepEqual(groups.map(g => g.label), ['Communication › Email', 'Network', 'Other']);
    assert.deepEqual(groups[0].categories.map(c => c.id), [10, 11]);
    assert.deepEqual(groups[2].categories.map(c => c.name), ['Old phones', 'Printer jam']);
});

test('without a catalogue the list has no groups', () => {
    const groups = categoryGroups(categories.slice(3), []);

    assert.equal(groups.length, 1);
    assert.equal(groups[0].label, null);
    assert.ok(!categoryOptionsHtml(categories.slice(3), []).includes('optgroup'));
});

test('the options escape names and mark the current category', () => {
    const html = categoryOptionsHtml([{ id: 5, name: '<img src=x onerror=alert(1)>', serviceId: 3 }], [{ id: 3, name: 'Apps "&" <b>', family: null }], 5);

    assert.ok(!html.includes('<img'));
    assert.ok(!html.includes('<b>'));
    assert.ok(html.includes('label="Apps &quot;&amp;&quot; &lt;b&gt;"'));
    assert.ok(html.includes('<option value="5" selected>'));
});

test('a ticket shows its service with its category', () => {
    assert.equal(categoryPath(11, categories, services), 'Email › New shared mailbox');
    assert.equal(categoryPath(13, categories, services), 'Printer jam');
    assert.equal(categoryPath(404, categories, services), '-');
});

test('a category brings its service and ticket type to the form', () => {
    assert.equal(serviceOf('10', categories, services).description, 'Outlook and shared mailboxes');
    assert.equal(serviceOf(13, categories, services), null);
    assert.equal(defaultTypeFor('11', categories), 2);
    assert.equal(defaultTypeFor(10, categories), null);
    assert.equal(defaultTypeFor('', categories), null);
});
