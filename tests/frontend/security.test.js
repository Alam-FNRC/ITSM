import test from 'node:test';
import assert from 'node:assert/strict';

const stored = new Map();
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

const { translations } = await import('../../src/ItsTool.Web/wwwroot/js/i18n.js');
const security = await import('../../src/ItsTool.Web/wwwroot/js/security.js');
const {
    describeAction, describeCustody, incidentIdFromUrl, statusLabel, categoryLabel, formatFileSize,
    STATUSES, OUTCOMES, CATEGORIES, ITEM_KINDS, ITEM_STATUSES, EVIDENCE_KINDS, CUSTODY_KINDS
} = security;

const names = { 1: 'Sara <b>Admin</b>', 2: 'Omar' };

test('every value the API sends has a label in every language', () => {
    const keyOf = (prefix, value) => `${prefix}_${value.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;
    const keys = [
        ...STATUSES.map(v => keyOf('sec_status', v)),
        ...[...OUTCOMES, 'Pending'].map(v => keyOf('sec_outcome', v)),
        ...[...CATEGORIES, 'Unclassified'].map(v => keyOf('sec_cat', v)),
        ...ITEM_KINDS.map(v => keyOf('sec_kind', v)),
        ...ITEM_STATUSES.map(v => keyOf('sec_item', v)),
        ...['Received', 'InProgress', 'Closed'].map(v => keyOf('sec_progress', v)),
        ...EVIDENCE_KINDS.map(v => keyOf('sec_ev_kind', v)),
        'sec_act_evidence_added', 'sec_act_evidence_handed_over', 'sec_act_evidence_failed',
        'sec_cust_collected_file', 'sec_cust_collected_item', 'sec_cust_handed_over', 'sec_cust_downloaded',
        'sec_cust_verified', 'sec_cust_failed', 'sec_cust_missing',
        'sec_sev_1', 'sec_sev_2', 'sec_sev_3', 'sec_sev_4', 'sec_sev_none'
    ];
    for (const lang of Object.keys(translations)) {
        assert.deepEqual(keys.filter(k => !(k in translations[lang])), [], `missing in ${lang}`);
    }
});

test('labels follow the reader language and fall back to the value', () => {
    stored.set('itsm_lang', 'ar');
    assert.equal(statusLabel('Containment'), translations.ar.sec_status_containment);
    assert.equal(categoryLabel('UnauthorisedAccess'), translations.ar.sec_cat_unauthorised_access);
    assert.equal(statusLabel('SomethingNew'), 'SomethingNew');
    stored.set('itsm_lang', 'en');
});

test('history entries escape names, values and notes', () => {
    const hostile = '<img src=x onerror="alert(1)">';
    const added = describeAction({ kind: 'AffectedItemAdded', userId: 1, newValue: 'Server', note: hostile }, names);
    assert.ok(!added.html.includes('<img'));
    assert.ok(!added.html.includes('<b>'));
    assert.ok(added.html.includes('&lt;img'));

    const updated = describeAction({ kind: 'Updated', userId: 2, field: 'Title', oldValue: hostile, newValue: '$& done' }, names);
    assert.ok(!updated.html.includes('<img'));
    assert.ok(updated.html.includes('$&amp; done'));

    // Notes are returned as plain text for the page to escape; the HTML part carries no note
    const note = describeAction({ kind: 'Note', userId: 2, note: hostile }, names);
    assert.equal(note.note, hostile);
    assert.ok(!note.html.includes(hostile));
});

test('history entries read naturally', () => {
    assert.equal(
        describeAction({ kind: 'StatusChanged', userId: 2, oldValue: 'Triage', newValue: 'Containment', note: null }, names).html,
        'Omar moved the incident from Triage to Containment');
    assert.equal(
        describeAction({ kind: 'Updated', userId: 2, field: 'Severity', oldValue: null, newValue: '1' }, names).html,
        'Omar changed the severity from (none) to 1 – Critical');
    assert.equal(
        describeAction({ kind: 'MemberAdded', userId: 2, newValue: '9' }, names).html,
        'Omar added #9 to the incident');
});

test('evidence history and custody entries read naturally and escape what people typed', () => {
    const hostile = '<img src=x onerror="alert(1)">';
    assert.equal(describeAction({ kind: 'EvidenceAdded', userId: 2, newValue: '3' }, names).html, 'Omar added evidence E3');
    assert.equal(describeAction({ kind: 'EvidenceIntegrityFailed', userId: 2, newValue: '3' }, names).html,
        'Omar found that evidence E3 failed its fingerprint check');

    const handOver = describeAction({ kind: 'EvidenceHandedOver', userId: 2, oldValue: '1', newValue: '1', note: hostile }, names);
    assert.ok(handOver.html.startsWith('Omar recorded that E1 was handed to Sara &lt;b&gt;Admin&lt;/b&gt;'));
    assert.ok(!handOver.html.includes('<img') && handOver.html.includes('&lt;img'));

    const moved = describeCustody({ kind: 'HandedOver', userId: 2, custodianUserId: 1, location: hostile, note: 'Seal 0042' }, names, false);
    assert.ok(!moved.html.includes('<img') && !moved.html.includes('<b>'));
    assert.equal(moved.note, 'Seal 0042');

    assert.equal(describeCustody({ kind: 'Collected', userId: 2 }, names, true).html, 'Omar stored the file and took its fingerprint');
    assert.equal(describeCustody({ kind: 'Collected', userId: 2, custodianUserId: 2, location: 'Safe' }, names, false).html, 'Omar recorded it, held by Omar at: Safe');
    assert.equal(describeCustody({ kind: 'IntegrityFailed', userId: 2, sha256: null }, names, true).html, 'Omar found the file missing');
    assert.equal(describeCustody({ kind: 'IntegrityFailed', userId: 2, sha256: 'ab' }, names, true).html, 'Omar found that the file no longer matches its fingerprint');
    for (const kind of CUSTODY_KINDS) {
        assert.ok(!describeCustody({ kind, userId: 2 }, names, true).html.includes('Omar ' + kind), kind);
    }
});

test('file sizes are short and readable', () => {
    assert.equal(formatFileSize(512), '512 B');
    assert.equal(formatFileSize(2048), '2.0 KB');
    assert.equal(formatFileSize(25 * 1024 * 1024), '25.0 MB');
    assert.equal(formatFileSize(null), '');
});

test('only a plain number is taken as the incident id', () => {
    assert.equal(incidentIdFromUrl('?id=42'), 42);
    assert.equal(incidentIdFromUrl('?id=../users/2'), null);
    assert.equal(incidentIdFromUrl('?id=4abc'), null);
    assert.equal(incidentIdFromUrl(''), null);
});
