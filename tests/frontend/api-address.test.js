import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

test('the pages reach the API on their own origin, not a fixed host', () => {
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const files = [
        ...fs.readdirSync(root).filter(f => f.endsWith('.html')),
        ...fs.readdirSync(path.join(root, 'js')).filter(f => f.endsWith('.js')).map(f => path.join('js', f))
    ];
    const fixedHosts = [];
    for (const file of files) {
        const lines = fs.readFileSync(path.join(root, file), 'utf8').split('\n');
        lines.forEach((line, i) => {
            if (/https?:\/\/(localhost|127\.0\.0\.1)[:/]/.test(line)) fixedHosts.push(`${file}:${i + 1}`);
        });
    }
    assert.deepEqual(fixedHosts, []);
});
