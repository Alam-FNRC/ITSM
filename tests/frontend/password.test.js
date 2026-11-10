import test from 'node:test';
import assert from 'node:assert/strict';
import { passwordMeetsPolicy } from '../../src/ItsTool.Web/wwwroot/js/password.js';

test('the browser check matches PasswordPolicy on the server', () => {
    for (const ok of ['Fnrc-It1', 'Passw0rd!', 'My-Own-Passphrase-2026', 'كلمة-Sr1A', 'Aa1-' + 'x'.repeat(60)]) assert.equal(passwordMeetsPolicy(ok), true, ok);
    for (const bad of ['', null, 'Fnrc-It', 'fnrc-it1', 'FNRC-IT1', 'Fnrc-Its', 'FnrcIts1', 'Aa1-' + 'x'.repeat(61)]) assert.equal(passwordMeetsPolicy(bad), false, bad);
});
