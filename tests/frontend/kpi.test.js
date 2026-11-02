import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

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
const { periodPreset, rate, formatPercent, formatWorkingTime, rowName, kpiCsvRows, DIMENSIONS } = await import('../../src/ItsTool.Web/wwwroot/js/kpi.js');

const report = {
    from: '2026-10-01', to: '2026-10-31', generatedAt: '2026-11-02T06:00:00Z',
    summary: {
        opened: 10, solved: 8, closed: 6, responseDue: 9, responseMet: 8, resolutionDue: 8, resolutionMet: 6,
        meanTimeToOwnMinutes: 45, meanTimeToResolveMinutes: 390, meanTimeToCloseMinutes: null,
        reopened: 1, firstTimeFixed: 6, surveyCount: 3, satisfactionAverage: 4.33
    },
    months: [{ year: 2026, month: 10, opened: 10, solved: 8 }],
    backlog: { open: 4, late: 1, days0To3: 2, days4To7: 1, days8To30: 1, over30Days: 0 },
    breakdowns: {
        team: [{ id: 7, name: '=HYPERLINK("x")', opened: 6, solved: 5, meanTimeToOwnMinutes: 30, meanTimeToResolveMinutes: 120, responseDue: 6, responseMet: 6, resolutionDue: 5, resolutionMet: 4, reopened: 0 },
               { id: null, name: null, opened: 4, solved: 3, meanTimeToOwnMinutes: null, meanTimeToResolveMinutes: 90, responseDue: 3, responseMet: 2, resolutionDue: 3, resolutionMet: 2, reopened: 1 }],
        agent: [], category: [], department: [], priority: []
    }
};

test('every KPI text exists in every language', () => {
    const source = fs.readFileSync(new URL('../../src/ItsTool.Web/wwwroot/js/kpi.js', import.meta.url), 'utf8')
        + fs.readFileSync(new URL('../../src/ItsTool.Web/wwwroot/kpi.html', import.meta.url), 'utf8');
    const keys = new Set([...source.matchAll(/['"`](kpi_[a-z0-9_]+)['"`]/g)].map(m => m[1]));
    for (const d of DIMENSIONS) keys.add(`kpi_by_${d}`).add(`kpi_filter_${d}`);
    for (const p of ['last_month', 'this_month', 'last_3_months', 'this_year', 'custom']) keys.add(`kpi_preset_${p}`);
    for (const d of ['service', 'team', 'agent', 'department']) keys.add(`kpi_none_${d}`);
    keys.add('nav_kpi');
    assert.ok(keys.size > 50);
    for (const lang of Object.keys(translations)) {
        assert.deepEqual([...keys].filter(k => !(k in translations[lang])), [], `missing in ${lang}`);
    }
});

test('presets cover whole months counted from today', () => {
    const today = new Date(2026, 10, 2);
    assert.deepEqual(periodPreset('lastMonth', today), { from: '2026-10-01', to: '2026-10-31' });
    assert.deepEqual(periodPreset('thisMonth', today), { from: '2026-11-01', to: '2026-11-02' });
    assert.deepEqual(periodPreset('last3Months', today), { from: '2026-08-01', to: '2026-10-31' });
    assert.deepEqual(periodPreset('thisYear', today), { from: '2026-01-01', to: '2026-11-02' });
    // In January, last month is in the year before
    assert.deepEqual(periodPreset('lastMonth', new Date(2026, 0, 15)), { from: '2025-12-01', to: '2025-12-31' });
});

test('rates are whole percentages and nothing due shows a dash', () => {
    assert.equal(rate(1, 3), 33);
    assert.equal(rate(0, 0), null);
    assert.equal(formatPercent(6, 8), '75%');
    assert.equal(formatPercent(0, 0), '—');
});

test('working minutes read as hours and minutes', () => {
    assert.equal(formatWorkingTime(null), '—');
    assert.equal(formatWorkingTime(45), '45 min');
    assert.equal(formatWorkingTime(90.4), '1 hr 30 min');
    assert.equal(formatWorkingTime(120), '2 hr');
});

test('the row without an id is named for its dimension', () => {
    assert.equal(rowName('agent', { id: null }), 'Unassigned');
    assert.equal(rowName('team', { id: null }), 'No team');
    assert.equal(rowName('service', { id: null }), 'No service');
    assert.equal(rowName('category', { id: null }), 'None');
    assert.equal(rowName('agent', { id: 3, name: 'Sara Ali' }), 'Sara Ali');
});

test('the spreadsheet has every section and keeps names as typed', () => {
    const rows = kpiCsvRows(report, { periodText: 'P', filtersText: 'F', generatedText: 'G' });

    assert.deepEqual(rows.slice(0, 4), [['Service desk KPIs'], ['P'], ['F'], ['G']]);
    assert.deepEqual(rows.find(r => r[0] === 'Response on time'), ['Response on time', 8, 9]);
    assert.deepEqual(rows.find(r => r[0] === 'Mean time to resolve (working hours)'), ['Mean time to resolve (working hours)', '6.5']);
    assert.deepEqual(rows.find(r => r[0] === 'Mean time to close (working hours)'), ['Mean time to close (working hours)', '']);
    assert.deepEqual(rows.find(r => r[0] === '2026-10'), ['2026-10', 10, 8]);
    // One header per dimension
    assert.equal(rows.filter(r => r[1] === 'Opened' && r.length === 10).length, DIMENSIONS.length);
    // Names go to the spreadsheet as typed; downloadCsv's csvCell stops them running as formulas
    assert.ok(rows.some(r => r[0] === '=HYPERLINK("x")'));
    assert.ok(rows.some(r => r[0] === 'No team' && r[2] === 3));
});
