import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Pages put ?id= into API paths. The browser resolves "../" in a URL, so an unchecked id lets a crafted link
// send the page's request (with the viewer's login) to another endpoint, e.g. survey.html?id=../users/2/roles/1?x=
test('ids from the address are accepted only when they are numbers', () => {
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const checked = String.raw`/^\d+$/.test(urlParams.get('id')`;
    const unchecked = [];
    for (const page of fs.readdirSync(root).filter(f => f.endsWith('.html'))) {
        fs.readFileSync(path.join(root, page), 'utf8').split('\n').forEach((line, i) => {
            if (line.includes(".get('id')") && !line.includes(checked)) unchecked.push(`${page}:${i + 1}`);
        });
    }
    assert.deepEqual(unchecked, []);
});
