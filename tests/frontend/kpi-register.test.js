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
    CYCLES, RESULT_TYPES, DIRECTIONS, YEAR_ENDS, SOURCES, WARNINGS, SKIPS, nameOf, periodCount, periodStart, periodEnd, periodLabel, periodStarted,
    yearEnd, formatValue, targetState, warningText, skipLabel, kpiIdFromUrl, reportCsvRows, cycleLabel
} = await import('../../src/ItsTool.Web/wwwroot/js/kpi-register.js');

const keyOf = (prefix, value) => `${prefix}_${value.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;

test('every value the API sends has a label in every language', () => {
    const keys = [
        ...CYCLES.map(v => keyOf('kpir_cycle', v)),
        ...RESULT_TYPES.map(v => keyOf('kpir_result', v)),
        ...DIRECTIONS.map(v => keyOf('kpir_direction', v)),
        ...YEAR_ENDS.map(v => keyOf('kpir_year_end', v)),
        ...SOURCES.map(v => keyOf('kpir_source', v)),
        ...WARNINGS.map(v => keyOf('kpir_warn', v)),
        ...SKIPS.map(v => keyOf('kpir_skip', v)),
        'kpir_met', 'kpir_missed', 'kpir_no_target', 'kpir_half', 'nav_kpi_register', 'page_kpi', 'notif_go_kpi'
    ];
    for (const lang of Object.keys(translations)) {
        assert.deepEqual(keys.filter(k => !(k in translations[lang])), [], `missing in ${lang}`);
    }
});

test('periods follow the cycle, as KpiPeriods on the server', () => {
    assert.deepEqual(CYCLES.map(periodCount), [1, 2, 4, 12]);
    assert.deepEqual(periodStart('Quarterly', 2026, 3), new Date(2026, 6, 1));
    assert.deepEqual(periodEnd('Quarterly', 2026, 3), new Date(2026, 8, 30));
    assert.deepEqual(periodEnd('Monthly', 2024, 2), new Date(2024, 1, 29));
    assert.equal(periodLabel('Yearly', 2026, 1), '2026');
    assert.equal(periodLabel('Quarterly', 2026, 3), 'Q3 2026');
    assert.equal(periodLabel('HalfYearly', 2026, 2), 'H2 2026');
    assert.equal(periodLabel('Monthly', 2026, 7), 'Jul 2026');
    assert.equal(periodStarted('Monthly', 2026, 11, new Date(2026, 10, 9)), true);
    assert.equal(periodStarted('Monthly', 2026, 12, new Date(2026, 10, 9)), false);
});

test('the year figure is the latest value or the average, and a target is met by direction', () => {
    const values = [{ cycle: 'Quarterly', period: 1, value: 5 }, { cycle: 'Quarterly', period: 3, value: 9 }, { cycle: 'Quarterly', period: 2, value: 7 }];
    assert.equal(yearEnd('Last', 2026, values), 9);
    assert.equal(yearEnd('Average', 2026, values), 7);
    assert.equal(yearEnd('Average', 2026, [{ cycle: 'Monthly', period: 1, value: 96.27 }, { cycle: 'Monthly', period: 2, value: 96.42 }]), 96.35);
    assert.equal(yearEnd('Last', 2026, []), null);
    assert.equal(yearEnd('Last', 2026, [{ cycle: 'Quarterly', period: 4, value: 12 }, { cycle: 'Yearly', period: 1, value: 14 }]), 14);

    assert.equal(targetState(99.5, 99, 'Up'), 'met');
    assert.equal(targetState(98, 99, 'Up'), 'missed');
    assert.equal(targetState(5.7, 8, 'Down'), 'met');
    assert.equal(targetState(9, 9, 'Monitor'), null);
    assert.equal(targetState(null, 9, 'Up'), null);
    assert.equal(formatValue(99.5, 'Percentage', '%'), '99.5%');
    assert.equal(formatValue(5.7, 'Number', 'hour'), '5.7 hour');
    assert.equal(formatValue(null, 'Number', 'hour'), '—');
});

test('import warnings and skips read as sentences, keeping odd text literal', () => {
    assert.equal(warningText('number:6 سنوات'), '"6 سنوات" is not only a number; the number in it was taken.');
    assert.equal(warningText('scaled:0.99'), '"0.99" was read as a fraction and shown as a percentage.');
    assert.equal(warningText('cycle:$&'), 'Cycle "$&" is unknown; yearly is used. Set it on the KPI.');
    assert.equal(skipLabel('AlreadyInRegister'), translations.en.kpir_skip_already_in_register);
    assert.equal(skipLabel(null), '');
});

test('names and labels follow the reader language', () => {
    const kpi = { nameEn: 'Portal availability', nameAr: 'نسبة إتاحة البوابة' };
    assert.equal(nameOf(kpi), 'Portal availability');
    stored.set('itsm_lang', 'ar');
    assert.equal(nameOf(kpi), 'نسبة إتاحة البوابة');
    assert.equal(cycleLabel('Quarterly'), translations.ar.kpir_cycle_quarterly);
    assert.equal(nameOf({ nameEn: 'Only English', nameAr: '' }), 'Only English');
    stored.set('itsm_lang', 'en');
});

test('the report becomes one spreadsheet row per KPI with a column per year', () => {
    const report = {
        fromYear: 2025, toYear: 2026,
        capabilities: [{ id: 1, number: 7, nameEn: 'Self-service portal', nameAr: 'بوابة' }],
        kpis: [{ id: 3, number: 13, capabilityId: 1, nameEn: 'Portal availability', nameAr: 'إتاحة', unit: '%', resultType: 'Percentage', direction: 'Up', ownerName: 'Omar',
            values: { 2025: null, 2026: 99.5 }, targets: { 2025: null, 2026: 99 }, entered: { 2025: 0, 2026: 1 } }]
    };
    const rows = reportCsvRows(report, { generatedText: 'Generated today' });
    assert.deepEqual(rows[3], ['Capability', 'No.', 'KPI', 'Unit', 'Direction', 'Owner', '2025', '2026', 'Target 2026', 'Result']);
    assert.deepEqual(rows[4], ['Self-service portal', 13, 'Portal availability', '%', 'Higher is better', 'Omar', '', 99.5, 99, 'Met']);
});

test('only a numeric id in the address opens a KPI', () => {
    assert.equal(kpiIdFromUrl('?id=12'), 12);
    assert.equal(kpiIdFromUrl('?id=12abc'), null);
    assert.equal(kpiIdFromUrl(''), null);
});
