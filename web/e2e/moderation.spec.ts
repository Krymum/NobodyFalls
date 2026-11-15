import { expect, test } from '@playwright/test';
import { API_HEADER, openSection, resetWorld, signUpByApi, staff, uniqueName } from './support.ts';

/**
 * Phase 2 done criterion: a moderator handles a report end to end, and every action
 * appears in the audit log (GAME_DESIGN §9).
 */

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('a moderator handles a report from the queue to a ban, and the audit shows every step', async ({
  page,
  browser,
}, info) => {
  const project = info.project.name;
  const troll = uniqueName('troll', project);
  const modo = uniqueName('modo', project);

  // A player reports another one.
  await signUpByApi(page.request, uniqueName('temoin', project));
  const trollContext = await browser.newContext({ locale: 'fr-FR' });
  await signUpByApi(trollContext.request, troll);
  const reported = await page.request.post('/api/v1/reports', {
    headers: API_HEADER,
    data: {
      target: 'player',
      pseudo: troll,
      category: 'harassment',
      details: 'Insultes répétées.',
      turnstileToken: 'XXXX.DUMMY.TOKEN.XXXX',
    },
  });
  expect(reported.status()).toBe(201);

  // The moderator takes it from the queue and bans the troll for three days.
  const moderator = await staff(browser, modo, ['moderator']);
  await openSection(moderator, 'Signalements');
  await moderator.getByRole('link', { name: troll }).click();
  await expect(moderator.getByText('Insultes répétées.')).toBeVisible();
  await moderator.getByRole('button', { name: "Je m'en occupe" }).click();
  await expect(moderator.getByRole('status')).toHaveText('Pris en charge.');

  await moderator.getByLabel('Traité', { exact: true }).check();
  await moderator.getByLabel("Note (pour l'équipe)").fill('Insultes confirmées.');
  await moderator.getByLabel('Sanctionner la cible').check();
  // A select inside its label is named by the label and its current option ("Sanction Avertissement").
  await moderator.getByRole('combobox', { name: /^Sanction/ }).selectOption('ban');
  await moderator.getByLabel("Motif (pour l'équipe)").fill('Harcèlement');
  await moderator.getByLabel('Message au joueur').fill('Tu es suspendu trois jours pour harcèlement.');
  const end = new Date(Date.now() + 3 * 24 * 3600 * 1000);
  const local = new Date(end.getTime() - end.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  await moderator.getByLabel('Fin', { exact: true }).fill(local);
  await moderator.getByRole('button', { name: 'Clore le signalement' }).click();
  await expect(moderator.getByRole('status')).toHaveText(
    "Signalement clos. L'auteur est prévenu qu'il a été traité.",
  );

  // The troll sees the ban and can only contest it.
  const trollPage = await trollContext.newPage();
  await trollContext.request.get(`/auth/login/dev?name=${troll}&returnUrl=/fr/`);
  await trollPage.goto('/fr/');
  await expect(trollPage.getByRole('heading', { name: 'Ton compte est suspendu.' })).toBeVisible();
  await expect(trollPage.getByText('Tu es suspendu trois jours pour harcèlement.')).toBeVisible();
  await trollPage.getByRole('link', { name: 'Contester cette décision' }).click();
  await expect(trollPage.getByRole('tab', { name: 'Contact' })).toBeVisible();
  await expect(trollPage.getByRole('tab', { name: 'Idées' })).toHaveCount(0);

  // An admin finds each of the moderator's actions in the audit log.
  const admin = await staff(browser, uniqueName('chef', project), ['admin']);
  await openSection(admin, "Journal d'audit");
  const byModerator = admin.getByRole('table').getByRole('row').filter({ hasText: modo });
  await expect(byModerator.filter({ hasText: 'Signalement pris en charge' })).toHaveCount(1);
  await expect(byModerator.filter({ hasText: 'Sanction appliquée' })).toContainText('ban');
  await expect(byModerator.filter({ hasText: 'Signalement clos' })).toContainText('actioned');
  await admin.getByRole('button', { name: "Vérifier l'intégrité" }).click();
  await expect(admin.getByRole('status')).toContainText('Chaîne intacte');
});

test('an admin reads the dashboard, looks up linked accounts, exports then deletes an account', async ({
  page,
  browser,
}, info) => {
  const project = info.project.name;
  const main = uniqueName('main', project);
  const alt = uniqueName('alt', project);
  await signUpByApi(page.request, main);
  // A second account from the same address (every test player comes from this machine).
  const altContext = await browser.newContext({ locale: 'fr-FR' });
  await signUpByApi(altContext.request, alt);

  const chef = uniqueName('chef', project);
  const admin = await staff(browser, chef, ['admin']);
  await expect(admin.getByText('Joueurs en vie')).toBeVisible();
  await expect(admin.getByRole('heading', { name: 'Comptes suspects' })).toBeVisible();

  await openSection(admin, 'Joueurs');
  await admin.getByLabel('Pseudo ou identifiant').fill(main);
  await admin.getByRole('link', { name: main, exact: true }).click();
  await admin.getByRole('button', { name: 'Afficher les comptes liés' }).click();
  await expect(admin.getByRole('listitem').filter({ hasText: alt })).toContainText('même adresse');

  const download = admin.waitForEvent('download');
  await admin.getByRole('link', { name: 'Exporter ses données (JSON)' }).click();
  expect((await download).suggestedFilename()).toMatch(/^nobodyfalls-[0-9a-f-]{36}\.json$/);

  await admin.getByLabel("Demande à l'origine de la suppression").fill('Demande du joueur par contact');
  admin.once('dialog', (dialog) => void dialog.accept());
  await admin.getByRole('button', { name: 'Supprimer le compte' }).click();
  await expect(admin.getByRole('status').filter({ hasText: 'Compte supprimé.' })).toBeVisible();

  await openSection(admin, "Journal d'audit");
  const mine = admin.getByRole('table').getByRole('row').filter({ hasText: chef });
  await expect(mine.filter({ hasText: 'Comptes liés consultés' })).toHaveCount(1);
  await expect(mine.filter({ hasText: 'Données exportées' })).toHaveCount(1);
  await expect(mine.filter({ hasText: 'Compte supprimé' })).toHaveCount(1);
});
