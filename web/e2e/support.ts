import { expect, type APIRequestContext, type Browser, type Page } from '@playwright/test';

export const API_HEADER = { 'X-NF-Request': '1' };

/** A name unique to this run and browser, within the 20 characters a pseudo allows. */
export function uniqueName(prefix: string, project: string): string {
  const stamp = Date.now().toString(36).slice(-5);
  return `${prefix}${project.replace(/[^a-z]/g, '').slice(0, 4)}${stamp}`.slice(0, 20);
}

/** Plays the onboarding through the interface, with the dev login. Ends on "Tonight". */
export async function signUpThroughTheInterface(page: Page, pseudo: string): Promise<void> {
  await page.goto('/');
  await expect(page).toHaveURL(/\/fr\/$/);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('button', { name: 'Suivant' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Pour te tenir, il faut te reconnaître.' })).toBeVisible();
  await page.getByPlaceholder('Nom de test').fill(pseudo);
  await page.getByRole('button', { name: 'Connexion de développement' }).click();

  await expect(page.getByRole('heading', { name: 'Ton personnage' })).toBeVisible();
  await page.getByLabel('Ton pseudo').fill(pseudo);
  await page.getByRole('button', { name: 'Bonnet' }).click();
  await page.getByRole('button', { name: 'Entrer dans le vide' }).click();

  await expect(page.getByRole('heading', { name: 'Garde le jeu à portée de main' })).toBeVisible();
  await page.getByRole('button', { name: /Plus tard|C'est parti/ }).click();
  await expect(page.getByRole('heading', { name: 'Ta première nuit.' })).toBeVisible();
}

const appearance = {
  skinTone: '#C9C3B9',
  slots: {
    coat: { itemId: 'coat-long', color: '#6A6744' },
    trousers: { itemId: 'trousers-plain', color: '#262A31' },
    boots: { itemId: 'boots-plain', color: '#16191E' },
  },
};

/** Another player, created through the API in a separate browser context. */
export async function anotherPlayer(
  browser: Browser,
  pseudo: string,
): Promise<{ request: APIRequestContext; id: string }> {
  const context = await browser.newContext();
  const request = context.request;
  await request.get(`/auth/login/dev?name=${encodeURIComponent(pseudo)}&returnUrl=/fr/`);
  const created = await request.post('/api/v1/registration', {
    headers: API_HEADER,
    data: { pseudo, appearance, locale: 'fr', inviteCode: null, turnstileToken: 'XXXX.DUMMY.TOKEN.XXXX' },
  });
  expect(created.status()).toBe(201);
  const { id } = (await created.json()) as { id: string };
  return { request, id };
}

export async function dev(request: APIRequestContext, path: string, data: object = {}): Promise<unknown> {
  const response = await request.post(path, { headers: API_HEADER, data });
  expect(response.ok(), `${path} → ${response.status()}`).toBe(true);
  return response.json();
}

export async function myId(page: Page): Promise<string> {
  const session = (await (await page.request.get('/api/v1/session')).json()) as { player: { id: string } };
  return session.player.id;
}

/** Each test starts from an empty world, real time, a season opening today (`POST /dev/reset`). */
export async function resetWorld(request: APIRequestContext): Promise<void> {
  const response = await request.post('/dev/reset', { headers: API_HEADER });
  expect(response.ok(), `reset → ${response.status()}`).toBe(true);
}
