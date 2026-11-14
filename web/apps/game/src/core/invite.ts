/**
 * Invitation links: `https://nobodyfalls.com/fr/i/ABCD234567`. Following one keeps the
 * code for the sign-up; joining makes you friends with the inviter (GAME_DESIGN §4).
 */
const KEY = 'nf-invite';

export function inviteLink(code: string, locale: string, origin = window.location.origin): string {
  return `${origin}/${locale === 'en' ? 'en' : 'fr'}/i/${encodeURIComponent(code)}`;
}

export function rememberInvite(code: string): void {
  try {
    sessionStorage.setItem(KEY, code);
  } catch {
    // Private browsing may refuse storage: the invitation is simply lost.
  }
}

export function pendingInvite(): string | null {
  try {
    return sessionStorage.getItem(KEY);
  } catch {
    return null;
  }
}

export function forgetInvite(): void {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // Nothing to forget.
  }
}
