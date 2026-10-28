import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// The pages keep most of their code in inline <script> blocks, which no other check parses
test('every inline script in the pages is valid JavaScript', () => {
    const root = fileURLToPath(new URL('../../src/ItsTool.Web/wwwroot/', import.meta.url));
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'page-scripts-'));
    const broken = [];
    try {
        for (const page of fs.readdirSync(root).filter(f => f.endsWith('.html'))) {
            const html = fs.readFileSync(path.join(root, page), 'utf8');
            let index = 0;
            for (const m of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)) {
                const [, attributes, code] = m;
                if (/\bsrc=/.test(attributes) || !code.trim()) continue;
                const file = path.join(dir, `${page}.${index++}.${/type="module"/.test(attributes) ? 'mjs' : 'js'}`);
                fs.writeFileSync(file, code);
                const check = spawnSync(process.execPath, ['--check', file], { encoding: 'utf8' });
                if (check.status !== 0) broken.push(`${page} script ${index}: ${check.stderr.split('\n').find(l => l.includes('Error')) ?? check.stderr}`);
            }
        }
    } finally {
        fs.rmSync(dir, { recursive: true, force: true });
    }
    assert.deepEqual(broken, []);
});
