// The KPI page's formatting and export, kept apart from the page so it can be tested.
// The API sends counts (met and due) and working minutes; this turns them into percentages and hours.
import { t, getLocale } from './i18n.js';

export const DIMENSIONS = ['team', 'agent', 'category', 'department', 'priority'];
export const PRESETS = ['lastMonth', 'thisMonth', 'last3Months', 'thisYear'];

const pad = (n) => String(n).padStart(2, '0');
const isoDate = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;

// A preset period as yyyy-mm-dd dates, counted from `today` (a Date in the user's time zone)
export function periodPreset(name, today = new Date()) {
    const y = today.getFullYear();
    const m = today.getMonth();
    switch (name) {
        case 'thisMonth': return { from: isoDate(new Date(y, m, 1)), to: isoDate(today) };
        case 'last3Months': return { from: isoDate(new Date(y, m - 3, 1)), to: isoDate(new Date(y, m, 0)) };
        case 'thisYear': return { from: isoDate(new Date(y, 0, 1)), to: isoDate(today) };
        default: return { from: isoDate(new Date(y, m - 1, 1)), to: isoDate(new Date(y, m, 0)) };
    }
}

// Share met of due as a whole percentage; null when nothing was due
export const rate = (met, due) => due > 0 ? Math.round((met / due) * 100) : null;

export function formatPercent(met, due) {
    const value = rate(met, due);
    return value == null ? '—' : new Intl.NumberFormat(getLocale(), { style: 'percent' }).format(value / 100);
}

export const formatCount = (n) => new Intl.NumberFormat(getLocale()).format(n);

// "12 of 50"; the order of the numbers follows the language
export const nOfM = (n, m) => t('kpi_n_of_m').replaceAll('{0}', formatCount(n)).replaceAll('{1}', formatCount(m));

const formatUnit = (value, unit) => new Intl.NumberFormat(getLocale(), { style: 'unit', unit, unitDisplay: 'short' }).format(value);

// Working minutes as hours and minutes; days are left out because a working day is not 24 hours
export function formatWorkingTime(minutes) {
    if (minutes == null) return '—';
    const whole = Math.round(minutes);
    if (whole < 60) return formatUnit(whole, 'minute');
    const hours = formatUnit(Math.floor(whole / 60), 'hour');
    return whole % 60 === 0 ? hours : `${hours} ${formatUnit(whole % 60, 'minute')}`;
}

// Working minutes as hours with one decimal, for the spreadsheet
const hours = (minutes) => minutes == null ? '' : (minutes / 60).toFixed(1);

export const formatMonth = (year, month) =>
    new Intl.DateTimeFormat(getLocale(), { month: 'short', year: 'numeric' }).format(new Date(year, month - 1, 1));

// Names that came from the seed data have a translation (db_...); names people typed are shown as they are
export const dbLabel = (name) => t('db_' + String(name).toLowerCase().replaceAll(' ', '_'), name);

// A breakdown row's name; the row without an id holds the tickets with no team, agent or department
export function rowName(dimension, row) {
    if (row.id == null) return t(`kpi_none_${dimension}`, t('kpi_none'));
    return dimension === 'agent' ? row.name : dbLabel(row.name);
}

// The whole report as spreadsheet rows: a block per section, separated by an empty row
export function kpiCsvRows(report, { periodText, filtersText, generatedText }) {
    const s = report.summary;
    const workingHours = (key) => t('kpi_csv_hours').replaceAll('{0}', t(key));
    const rows = [
        [t('kpi_report_title')],
        [periodText],
        [filtersText],
        [generatedText],
        [],
        [t('kpi_csv_measure'), t('kpi_csv_value'), t('kpi_csv_out_of')],
        [t('kpi_opened'), s.opened],
        [t('kpi_solved'), s.solved],
        [t('kpi_closed'), s.closed],
        [t('kpi_response_on_time'), s.responseMet, s.responseDue],
        [t('kpi_resolution_on_time'), s.resolutionMet, s.resolutionDue],
        [workingHours('kpi_mean_own'), hours(s.meanTimeToOwnMinutes)],
        [workingHours('kpi_mean_resolve'), hours(s.meanTimeToResolveMinutes)],
        [workingHours('kpi_mean_close'), hours(s.meanTimeToCloseMinutes)],
        [t('kpi_reopen_rate'), s.reopened, s.solved],
        [t('kpi_first_time_fix'), s.firstTimeFixed, s.solved],
        [t('kpi_satisfaction'), s.satisfactionAverage ?? '', s.surveyCount],
        [],
        [t('kpi_csv_month'), t('kpi_opened'), t('kpi_solved')],
        ...report.months.map(m => [`${m.year}-${pad(m.month)}`, m.opened, m.solved]),
        [],
        [t('kpi_backlog_title'), t('kpi_csv_value')],
        [t('kpi_backlog_open'), report.backlog.open],
        [t('kpi_backlog_late'), report.backlog.late],
        [t('kpi_age_0_3'), report.backlog.days0To3],
        [t('kpi_age_4_7'), report.backlog.days4To7],
        [t('kpi_age_8_30'), report.backlog.days8To30],
        [t('kpi_age_over_30'), report.backlog.over30Days]
    ];
    for (const dimension of DIMENSIONS) {
        rows.push([], [
            t(`kpi_filter_${dimension}`), t('kpi_opened'), t('kpi_solved'), workingHours('kpi_mean_own'), workingHours('kpi_mean_resolve'),
            t('kpi_response_on_time'), t('kpi_csv_out_of'), t('kpi_resolution_on_time'), t('kpi_csv_out_of'), t('kpi_reopen_rate')
        ]);
        for (const r of report.breakdowns[dimension] || []) {
            rows.push([rowName(dimension, r), r.opened, r.solved, hours(r.meanTimeToOwnMinutes), hours(r.meanTimeToResolveMinutes),
                r.responseMet, r.responseDue, r.resolutionMet, r.resolutionDue, r.reopened]);
        }
    }
    return rows;
}
