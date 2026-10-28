import { t, getLocale } from './i18n.js';
import { escapeHtml } from './ui.js';

let hubConnection = null;

function setupNotificationActionButtons(notifList) {
    if (!notifList || document.getElementById('btnDeleteAllNotifs')) return;

    const oldMarkReadBtn = notifList.querySelector('[onclick="markAllAsRead(event)"]');
    if (oldMarkReadBtn?.parentElement) {
        const hr = oldMarkReadBtn.parentElement.previousElementSibling;
        if (hr?.innerHTML?.includes('hr')) hr.remove();
        oldMarkReadBtn.parentElement.remove();
    }

    const header = notifList.querySelector('.dropdown-header');
    if (header?.parentElement) {
        const actionsLi = document.createElement('li');
        actionsLi.id = 'notifActionsContainer';
        actionsLi.innerHTML = `
            <div style="display: flex; justify-content: space-between; padding: 4px 12px 12px 12px; border-bottom: 1px solid var(--border); margin-bottom: 8px;">
                <button type="button" onclick="markAllAsRead(event)" style="background: none; border: none; color: var(--primary); font-size: 13px; font-weight: 500; cursor: pointer; display: flex; align-items: center; gap: 4px; padding: 0;">
                    <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>
                    ${t('topbar_mark_all_read')}
                </button>
                <button type="button" id="btnDeleteAllNotifs" onclick="deleteAllNotifications(event)" style="background: none; border: none; color: var(--danger); font-size: 13px; font-weight: 500; cursor: pointer; display: flex; align-items: center; gap: 4px; padding: 0;">
                    <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M16 9v10H8V9h8m-1.5-6h-5l-1 1H5v2h14V4h-3.5l-1-1zM18 7H6v12c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7z"/></svg>
                    ${t('topbar_delete_all')}
                </button>
            </div>
        `;
        header.parentElement.after(actionsLi);
    }
}

export async function initNotifications() {
    const token = window.api ? window.api.token : localStorage.getItem('jwt_token');
    if (!token) return;

    function getUserIdFromToken() {
        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            return payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] || payload.nameid || payload.sub;
        } catch { return null; }
    }

    const userId = getUserIdFromToken();
    if (!userId) return;

    // Fetch initial notifications
    await loadNotifications();

    // Setup SignalR
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/notification", {
            accessTokenFactory: () => token
        })
        .withAutomaticReconnect()
        // The client logs the connection URL, which carries the token, at Information level
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    hubConnection.on("ReceiveNotification", (payload) => {
        // payload = { type, title, body, entityId, priority, createdAt }
        showToastNotification(payload);
        loadNotifications(); // reload list
    });

    setupNotificationActionButtons(document.getElementById('notificationList'));
    
    try {
        await hubConnection.start();
        console.log("SignalR Connected for notifications.");
    } catch (err) {
        console.error("SignalR Connection Error: ", err);
    }
}

// Dropdown toggle logic runs unconditionally
if (typeof document !== 'undefined') {
    document.addEventListener('click', (e) => {
        const toggle = e.target.closest('#bellDropdown');
        const menu = document.getElementById('notificationList');
        if (toggle) {
            menu.classList.toggle('show');
        } else if (menu && !menu.contains(e.target)) {
            menu.classList.remove('show');
        }
    });
}

async function loadNotifications() {
    try {
        const res = await window.api.request(`/notifications`);
        const data = res; // window.api.request parses json
        
        const container = document.getElementById('notificationItems');
        const badge = document.getElementById('notificationBadge');
        if (!container || !badge) return;

        container.innerHTML = '';
        let unreadCount = 0;

        if (data.length === 0) {
            container.innerHTML = `<div class="p-3 text-center text-muted"><small>${escapeHtml(t('notif_none'))}</small></div>`;
        } else {
            data.forEach(n => {
                if (!n.isRead) unreadCount++;
                container.appendChild(createNotificationItem(n));
            });
        }

        if (unreadCount > 0) {
            badge.style.display = 'block';
            badge.innerText = unreadCount > 9 ? '9+' : unreadCount;
        } else {
            badge.style.display = 'none';
        }

    } catch (err) {
        console.error("Error loading notifications:", err);
    }
}


window.deleteAllNotifications = async function(e) {
    e.preventDefault();
    if (!confirm(t('notif_confirm_delete'))) return;
    try {
        await window.api.request(`/notifications/all`, { method: 'DELETE' });
        loadNotifications();
    } catch(err) {
        console.error(err);
    }
}

window.markAllAsRead = async function(e) {
    e.preventDefault();
    try {
        await window.api.request(`/notifications/read-all`, { method: 'POST' });
        loadNotifications();
    } catch(err) {
        console.error(err);
    }
}

function getOrCreateToastContainer() {
    let toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toast-container';
        toastContainer.style.position = 'fixed';
        toastContainer.style.top = '20px';
        toastContainer.style.insetInlineEnd = '20px';
        toastContainer.style.zIndex = '9999';
        document.body.appendChild(toastContainer);
    }
    return toastContainer;
}

// Titles by event type, so they follow the reader's language; the server's own title is the fallback
const EVENT_TITLE_KEYS = {
    'ticket.assigned': 'notif_title_assigned',
    'ticket.created': 'notif_title_created',
    'ticket.transferred': 'notif_title_transferred',
    'ticket.comment.added': 'notif_title_comment',
    'comment.added': 'notif_title_comment',
    'comment.mention': 'notif_title_mention',
    'status.changed': 'notif_title_status',
    'ticket.reopened': 'notif_title_reopened',
    'ticket.resolved': 'notif_title_resolved',
    'ticket.closed': 'notif_title_closed',
    'ticket.closed.survey': 'notif_title_survey',
    'sla.breached': 'notif_title_sla_breached',
    'sla.breach': 'notif_title_sla_breached',
    'sla.risk': 'notif_title_sla_risk',
    'sla.warning': 'notif_title_sla_warning',
    'critical.unassigned': 'notif_title_critical_unassigned',
    'survey.low': 'notif_title_survey_low',
    'kb.suggested': 'notif_title_kb_suggested',
    'kb.reviewed': 'notif_title_kb_reviewed'
};

// The messages the server writes (in English) for known events; anything else is shown as stored
const BODY_PATTERNS = [
    [/^Ticket #(\S+) was created\.$/, 'notif_body_created'],
    [/^Ticket #(\S+) was assigned to you or your team\.$/, 'notif_body_assigned'],
    [/^A new comment was added to ticket #(\S+)\.$/, 'notif_body_comment'],
    [/^You were mentioned on ticket #(\S+) \(([\s\S]*)\)\.$/, 'notif_body_mention'],
    [/^Ticket #(\S+) was closed\. Please fill in the satisfaction survey\.$/, 'notif_body_survey'],
    [/^Ticket #(\S+)$/, 'notif_body_ticket'],
    [/^First response SLA breached\.$/, 'notif_body_sla_fr_breached'],
    [/^Resolution SLA breached\.$/, 'notif_body_sla_res_breached'],
    [/^First response SLA is about to be breached\. \(([\s\S]*)\)$/, 'notif_body_sla_fr_warning'],
    [/^Resolution SLA is about to be breached\. \(([\s\S]*)\)$/, 'notif_body_sla_res_warning'],
    [/^A new article '([\s\S]*)' requires your review\.$/, 'notif_body_kb_suggested'],
    [/^Your article '([\s\S]*)' has been published\.$/, 'notif_body_kb_published'],
    [/^Your article '([\s\S]*)' has been marked for revision\.$/, 'notif_body_kb_revision'],
    [/^Your article '([\s\S]*)' has been rejected\.$/, 'notif_body_kb_rejected']
];

export function getNotificationTitle(type, defaultTitle) {
    const clean = (defaultTitle || '').replaceAll('Event ', '').trim();
    const key = EVENT_TITLE_KEYS[(type || '').toLowerCase()] ?? EVENT_TITLE_KEYS[clean.toLowerCase()];
    if (key) return t(key);
    return clean || t('notif_title_default');
}

export function formatNotificationBody(bodyText) {
    const text = (bodyText || '').trim();
    for (const [pattern, key] of BODY_PATTERNS) {
        const match = text.match(pattern);
        // A function as replacement, so a "$&" in a ticket title is not read as a pattern
        if (match) return match.slice(1).reduce((s, value, i) => s.replaceAll(`{${i}}`, () => value), t(key));
    }
    return text;
}

function getToastIconAndTitle(payload) {
    const displayTitle = getNotificationTitle(payload.type, payload.title);
    let iconSvg = '';
    
    if (payload.type === 'comment.mention') { 
        iconSvg = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10h5v-2h-5c-4.34 0-8-3.66-8-8s3.66-8 8-8 8 3.66 8 8v1.43c0 .79-.71 1.57-1.5 1.57s-1.5-.78-1.5-1.57V12c0-2.76-2.24-5-5-5s-5 2.24-5 5 2.24 5 5 5c1.38 0 2.64-.56 3.54-1.47.65.89 1.77 1.47 2.96 1.47 1.97 0 3.5-1.6 3.5-3.57V12c0-5.52-4.48-10-10-10zm0 13c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3z"/></svg>';
    } else if (payload.type && (payload.type.startsWith('sla.') || payload.type === 'sla.breach')) {
        iconSvg = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"/></svg>';
    } else if (payload.type === 'ticket.assigned' || payload.type === 'ticket.transferred') {
        iconSvg = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg>';
    } else if (payload.type === 'ticket.comment.added' || payload.type === 'comment.added') {
        iconSvg = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M20 2H4c-1.1 0-1.99.9-1.99 2L2 22l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 9h12v2H6V9zm8 5H6v-2h8v2zm4-6H6V6h12v2z"/></svg>';
    } else {
        iconSvg = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 22c1.1 0 2-.9 2-2h-4c0 1.1.89 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z"/></svg>';
    }
    return { displayTitle, iconSvg };
}

function showToastNotification(payload) {
    const toastContainer = getOrCreateToastContainer();
    const toastId = 'toast_' + Date.now();
    const borderCol = payload.priority === 'High' ? 'var(--danger)' : 'var(--primary)';
    const iconColor = payload.priority === 'High' ? 'var(--danger)' : 'var(--primary)';
    
    const { bodyText, commentId } = parseNotificationBody(payload);
    const { displayTitle, iconSvg } = getToastIconAndTitle(payload);

    let linkUrl = '#';
    if (payload.entityId) {
        if (payload.entityType === 'Ticket') {
            linkUrl = `/ticket-detail.html?id=${payload.entityId}`;
            if (commentId) linkUrl += `&highlight=true&commentId=${commentId}`;
        } else if (payload.entityType === 'KnowledgeArticle') {
            linkUrl = `/kb-article.html?id=${payload.entityId}`;
        }
    }

    const toastHtml = `
        <div id="${toastId}" role="alert" aria-live="assertive" aria-atomic="true" style="display: flex; flex-direction: column; margin-bottom: 10px; width: 280px; border: none; border-inline-start: 3px solid ${borderCol}; border-radius: 4px; box-shadow: 0 4px 12px rgba(0,0,0,0.15); background: var(--bg-surface); opacity: 1; transition: opacity 0.4s ease;">
            <div style="display: flex; align-items: center; padding: 10px 12px 4px 12px;">
                <div style="color: ${iconColor}; margin-inline-end: 8px; display: flex; align-items: center;">
                    ${iconSvg}
                </div>
                <strong style="font-size: 13px; color: var(--text-main); font-weight: 600; flex: 1;">${escapeHtml(displayTitle)}</strong>
                <button type="button" onclick="document.getElementById('${toastId}').remove()" aria-label="${escapeHtml(t('btn_close'))}" style="font-size: 14px; margin-inline-start: 8px; background: transparent; border: none; cursor: pointer; color: var(--text-muted); opacity: 0.6; padding: 0 4px; line-height: 1;">&times;</button>
            </div>
            <div style="padding: 4px 12px 12px 12px; font-size: 12px; color: var(--text-muted); line-height: 1.4;">
                <div style="margin-bottom: 8px;">${escapeHtml(bodyText)}</div>
                <div style="text-align: end;">
                    <a href="${escapeHtml(linkUrl)}" class="btn btn-sm btn-outline-primary" style="display: inline-block; font-size: 11px; padding: 2px 8px; border-radius: 4px; text-decoration: none; cursor: pointer;">${escapeHtml(t('notif_go_ticket'))}</a>
                </div>
            </div>
        </div>
    `;
    
    toastContainer.insertAdjacentHTML('beforeend', toastHtml);
    setTimeout(() => {
        const el = document.getElementById(toastId);
        if (el) {
            el.style.opacity = '0';
            el.style.transition = 'opacity 0.4s ease';
            setTimeout(() => el.remove(), 400);
        }
    }, 6000);
}

function getNotificationLink(n, commentId) {
    if (n.entityType === 'Ticket' && n.entityId) {
        return `/ticket-detail.html?id=${n.entityId}` + (commentId ? `&highlight=true&commentId=${commentId}` : '');
    }
    if (n.entityType === 'KnowledgeArticle' && n.entityId) {
        return `/kb-article.html?id=${n.entityId}`;
    }
    return '#';
}

// Mention notifications end in "|<comment id>"; the text before it can itself contain "|" (it includes the ticket title)
export function parseNotificationBody(n) {
    let bodyText = n.body || '';
    let commentId = null;
    const separator = bodyText.lastIndexOf('|');
    if (n.type === 'comment.mention' && separator >= 0) {
        const id = bodyText.slice(separator + 1).trim();
        bodyText = bodyText.slice(0, separator);
        if (/^\d+$/.test(id)) commentId = id;
    }
    bodyText = formatNotificationBody(bodyText);
    return { bodyText, commentId };
}

export function createNotificationItem(n) {
    const priorityHtml = n.priority === 'High' ? `<span class="badge" style="background:var(--danger);font-size:10px;">${escapeHtml(t('notif_priority_high'))}</span>` : '';
    const bgClass = n.isRead ? 'background: var(--bg-hover); opacity: 0.8;' : 'background: var(--bg-surface);';
    
    const { bodyText, commentId } = parseNotificationBody(n);
    const linkUrl = getNotificationLink(n, commentId);
    const displayTitle = getNotificationTitle(n.type, n.title);
    
    const readIcon = n.isRead 
        ? `<div class="already-read-icon" style="display:flex; align-items:center; justify-content:center; width:24px; height:24px; cursor:default;"><svg viewBox="0 0 24 24" width="18" height="18" style="fill:var(--success);" title="${escapeHtml(t('notif_read'))}"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg></div>`
        : `<button class="mark-read-btn" style="background:none; border:none; padding:0; display:flex; align-items:center; justify-content:center; width:24px; height:24px; cursor:pointer;" title="${escapeHtml(t('notif_mark_read'))}"><svg viewBox="0 0 24 24" width="18" height="18" style="fill:var(--text-muted); opacity: 0.5; transition: opacity 0.2s;" onmouseover="this.style.opacity=1" onmouseout="this.style.opacity=0.5"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg></button>`;
    
    const item = document.createElement('div');
    item.className = `dropdown-item py-2 px-3 border-bottom`;
    item.style = `white-space: normal; position: relative; ${bgClass} cursor: default;`;
    item.innerHTML = `
        <div style="display: flex; gap: 12px; align-items: flex-start;">
            <div style="flex: 1;">
                <div class="d-flex justify-content-between align-items-center mb-1">
                    <div class="d-flex align-items-center gap-2">
                        <strong style="font-size: 14px;">${escapeHtml(displayTitle)}</strong>
                        ${priorityHtml}
                    </div>
                </div>
                <div style="font-size: 13px; color: var(--text-muted); line-height: 1.4; margin-bottom: 8px;">${escapeHtml(bodyText)}</div>
                
                <div class="d-flex justify-content-between align-items-center">
                    <div style="font-size: 11px; color: #999;">${new Date(n.createdAt).toLocaleString(getLocale())}</div>
                    <div class="d-flex gap-sm">
                        <button type="button" class="btn btn-outline-secondary notification-detail-btn" style="padding: 2px 8px; font-size: 11px; border-radius: 4px;">${escapeHtml(t('notif_detail_btn'))}</button>
                        <a href="${escapeHtml(linkUrl)}" class="btn btn-primary" style="padding: 2px 8px; font-size: 11px; border-radius: 4px; color: white; text-decoration: none;" onclick="event.stopPropagation(); if (window.api && !${Boolean(n.isRead)}) { window.api.request('/notifications/${n.id}/read', { method: 'POST' }).catch(()=>{}); }">${escapeHtml(t('notif_open'))}</a>
                    </div>
                </div>
            </div>
            <div style="display: flex; flex-direction: column; gap: 8px; align-items: center; margin-top: 2px;">
                ${readIcon}
                <button class="delete-notification-btn" data-id="${n.id}" style="background:none; border:none; color:var(--danger); padding:0; display:flex; align-items:center; justify-content:center; width:24px; height:24px; cursor:pointer; opacity: 0.7; transition: opacity 0.2s;" onmouseover="this.style.opacity=1" onmouseout="this.style.opacity=0.7" title="${escapeHtml(t('notif_delete'))}">
                    <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M16 9v10H8V9h8m-1.5-6h-5l-1 1H5v2h14V4h-3.5l-1-1zM18 7H6v12c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7z"/></svg>
                </button>
            </div>
        </div>
    `;
    
    item.addEventListener('click', async (e) => {
        if (e.target.closest('.delete-notification-btn')) {
            e.preventDefault();
            e.stopPropagation();
            try {
                await window.api.request(`/notifications/${n.id}`, { method: 'DELETE' });
                loadNotifications();
            } catch(err) { console.error(err); }
            return;
        }
        if (e.target.closest('.mark-read-btn') || e.target.closest('.already-read-icon')) {
            e.preventDefault();
            e.stopPropagation();
            if (!n.isRead) {
                try {
                    await window.api.request(`/notifications/${n.id}/read`, { method: 'POST' });
                    loadNotifications();
                } catch(err) { console.error(err); }
            }
            return;
        }
        if (e.target.closest('.notification-detail-btn')) {
            e.preventDefault();
            e.stopPropagation();
            window.showInfoModal(t('notif_detail'), bodyText);
            return;
        }
        if (e.target.closest('.btn')) {
            return; // handled by inline handlers
        }
        e.preventDefault();
        if (!n.isRead) {
            try {
                await window.api.request(`/notifications/${n.id}/read`, { method: 'POST' });
            } catch(err) { console.warn('Error loading notifications', err); }
        }
        if (linkUrl !== '#') {
            window.location.href = linkUrl;
        } else {
            loadNotifications();
        }
    });
    
    return item;
}
