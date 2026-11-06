import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Staging runs the app as an IIS sub-application (https://staging.fnrc.gov.ae/itsm/), so a link starting with /
// would leave it for the parent site. Every page sits in the top folder, so links name the page only.
const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
const files = [
    ...fs.readdirSync(root).filter(f => f.endsWith('.html')),
    ...fs.readdirSync(path.join(root, 'js')).filter(f => f.endsWith('.js') && !f.includes('.min.')).map(f => path.join('js', f))
];

test('pages, the API and the notification hub are reached by relative URLs', () => {
    const rootRelative = /(["'`(=])(\/(?:[a-z][a-z0-9-]*\.html|api\b|hubs\/)[^"'`)\s]*)/g;
    const found = [];
    for (const file of files) {
        const text = fs.readFileSync(path.join(root, file), 'utf8');
        for (const m of text.matchAll(rootRelative)) found.push(`${file}: ${m[2]}`);
    }
    assert.deepEqual(found, []);
});
