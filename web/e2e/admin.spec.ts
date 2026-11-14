import { expect, test, type Page } from '@playwright/test';
import { anotherPlayer, dev, resetWorld, signUpByApi, uniqueName } from './support.ts';

/** The code the member's authenticator would show now (dev tools only). */
async function currentCode(page: Page, playerId: string): Promise<string> {
  const response = await page.request.get(`/dev/totp/${playerId}`);
  expect(response.ok()).toBe(true);
  return ((await response.json()) as { code: string }).code;
}

/** On phones the sidebar is a drawer behind the "Menu" button. */
async function openSection(page: Page, name: string): Promise<void> {
  const menu = page.getByRole('button', { name: 'Menu' });
  if (await menu.isVisible()) {
    await menu.click();
  }
  await page.getByRole('navigation').getByRole('link', { name }).click();
}

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('signed-out visitors are sent to the game first', async ({ page }) => {
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: "Connecte-toi d'abord au jeu." })).toBeVisible();
});

test('players without a role are turned away', async ({ page }, info) => {
  await signUpByApi(page.request, uniqueName('nobody', info.project.name));
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: "Accès réservé à l'équipe." })).toBeVisible();
});

test('an admin sets up two-factor sign-in, adds a moderator and finds every action in the audit', async ({
  page,
  browser,
}, info) => {
  const boss = uniqueName('boss', info.project.name);
  const helper = uniqueName('help', info.project.name);
  await anotherPlayer(browser, helper);
  const bossId = await signUpByApi(page.request, boss);
  await dev(page.request, '/dev/staff/me', { roles: ['admin'] });

  // First visit: enrolment.
  await page.goto('/admin/');
  await expect(page.getByRole('heading', { name: 'Protège ton accès' })).toBeVisible();
  await page.getByRole('button', { name: 'Configurer mon application' }).click();
  await expect(page.getByRole('img', { name: /Code QR/ })).toBeVisible();
  await expect(page.getByTestId('totp-secret')).toHaveText(/^[A-Z2-7 ]{32,}$/);
  await page.getByLabel('Code à 6 chiffres').fill(await currentCode(page, bossId));
  await page.getByRole('button', { name: 'Confirmer' }).click();

  await expect(page.getByRole('heading', { name: 'Tes codes de secours' })).toBeVisible();
  await expect(page.getByTestId('recovery-codes').getByRole('listitem')).toHaveCount(10);
  await page.getByRole('button', { name: 'Je les ai mis en lieu sûr' }).click();
  await expect(page.getByRole('heading', { name: `Bonjour ${boss}.` })).toBeVisible();

  // Team: add a moderator.
  await openSection(page, 'Équipe');
  const addForm = page.locator('form', { has: page.getByRole('heading', { name: 'Ajouter un membre' }) });
  await addForm.getByLabel('Pseudo du joueur').fill(helper);
  await addForm.getByLabel('Support').uncheck();
  await addForm.getByLabel('Modérateur').check();
  await addForm.getByRole('button', { name: 'Ajouter' }).click();
  await expect(page.getByRole('status')).toHaveText(`${helper} fait partie de l'équipe.`);
  await expect(page.getByRole('row', { name: new RegExp(helper) })).toContainText('À configurer');

  // Audit: the enrolment and the new member, with the author; the chain verifies.
  // (The log survives /dev/reset by design: rows are picked by this run's author.)
  await openSection(page, "Journal d'audit");
  const mine = page.getByRole('table').getByRole('row').filter({ hasText: boss });
  await expect(mine.filter({ hasText: 'Membre ajouté' })).toContainText('moderator');
  await expect(mine.filter({ hasText: '2FA configurée' })).toHaveCount(1);
  await page.getByRole('button', { name: "Vérifier l'intégrité" }).click();
  await expect(page.getByRole('status')).toContainText('Chaîne intacte');

  // Signing out of the admin keeps the game session; a fresh code opens it again.
  const menu = page.getByRole('button', { name: 'Menu' });
  if (await menu.isVisible()) {
    await menu.click();
  }
  await page.getByRole('button', { name: "Se déconnecter de l'admin" }).click();
  await expect(page.getByRole('heading', { name: "Code d'authentification" })).toBeVisible();
  await page.getByLabel('Code à 6 chiffres').fill('000000');
  await page.getByRole('button', { name: 'Entrer' }).click();
  await expect(page.getByRole('alert')).toHaveText("Ce code n'est pas valide.");
  await page.getByLabel('Code à 6 chiffres').fill(await currentCode(page, bossId));
  await page.getByRole('button', { name: 'Entrer' }).click();
  // Back where the member was.
  await expect(page.getByRole('heading', { name: "Journal d'audit" })).toBeVisible();
});
