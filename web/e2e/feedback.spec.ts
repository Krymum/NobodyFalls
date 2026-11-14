import { expect, test } from '@playwright/test';
import { anotherPlayer, API_HEADER, resetWorld, signUpByApi, uniqueName } from './support.ts';

/** "Ideas and contact", from the profile (GAME_DESIGN §8). */

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('ideas: suggest one, see others back it', async ({ page, browser }, info) => {
  const me = uniqueName('idee', info.project.name);
  await signUpByApi(page.request, me);
  const other = await anotherPlayer(browser, uniqueName('fan', info.project.name));

  await page.goto('/fr/profile');
  await page.getByRole('link', { name: /Proposer une idée, écrire à l'équipe/ }).click();
  await expect(page.getByRole('heading', { name: 'Idées et contact' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Idées' })).toHaveAttribute('aria-selected', 'true');

  await page.getByText('Proposer une idée').click();
  await page.getByLabel('Titre').fill('Une carte du filet');
  await page.getByLabel('Description').fill('Voir qui tient qui, à l’échelle du monde.');
  await page.getByLabel('Catégorie').selectOption('gameplay');
  await page.getByRole('button', { name: 'Proposer', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Merci. Ton idée est en ligne.');

  const idea = page.getByRole('listitem').filter({ hasText: 'Une carte du filet' });
  await expect(idea).toContainText('Ta proposition');
  await expect(idea).toContainText('0 soutien');

  // Another player backs it.
  const list = (await (await other.request.get('/api/v1/ideas')).json()) as { items: { id: number }[] };
  const id = list.items[0]?.id ?? 0;
  expect((await other.request.put(`/api/v1/ideas/${id}/vote`, { headers: API_HEADER })).ok()).toBe(true);

  await page.reload();
  await expect(page.getByRole('listitem').filter({ hasText: 'Une carte du filet' })).toContainText(
    '1 soutien',
  );
});

test('contact: write to the team and follow the conversation', async ({ page }, info) => {
  await signUpByApi(page.request, uniqueName('aide', info.project.name));
  await page.goto('/fr/profile/feedback?tab=contact');

  await page.getByText('Nouvelle demande').click();
  await page.getByLabel('Sujet de la demande').selectOption('support');
  await page.getByLabel('Objet').fill('Mes mains');
  await page.getByLabel('Message', { exact: true }).fill('Je ne vois pas mes mains ce soir.');
  await page.getByRole('button', { name: 'Envoyer' }).click();
  await expect(page.getByRole('status')).toHaveText("Demande envoyée. L'équipe te répondra ici.");

  const ticket = page.getByRole('button', { name: /Mes mains/ });
  await expect(ticket).toContainText("En attente de l'équipe");
  await expect(page.getByText('Je ne vois pas mes mains ce soir.')).toBeVisible();

  await page.getByLabel('Ta réponse').fill('J’ajoute : depuis hier.');
  await page.getByRole('button', { name: 'Répondre' }).click();
  await expect(page.getByText('J’ajoute : depuis hier.')).toBeVisible();
});

test('report: a player, once', async ({ page, browser }, info) => {
  await signUpByApi(page.request, uniqueName('temoin', info.project.name));
  const target = uniqueName('fauteur', info.project.name);
  await anotherPlayer(browser, target);
  await page.goto('/fr/profile/feedback');

  await page.getByRole('tab', { name: 'Idées' }).focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Signaler' })).toHaveAttribute('aria-selected', 'true');

  await page.getByLabel('Pseudo du joueur').fill(target);
  await page.getByLabel('Motif').selectOption('pseudo');
  await page.getByRole('button', { name: 'Signaler', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText("Merci. L'équipe va regarder.");

  await page.getByLabel('Pseudo du joueur').fill(target);
  await page.getByRole('button', { name: 'Signaler', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText("Tu as déjà signalé ce joueur : l'équipe s'en occupe.");
});
