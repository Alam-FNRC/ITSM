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

const { translations } = await import('../../src/ItsTool.Web/wwwroot/js/i18n.js');
const {
    STATUSES, TREATMENTS, LEVELS, FIELDS, WARNINGS, reference, riskScore, riskLevel, warningText, skipLabel, describeHistory, riskIdFromUrl, statusLabel
} = await import('../../src/ItsTool.Web/wwwroot/js/risks.js');

const keyOf = (prefix, value) => `${prefix}_${value.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;

test('every value the API sends has a label in every language', () => {
    const keys = [
        ...STATUSES.map(v => keyOf('risk_status', v)),
        ...TREATMENTS.map(v => keyOf('risk_treat', v)),
        ...LEVELS.map(v => keyOf('risk_level', v)),
        ...FIELDS.map(v => keyOf('risk_field', v)),
        ...WARNINGS.map(v => keyOf('risk_warn', v)),
        ...['AlreadyInRegister', 'NoRatings'].map(v => keyOf('risk_skip', v)),
        ...[1, 2, 3, 4, 5].flatMap(n => [`risk_impact_${n}`, `risk_likelihood_${n}`]),
        'risk_act_created', 'risk_act_imported', 'risk_act_updated', 'risk_act_approved', 'risk_act_approval_cleared', 'risk_act_reviewed', 'risk_act_unknown',
        'nav_risks', 'notif_go_risk'
    ];
    for (const lang of Object.keys(translations)) {
        assert.deepEqual(keys.filter(k => !(k in translations[lang])), [], `missing in ${lang}`);
    }
});

test('the score and level follow the Excel formula and the agreed bands', () => {
    assert.equal(riskScore(3, 5, 5, 4), 17.33);
    assert.equal(riskScore('2', '2', '2', '2'), 4);
    assert.equal(riskScore(6, 1, 1, 1), null);
    assert.equal(riskScore('', 1, 1, 1), null);
    assert.deepEqual([4, 4.33, 12, 12.67].map(riskLevel), ['Low', 'Medium', 'Medium', 'High']);
    assert.equal(riskLevel(null), null);
    assert.equal(reference(7), 'R-007');
    assert.equal(reference(1234), 'R-1234');
});

test('import warnings and skips read as sentences, keeping odd text literal', () => {
    assert.equal(warningText('date:Q432025'), 'Target date "Q432025" was not understood; set it on the risk.');
    assert.equal(warningText('control:A$&9'), '"A$&9" is not an ISO 27001 Annex A control and was left out.');
    assert.equal(skipLabel('NoRatings'), translations.en.risk_skip_no_ratings);
    assert.equal(skipLabel(null), '');
});

test('the history escapes names and values and labels the fields', () => {
    const names = { 1: 'Sara <b>Admin</b>', 3: 'Omar' };
    assert.equal(describeHistory({ kind: 'Updated', userId: 1, field: 'Owner', oldValue: null, newValue: '3' }, names).html,
        'Sara &lt;b&gt;Admin&lt;/b&gt; changed the owner from (empty) to Omar');
    assert.equal(describeHistory({ kind: 'Updated', userId: 3, field: 'Status', oldValue: 'Open', newValue: 'Treating' }, names).html,
        'Omar changed the status from Open to Treating');
    assert.equal(describeHistory({ kind: 'Updated', userId: 3, field: 'Title', oldValue: '<i>x</i>', newValue: 'y' }, names).html,
        'Omar changed the scenario from &lt;i&gt;x&lt;/i&gt; to y');
    const reviewed = describeHistory({ kind: 'Reviewed', userId: 9, note: 'C3 I5 A5 L4 = 17.33 (High)' }, names);
    assert.deepEqual(reviewed, { html: '#9 reviewed the risk', note: 'C3 I5 A5 L4 = 17.33 (High)' });
});

test('labels follow the reader language', () => {
    stored.set('itsm_lang', 'ar');
    assert.equal(statusLabel('Treating'), translations.ar.risk_status_treating);
    stored.set('itsm_lang', 'en');
});

test('only a numeric id in the address opens a risk', () => {
    assert.equal(riskIdFromUrl('?id=12'), 12);
    assert.equal(riskIdFromUrl('?id=12abc'), null);
    assert.equal(riskIdFromUrl(''), null);
});
