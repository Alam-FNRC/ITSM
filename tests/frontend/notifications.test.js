import test from 'node:test';
import assert from 'node:assert/strict';

const stored = new Map();
global.localStorage = { getItem: (k) => stored.get(k) ?? null, setItem: (k, v) => stored.set(k, String(v)), removeItem: (k) => stored.delete(k) };
global.document = {
    documentElement: {},
    addEventListener: () => {},
    querySelectorAll: () => [],
    getElementById: () => null,
    createElement: () => ({ style: {}, innerHTML: '', addEventListener() {} })
};
global.window = {};

const { createNotificationItem, parseNotificationBody, getNotificationTitle, formatNotificationBody } = await import('../../src/ItsTool.Web/wwwroot/js/notifications.js');

// A requester chooses their ticket's title, and mention notifications quote it
const hostileTitle = 'Printer <img src=x onerror="alert(1)">';

test('notification text is shown as text, not markup', () => {
    const item = createNotificationItem({
        id: 7,
        type: 'comment.mention',
        title: hostileTitle,
        body: `#GEN-1 (${hostileTitle}) mentioned you.|12`,
        isRead: false,
        entityType: 'Ticket',
        entityId: 1,
        createdAt: '2026-10-28T09:00:00Z'
    });
    assert.ok(!item.innerHTML.includes('<img'), 'the title became an element');
    assert.ok(item.innerHTML.includes('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;'));
    assert.ok(!/onclick="[^"]*showInfoModal/.test(item.innerHTML), 'notification text is passed through an inline handler');
});

test('a mention keeps its comment link when the ticket title contains "|"', () => {
    const { bodyText, commentId } = parseNotificationBody({ type: 'comment.mention', body: '#GEN-1 (a|b) mentioned you.|12' });
    assert.equal(commentId, '12');
    assert.equal(bodyText, '#GEN-1 (a|b) mentioned you.');
});

test('a mention link only takes a numeric comment id', () => {
    const { commentId } = parseNotificationBody({ type: 'comment.mention', body: '#GEN-1 (x) mentioned you.|12" onmouseover="alert(1)' });
    assert.equal(commentId, null);
});

test("titles and known messages follow the reader's language", () => {
    stored.set('itsm_lang', 'ar');
    try {
        assert.equal(getNotificationTitle('comment.mention', 'You Were Mentioned'), 'تمت الإشارة إليك');
        assert.equal(formatNotificationBody('Ticket #GEN-1 was created.'), 'تم إنشاء التذكرة GEN-1.');
    } finally {
        stored.delete('itsm_lang');
    }
    assert.equal(formatNotificationBody('You were mentioned on ticket #GEN-1 (Printer offline).'), 'You were mentioned on ticket #GEN-1 (Printer offline).');
});

test('an unknown message is shown as stored, and a title with "$&" stays as typed', () => {
    assert.equal(formatNotificationBody('Bilet #GEN-1 kapandi'), 'Bilet #GEN-1 kapandi');
    assert.equal(formatNotificationBody('You were mentioned on ticket #GEN-1 (cost $& tax).'), 'You were mentioned on ticket #GEN-1 (cost $& tax).');
    assert.equal(getNotificationTitle('some.new.event', ''), 'Notification');
});

test('security notifications are translated and name the reference only', () => {
    stored.set('itsm_lang', 'ar');
    try {
        assert.notEqual(getNotificationTitle('security.reported', 'New security report'), 'New security report');
        const body = formatNotificationBody('You were added to security incident SEC-2026-0007.');
        assert.ok(body.includes('SEC-2026-0007'));
        assert.notEqual(body, 'You were added to security incident SEC-2026-0007.');
    } finally {
        stored.delete('itsm_lang');
    }
});
