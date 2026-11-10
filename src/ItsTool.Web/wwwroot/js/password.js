// FNRC's ISO password standard, as PasswordPolicy on the server: at least 8 characters (at most 64) with an
// upper-case letter, a lower-case letter, a digit and a symbol. The server decides; this only saves a round trip.
export const PASSWORD_MIN_LENGTH = 8;
export const PASSWORD_MAX_LENGTH = 64;

export function passwordMeetsPolicy(password) {
    const p = password || '';
    return p.length >= PASSWORD_MIN_LENGTH && p.length <= PASSWORD_MAX_LENGTH
        && /\p{Lu}/u.test(p) && /\p{Ll}/u.test(p) && /\d/.test(p) && /[^\p{L}\p{N}]/u.test(p);
}
