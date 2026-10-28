import test from 'node:test';
import assert from 'node:assert/strict';

global.localStorage = { getItem: () => null, setItem: () => {}, removeItem: () => {} };
global.document = {
    documentElement: {},
    addEventListener: () => {},
    querySelectorAll: () => [],
    getElementById: () => null,
    createElement: () => ({ style: {}, innerHTML: '', addEventListener() {} })
};
global.window = {};

const { createNotificationItem, parseNotificationBody } = await import('../../src/ItsTool.Web/wwwroot/js/notifications.js');

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
