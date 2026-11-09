// Shared by the IT KPI pages: labels for the values the API sends, period names, the year-end rule (as KpiPeriods
// on the server), met or missed against a target, import warnings and the report as spreadsheet rows.
import { t, getLocale, getCurrentLanguage } from './i18n.js';
import { escapeHtml } from './ui.js';

export const CYCLES = ['Yearly', 'HalfYearly', 'Quarterly', 'Monthly'];
export const RESULT_TYPES = ['Number', 'Percentage'];
export const DIRECTIONS = ['Up', 'Down', 'Monitor'];
export const YEAR_ENDS = ['Last', 'Average'];
export const SOURCES = ['Manual', 'TicketResolutionTime'];
export const WARNINGS = ['number', 'cycle', 'direction', 'scaled', 'name'];
export const SKIPS = ['AlreadyInRegister', 'NoName'];

// "HalfYearly" -> "kpir_cycle_half_yearly"
const keyOf = (prefix, value) => `${prefix}_${String(value).replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;

export const cycleLabel = (value) => t(keyOf('kpir_cycle', value), value);
export const resultLabel = (value) => t(keyOf('kpir_result', value), value);
export const directionLabel = (value) => t(keyOf('kpir_direction', value), value);
export const yearEndLabel = (value) => t(keyOf('kpir_year_end', value), value);
export const sourceLabel = (value) => t(keyOf('kpir_source', value), value);
export const skipLabel = (skip) => skip ? t(keyOf('kpir_skip', skip), skip) : '';

/** The KPI's or capability's name in the reader's language, falling back to the other one. */
export const nameOf = (item) => getCurrentLanguage() === 'ar' ? (item.nameAr || item.nameEn) : (item.nameEn || item.nameAr);

export const directionSymbol = (direction) => direction === 'Down' ? '▼' : direction === 'Monitor' ? '◆' : '▲';

export const periodCount = (cycle) => ({ Monthly: 12, Quarterly: 4, HalfYearly: 2 })[cycle] ?? 1;

/** The first day of a period, as a local date. */
export const periodStart = (cycle, year, period) => new Date(year, (period - 1) * (12 / periodCount(cycle)), 1);

/** The last day of a period, as a local date. */
export const periodEnd = (cycle, year, period) => new Date(year, period * (12 / periodCount(cycle)), 0);

export const periodStarted = (cycle, year, period, today = new Date()) => periodStart(cycle, year, period) <= today;

export function periodLabel(cycle, year, period) {
    switch (cycle) {
        case 'Monthly': return new Intl.DateTimeFormat(getLocale(), { month: 'short', year: 'numeric' }).format(periodStart(cycle, year, period));
        case 'Quarterly': return `Q${period} ${year}`;
        case 'HalfYearly': return `${t('kpir_half').replaceAll('{0}', String(period))} ${year}`;
        default: return String(year);
    }
}

/** The year's figure as the server works it out: the latest period's value, or the average rounded to 2 decimals. */
export function yearEnd(rule, year, values) {
    if (!values.length) return null;
    if (rule === 'Average') return Math.round((values.reduce((sum, v) => sum + Number(v.value), 0) / values.length) * 100) / 100;
    return Number(values.slice().sort((a, b) => periodEnd(b.cycle, year, b.period) - periodEnd(a.cycle, year, a.period) || periodCount(a.cycle) - periodCount(b.cycle))[0].value);
}

export function formatValue(value, resultType, unit) {
    if (value == null) return '—';
    const text = Number(value).toLocaleString(getLocale(), { maximumFractionDigits: 2 });
    if (resultType === 'Percentage') return `${text}%`;
    return unit && unit !== '%' ? `${text} ${unit}` : text;
}

/** "met", "missed" or null (no value, no target, or a monitored KPI). */
export function targetState(value, target, direction) {
    if (value == null || target == null || direction === 'Monitor') return null;
    const met = direction === 'Down' ? Number(value) <= Number(target) : Number(value) >= Number(target);
    return met ? 'met' : 'missed';
}

export function targetBadge(value, target, direction) {
    const state = targetState(value, target, direction);
    if (!state) return target == null ? `<span class="text-muted" style="font-size: 12px;">${escapeHtml(t('kpir_no_target'))}</span>` : '';
    return `<span class="badge ${state === 'met' ? 'badge-success' : 'badge-danger'}">${escapeHtml(t(`kpir_${state}`))}</span>`;
}

export function formatDateTime(value) {
    if (!value) return '—';
    return new Date(value).toLocaleString(getLocale(), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function optionsHtml(values, label, selected) {
    return values.map(v => `<option value="${escapeHtml(v)}"${String(v) === String(selected) ? ' selected' : ''}>${escapeHtml(label(v))}</option>`).join('');
}

/** An import warning, sent as "kind:value", as text in the reader's language; user text stays literal. */
export function warningText(warning) {
    const at = String(warning).indexOf(':');
    const kind = at < 0 ? warning : warning.slice(0, at);
    const value = at < 0 ? '' : warning.slice(at + 1);
    return t(keyOf('kpir_warn', kind), kind).replaceAll('{0}', () => value);
}

/** The KPI id in the page address, or null for a new KPI. */
export function kpiIdFromUrl(search) {
    const id = new URLSearchParams(search).get('id');
    return id && /^\d+$/.test(id) ? Number(id) : null;
}

/** The year-end report as spreadsheet rows: a header, then one row per KPI under its capability. */
export function reportCsvRows(report, { generatedText }) {
    const years = [];
    for (let y = report.fromYear; y <= report.toYear; y++) years.push(y);
    const rows = [
        [t('kpir_report_title')],
        [generatedText],
        [],
        [t('kpir_col_capability'), t('kpir_col_no'), t('kpir_col_kpi'), t('kpir_col_unit'), t('kpir_col_direction'), t('kpir_col_owner'), ...years.map(String), `${t('kpir_col_target')} ${report.toYear}`, t('kpir_col_result')]
    ];
    for (const capability of report.capabilities) {
        for (const kpi of report.kpis.filter(k => k.capabilityId === capability.id)) {
            const value = kpi.values[report.toYear];
            const target = kpi.targets[report.toYear];
            const state = targetState(value, target, kpi.direction);
            rows.push([
                nameOf(capability), kpi.number, nameOf(kpi), kpi.resultType === 'Percentage' ? '%' : (kpi.unit ?? ''), directionLabel(kpi.direction), kpi.ownerName ?? '',
                ...years.map(y => kpi.values[y] ?? ''), target ?? '', state ? t(`kpir_${state}`) : ''
            ]);
        }
    }
    return rows;
}
