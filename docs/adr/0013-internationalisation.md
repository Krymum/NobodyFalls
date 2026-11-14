# 13. Internationalisation

- Status: accepted
- Date: 2026-11-14

## Context

French and English from day one, no hard-coded text, plurals, numbers and dates
through the i18n library, a detected, changeable and remembered language, and
`/fr` and `/en` URLs (GAME_DESIGN §11). Notifications are written by the server.

## Decision

- **Front end**: i18next with two bundled JSON catalogues (`fr.json`, `en.json`).
  Plurals use i18next suffixes (`_one`, `_other`, `_zero`), numbers and dates go
  through `Intl`. Both catalogues ship in the bundle: switching language never
  waits on the network.
- **Errors**: the API returns stable codes (`hand.none_left`…); the interface
  shows `errors.<code>`. A test lists every code the API can return and requires
  a text for it.
- **Parity**: a test requires both catalogues to have the same keys and the same
  placeholders for every text.
- **Language choice**: the URL decides. `/` redirects using, in order, the
  remembered choice (`nf-lang` cookie), the browser's languages, then French; the
  server and the client apply the same rule. Changing the language in the profile
  saves it on the account (for notifications), sets the cookie and moves to the
  same screen under the other prefix.
- **Server texts** (push notifications): `.resx` resources, French neutral and an
  English satellite, rendered in the recipient's saved language (ADR 0009).
- French texts avoid gendered forms where a neutral one exists ("Tu as glissé dans
  le vide" rather than "Tu es tombé·e").

## Consequences

- Adding a language means one JSON catalogue, one `.resx` file and the locale in
  the supported list; the parity tests show what is missing.
