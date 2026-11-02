// Shared by the risk register pages: labels for the values the API sends, badges, the score formula for the
// form's live preview, import warnings and the history as text. The API sends enum names (e.g. "Treating").
import { t, getLocale } from './i18n.js';
import { escapeHtml } from './ui.js';

export const STATUSES = ['Open', 'Treating', 'Done'];
export const TREATMENTS = ['Mitigate', 'Avoid', 'Accept', 'Transfer'];
export const LEVELS = ['High', 'Medium', 'Low'];
export const FIELDS = [
    'Title', 'Threat', 'Vulnerability', 'ExistingControls', 'Confidentiality', 'Integrity', 'Availability', 'Likelihood',
    'Treatment', 'RecommendedControls', 'AnnexAControls', 'Owner', 'TargetDate', 'Status',
    'ResidualConfidentiality', 'ResidualIntegrity', 'ResidualAvailability', 'ResidualLikelihood'
];
export const WARNINGS = ['rating', 'date', 'control', 'treatment', 'status', 'residual'];

// "ExistingControls" -> "risk_field_existing_controls"
const keyOf = (prefix, value) => `${prefix}_${String(value).replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;

export const statusLabel = (value) => t(keyOf('risk_status', value), value);
export const treatmentLabel = (value) => t(keyOf('risk_treat', value), value);
export const levelLabel = (value) => t(keyOf('risk_level', value), value);
export const fieldLabel = (value) => t(keyOf('risk_field', value), value);

/** R-001, as the server names risks in reminders. */
export const reference = (number) => `R-${String(Number(number)).padStart(3, '0')}`;

/** The Excel register's formula, as RiskScore on the server: the average impact times the likelihood. */
export function riskScore(confidentiality, integrity, availability, likelihood) {
    const values = [confidentiality, integrity, availability, likelihood].map(Number);
    if (values.some(v => !Number.isInteger(v) || v < 1 || v > 5)) return null;
    const [c, i, a, l] = values;
    return Math.round((c + i + a) / 3 * l * 100) / 100;
}

/** Low up to 4, Medium up to 12, High above (decided 2 Nov 2026). */
export const riskLevel = (score) => score == null ? null : score <= 4 ? 'Low' : score <= 12 ? 'Medium' : 'High';

export const formatScore = (score) => Number(score).toLocaleString(getLocale(), { maximumFractionDigits: 2 });

const LEVEL_BADGES = { High: 'badge-danger', Medium: 'badge-warning', Low: 'badge-success' };
const STATUS_BADGES = { Open: 'badge-default', Treating: 'badge-info', Done: 'badge-success' };
const badge = (cls, text) => `<span class="badge ${cls || 'badge-default'}">${escapeHtml(text)}</span>`;

export const levelBadge = (level, score) => level
    ? badge(LEVEL_BADGES[level], score != null ? `${levelLabel(level)} · ${formatScore(score)}` : levelLabel(level))
    : '—';
export const statusBadge = (status) => badge(STATUS_BADGES[status], statusLabel(status));

/** A date the API sends as "2026-06-30", shown in the reader's language; no time zone is involved. */
export function formatDate(value) {
    if (!value) return '—';
    const [year, month, day] = String(value).split('-').map(Number);
    return new Date(year, month - 1, day).toLocaleDateString(getLocale(), { day: 'numeric', month: 'short', year: 'numeric' });
}

export function formatDateTime(value) {
    if (!value) return '—';
    return new Date(value).toLocaleString(getLocale(), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function optionsHtml(values, label, selected) {
    return values.map(v => `<option value="${escapeHtml(v)}"${String(v) === String(selected) ? ' selected' : ''}>${escapeHtml(label(v))}</option>`).join('');
}

// Fills {0}, {1}... with escaped values; a function as replacement keeps "$&" in user text literal
function fill(key, ...values) {
    return values.reduce((text, value, i) => text.replaceAll(`{${i}}`, () => escapeHtml(value ?? '')), t(key));
}

/** An import warning, sent as "kind:value", as text in the reader's language. */
export function warningText(warning) {
    const at = String(warning).indexOf(':');
    const kind = at < 0 ? warning : warning.slice(0, at);
    const value = at < 0 ? '' : warning.slice(at + 1);
    return t(keyOf('risk_warn', kind), kind).replaceAll('{0}', () => value);
}

export const skipLabel = (skip) => skip ? t(keyOf('risk_skip', skip), skip) : '';

function fieldValue(field, value, names) {
    if (value == null || value === '') return t('risk_value_empty');
    switch (field) {
        case 'Status': return statusLabel(value);
        case 'Treatment': return treatmentLabel(value);
        case 'Owner': return names[value] ?? `#${value}`;
        case 'TargetDate': return formatDate(value);
        default: return value;
    }
}

/**
 * One history entry as HTML (every value escaped) plus its note, shown separately as text.
 * names maps user ids to display names, as the risk detail sends them.
 */
export function describeHistory(entry, names) {
    const who = names[entry.userId] ?? `#${entry.userId}`;
    switch (entry.kind) {
        case 'Created': return { html: fill('risk_act_created', who), note: null };
        case 'Imported': return { html: fill('risk_act_imported', who), note: entry.note };
        case 'Updated':
            return {
                html: fill('risk_act_updated', who, fieldLabel(entry.field), fieldValue(entry.field, entry.oldValue, names), fieldValue(entry.field, entry.newValue, names)),
                note: null
            };
        case 'Approved': return { html: fill('risk_act_approved', who, treatmentLabel(entry.newValue)), note: entry.note };
        case 'ApprovalCleared': return { html: fill('risk_act_approval_cleared', who), note: null };
        case 'Reviewed': return { html: fill('risk_act_reviewed', who), note: entry.note };
        default: return { html: fill('risk_act_unknown', who, entry.kind), note: entry.note };
    }
}

/** The risk id in the page address, or null for a new risk. */
export function riskIdFromUrl(search) {
    const id = new URLSearchParams(search).get('id');
    return id && /^\d+$/.test(id) ? Number(id) : null;
}
