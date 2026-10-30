// Shared by the security pages: labels for the values the API sends, badges, and the incident history as text.
// The API sends enum names (e.g. "Containment"); each has a key such as sec_status_containment in the dictionaries.
import { t, getLocale } from './i18n.js';
import { escapeHtml } from './ui.js';

export const STATUSES = ['New', 'Triage', 'Containment', 'Eradication', 'Recovery', 'Review', 'Closed'];
export const OUTCOMES = ['NotSecurity', 'Event', 'Incident'];
export const CATEGORIES = ['Phishing', 'Malware', 'UnauthorisedAccess', 'AccountCompromise', 'DataLeak', 'LostDevice', 'DenialOfService', 'PolicyViolation', 'Vulnerability', 'Other'];
export const ITEM_KINDS = ['Server', 'Workstation', 'NetworkDevice', 'Address', 'Application', 'Service', 'UserAccount', 'Other'];
export const ITEM_STATUSES = ['Open', 'Fixed', 'FalsePositive', 'AcceptedRisk'];

// "UnauthorisedAccess" -> "sec_cat_unauthorised_access"; the value itself is the fallback for an unknown name
const keyOf = (prefix, value) => `${prefix}_${String(value).replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()}`;

export const statusLabel = (value) => t(keyOf('sec_status', value), value);
export const outcomeLabel = (value) => t(keyOf('sec_outcome', value), value);
export const categoryLabel = (value) => t(keyOf('sec_cat', value), value);
export const kindLabel = (value) => t(keyOf('sec_kind', value), value);
export const itemStatusLabel = (value) => t(keyOf('sec_item', value), value);
export const progressLabel = (value) => t(keyOf('sec_progress', value), value);
export const severityLabel = (severity) => severity ? t(`sec_sev_${severity}`, String(severity)) : t('sec_sev_none');

const STATUS_BADGES = { New: 'badge-info', Triage: 'badge-warning', Containment: 'badge-danger', Eradication: 'badge-danger', Recovery: 'badge-warning', Review: 'badge-info', Closed: 'badge-success' };
const SEVERITY_BADGES = { 1: 'badge-danger', 2: 'badge-warning', 3: 'badge-info', 4: 'badge-default' };
const ITEM_BADGES = { Open: 'badge-danger', Fixed: 'badge-success', FalsePositive: 'badge-default', AcceptedRisk: 'badge-warning' };
const PROGRESS_BADGES = { Received: 'badge-info', InProgress: 'badge-warning', Closed: 'badge-success' };

const badge = (cls, text) => `<span class="badge ${cls || 'badge-default'}">${escapeHtml(text)}</span>`;
export const statusBadge = (status) => badge(STATUS_BADGES[status], statusLabel(status));
export const severityBadge = (severity) => severity ? badge(SEVERITY_BADGES[severity], severityLabel(severity)) : badge('badge-default', severityLabel(null));
export const itemStatusBadge = (status) => badge(ITEM_BADGES[status], itemStatusLabel(status));
export const progressBadge = (progress) => badge(PROGRESS_BADGES[progress], progressLabel(progress));

export function formatDateTime(value) {
    if (!value) return '—';
    return new Date(value).toLocaleString(getLocale(), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

// Fills {0}, {1}... with escaped values; a function as replacement keeps "$&" in user text literal
function fill(key, ...values) {
    return values.reduce((text, value, i) => text.replaceAll(`{${i}}`, () => escapeHtml(value ?? '')), t(key));
}

function fieldValue(field, value, names) {
    if (value == null || value === '') return t('sec_value_empty');
    switch (field) {
        case 'Category': return categoryLabel(value);
        case 'Severity': return severityLabel(Number(value));
        case 'PersonalDataInvolved': return value === 'True' ? t('secd_yes') : t('secd_no');
        case 'HandlerUserId': return names[value] ?? `#${value}`;
        case 'OccurredAt': return formatDateTime(value);
        case 'TriageOutcome': return outcomeLabel(value);
        default: return value;
    }
}

/**
 * One history entry as HTML (every value escaped) plus its note, shown separately as text.
 * names maps user ids to display names, as the incident detail sends them.
 */
export function describeAction(action, names) {
    const who = names[action.userId] ?? `#${action.userId}`;
    switch (action.kind) {
        case 'Reported':
            return { html: fill('sec_act_reported', who), note: null };
        case 'Updated':
            return {
                html: fill('sec_act_updated', who, t(keyOf('sec_field', action.field), action.field),
                    fieldValue(action.field, action.oldValue, names), fieldValue(action.field, action.newValue, names)),
                note: null
            };
        case 'Triaged':
            return { html: fill('sec_act_triaged', who, outcomeLabel(action.newValue)), note: action.note };
        case 'StatusChanged':
            return { html: fill('sec_act_status', who, statusLabel(action.oldValue), statusLabel(action.newValue)), note: action.note };
        case 'Note':
            return { html: fill('sec_act_note', who), note: action.note };
        case 'MemberAdded':
            return { html: fill('sec_act_member_added', who, names[action.newValue] ?? `#${action.newValue}`), note: null };
        case 'MemberRemoved':
            return { html: fill('sec_act_member_removed', who, names[action.oldValue] ?? `#${action.oldValue}`), note: null };
        case 'AffectedItemAdded':
            return { html: fill('sec_act_item_added', who, kindLabel(action.newValue), action.note), note: null };
        case 'AffectedItemUpdated':
            return { html: fill('sec_act_item_updated', who, action.note, itemStatusLabel(action.newValue)), note: null };
        case 'ReviewSaved':
            return { html: fill('sec_act_review', who), note: null };
        default:
            return { html: fill('sec_act_unknown', who, action.kind), note: action.note };
    }
}

// Ids come from the page address; anything but a plain number would let "../" reach another endpoint
export function incidentIdFromUrl(search) {
    const id = new URLSearchParams(search).get('id') ?? '';
    return /^\d+$/.test(id) ? Number(id) : null;
}

export function optionsHtml(values, label, selected) {
    return values.map(v => `<option value="${escapeHtml(v)}"${v === selected ? ' selected' : ''}>${escapeHtml(label(v))}</option>`).join('');
}
