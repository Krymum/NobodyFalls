import { expect, test } from '@playwright/test';
import { anotherPlayer, resetWorld, signUpThroughTheInterface, uniqueName } from './support.ts';

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('the language lives in the URL and can be switched', async ({ page }, info) => {
  await signUpThroughTheInterface(page, uniqueName('lang', info.project.name));

  await page.getByRole('link', { name: 'Profil' }).click();
  await page.getByRole('button', { name: 'English' }).click();

  await expect(page).toHaveURL(/\/en\/profile$/);
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
  await page.getByRole('link', { name: 'Tonight' }).click();
  await expect(page.getByRole('heading', { name: 'Your first night.' })).toBeVisible();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
});

test('an invitation link makes friends at sign-up', async ({ page, browser }, info) => {
  const inviterName = uniqueName('hote', info.project.name);
  const inviter = await anotherPlayer(browser, inviterName);
  const { inviteCode } = (await (await inviter.request.get('/api/v1/me/profile')).json()) as {
    inviteCode: string;
  };

  await page.goto(`/fr/i/${inviteCode}`);
  for (let i = 0; i < 3; i++) {
    await page.getByRole('button', { name: 'Suivant' }).click();
  }
  const pseudo = uniqueName('invite', info.project.name);
  await page.getByPlaceholder('Nom de test').fill(pseudo);
  await page.getByRole('button', { name: 'Connexion de développement' }).click();
  await expect(page.getByText(`${inviterName} t'a invité : vous serez amis.`)).toBeVisible();
  await page.getByLabel('Ton pseudo').fill(pseudo);
  await page.getByRole('button', { name: 'Entrer dans le vide' }).click();
  await page.getByRole('button', { name: /Plus tard|C'est parti/ }).click();

  await page.getByRole('link', { name: 'Amis' }).click();
  await expect(page.getByText(inviterName, { exact: true })).toBeVisible();
});

test('friend requests by name, answered live', async ({ page, browser }, info) => {
  await signUpThroughTheInterface(page, uniqueName('req', info.project.name));
  const otherName = uniqueName('autre', info.project.name);
  const other = await anotherPlayer(browser, otherName);

  await page.getByRole('link', { name: 'Amis' }).click();
  await page.getByLabel('Ajouter par pseudo').fill(otherName);
  await page.getByRole('button', { name: 'Ajouter', exact: true }).click();
  await expect(page.getByText(`Demande envoyée à ${otherName}.`)).toBeVisible();
  await expect(page.getByText('En attente de réponse')).toBeVisible();

  const requests = (await (await other.request.get('/api/v1/friends/requests')).json()) as {
    incoming: { id: number }[];
  };
  const id = requests.incoming[0]?.id;
  await other.request.post(`/api/v1/friends/requests/${id}/accept`, { headers: { 'X-NF-Request': '1' } });

  // The acceptance arrives through the real-time hub.
  await expect(page.getByText('Ton ami')).toBeVisible();
});

test('the world counters and the reference scene render', async ({ page }, info) => {
  await signUpThroughTheInterface(page, uniqueName('monde', info.project.name));
  await page.getByRole('link', { name: /NUIT \d+/ }).click();
  await expect(page.getByRole('heading', { name: 'Le monde' })).toBeVisible();
  await expect(page.getByText('EN VIE')).toBeVisible();

  await page.goto('/fr/dev/scene');
  await expect(page.getByRole('button', { name: 'Tendre ta dernière main à yuki' })).toBeVisible();
  await page.getByRole('button', { name: 'Tendre ta dernière main à yuki' }).click();
  await expect(page.getByText('YUKI · 1/2')).toBeVisible();
});

test('signed-out visitors start with the rules', async ({ page }) => {
  await page.goto('/en/');
  await expect(page.getByRole('heading', { name: 'Nobody falls.' })).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0);
});
