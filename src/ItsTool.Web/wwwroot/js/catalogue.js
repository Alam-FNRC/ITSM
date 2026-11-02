// The service catalogue on the ticket pages: categories grouped under their service, services under their family.
// /api/lookup sends the active services (ordered by family, order and name) and every category with its serviceId.
// It does not import ui.js: the ticket page loads ui.js with a version query, and a second copy would replace window.ui.
import { t } from './i18n.js';

const escapeHtml = (text) => String(text ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');

// Seeded names have a translation (db_...); names people typed are shown as they are
const label = (name) => t('db_' + String(name).toLowerCase().replaceAll(' ', '_'), name);

const serviceLabel = (service) => service.family ? `${service.family} › ${service.name}` : service.name;

// [{ label, service, categories }] in catalogue order; categories without an active service come last under "Other".
// With no services at all there is one group without a label, so the list looks as it did before the catalogue.
export function categoryGroups(categories = [], services = []) {
    const byName = (a, b) => label(a.name).localeCompare(label(b.name));
    const known = new Set(services.map(s => s.id));
    const groups = services
        .map(service => ({ label: serviceLabel(service), service, categories: categories.filter(c => c.serviceId === service.id).sort(byName) }))
        .filter(g => g.categories.length > 0);
    const rest = categories.filter(c => !known.has(c.serviceId)).sort(byName);
    if (rest.length > 0) groups.push({ label: groups.length > 0 ? t('svc_other') : null, service: null, categories: rest });
    return groups;
}

// <option>s for a category <select>, in <optgroup>s per service
export function categoryOptionsHtml(categories, services, selectedId = null) {
    const option = (c) => `<option value="${Number(c.id)}"${c.id === selectedId ? ' selected' : ''}>${escapeHtml(label(c.name))}</option>`;
    return categoryGroups(categories, services)
        .map(g => g.label == null ? g.categories.map(option).join('') : `<optgroup label="${escapeHtml(g.label)}">${g.categories.map(option).join('')}</optgroup>`)
        .join('');
}

const findCategory = (categoryId, categories = []) => categories.find(c => c.id === Number(categoryId));

// "Email › Mailbox full", or just the category when it has no service
export function categoryPath(categoryId, categories = [], services = []) {
    const category = findCategory(categoryId, categories);
    if (!category) return '-';
    const service = services.find(s => s.id === category.serviceId);
    return service ? `${service.name} › ${label(category.name)}` : label(category.name);
}

// The service a category belongs to, for the hint under the ticket form's category
export function serviceOf(categoryId, categories = [], services = []) {
    const category = findCategory(categoryId, categories);
    return category ? services.find(s => s.id === category.serviceId) ?? null : null;
}

// The ticket type a category asks for, e.g. Service Request for "New laptop"; null when it leaves the choice open
export const defaultTypeFor = (categoryId, categories = []) => findCategory(categoryId, categories)?.defaultTypeId ?? null;
