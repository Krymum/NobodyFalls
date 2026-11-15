import { expect, test, type APIRequestContext } from '@playwright/test';
import { API_HEADER, anotherPlayer, dev, openSection, resetWorld, staff, uniqueName } from './support.ts';

/**
 * Phase 2 done criterion: an admin schedules a storm and sees it applied at midnight, in
 * dev with the simulated clock (GAME_DESIGN §9, plan §7).
 */

interface Tonight {
  day: string;
  storm: boolean;
  status: string;
  fellOn: string | null;
  standing: { required: number } | null;
}

async function tonight(request: APIRequestContext): Promise<Tonight> {
  return (await (await request.get('/api/v1/tonight')).json()) as Tonight;
}

/**
 * Midnight: the clock moves just past it and the night is run at once (`/dev/night/tick`
 * waits for the worker if it got there first). Returns the first view of the new day: a
 * fallen player sees `fellOn` once, then is back in play.
 */
async function midnight(request: APIRequestContext, after: string): Promise<Tonight> {
  await dev(request, '/dev/clock', { toMidnightMinusSeconds: -1 });
  await dev(request, '/dev/night/tick');
  const view = await tonight(request);
  expect(view.day).not.toBe(after);
  return view;
}

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('an admin schedules a storm, it opens the next day and the night is resolved with one more hand', async ({
  browser,
}, info) => {
  const project = info.project.name;
  const lea = await anotherPlayer(browser, uniqueName('lea', project));
  const givers = [
    await anotherPlayer(browser, uniqueName('don', project)),
    await anotherPlayer(browser, uniqueName('dona', project)),
  ];
  const today = (await tonight(lea.request)).day;

  // The admin schedules a storm for tomorrow from the configuration screen.
  const chef = uniqueName('chef', project);
  const admin = await staff(browser, chef, ['admin']);
  await openSection(admin, 'Configuration');
  await admin.getByRole('link', { name: 'Tempêtes' }).click();
  const tomorrow = new Date(`${today}T00:00:00Z`);
  tomorrow.setUTCDate(tomorrow.getUTCDate() + 1);
  const day = tomorrow.toISOString().slice(0, 10);
  await admin.getByLabel('Jour').fill(day);
  await admin.getByRole('button', { name: 'Programmer' }).click();
  await expect(admin.getByRole('status')).toContainText('Tempête programmée');
  await expect(admin.getByRole('row').filter({ hasText: 'À venir' })).toHaveCount(1);

  // Midnight: tomorrow opens with the storm, and the threshold shows one more hand.
  // Léa signed up today: her second night is fragile (+1), and the storm adds one.
  const stormy = await midnight(lea.request, today);
  expect(stormy.storm).toBe(true);
  expect(stormy.standing?.required).toBe(3);
  const leaPage = await (
    await browser.newContext({ locale: 'fr-FR', storageState: await lea.request.storageState() })
  ).newPage();
  await leaPage.goto('/fr/');
  await expect(leaPage.getByText('Nuit de tempête : il faut une main de plus pour tenir.')).toBeVisible();
  await admin.reload();
  await expect(admin.getByRole('row').filter({ hasText: "Aujourd'hui" })).toHaveCount(1);
  // The day has started: the storm can no longer be cancelled.
  await expect(admin.getByRole('button', { name: 'Annuler' })).toHaveCount(0);

  // Two hands would be enough on a calm night; with the storm, Léa needs three.
  for (const giver of givers) {
    const hand = await giver.request.post('/api/v1/hands', {
      headers: API_HEADER,
      data: { receiverId: lea.id, turnstileToken: 'XXXX.DUMMY.TOKEN.XXXX' },
    });
    expect(hand.status()).toBe(201);
  }
  const after = await midnight(lea.request, stormy.day);
  expect(after.fellOn).toBe(stormy.day);

  // Every step is in the audit (the log survives /dev/reset: rows are picked by this run's author).
  await openSection(admin, "Journal d'audit");
  await expect(
    admin
      .getByRole('table')
      .getByRole('row')
      .filter({ hasText: chef })
      .filter({ hasText: 'Tempête programmée' })
      .filter({ hasText: day }),
  ).toHaveCount(1);
});

test('an admin switches the ideas off, players no longer see how to propose one', async ({
  browser,
}, info) => {
  const project = info.project.name;
  const admin = await staff(browser, uniqueName('chef', project), ['admin']);
  await openSection(admin, 'Configuration');
  await admin.getByRole('link', { name: 'Fonctionnalités' }).click();
  const ideas = admin.getByRole('row').filter({ hasText: 'Idées' });
  await ideas.getByRole('checkbox').click();
  await expect(admin.getByRole('status')).toHaveText('Idées : désactivée.');
  await expect(ideas).toContainText('Désactivée');

  const player = await anotherPlayer(browser, uniqueName('joueur', project));
  const page = await (
    await browser.newContext({ locale: 'fr-FR', storageState: await player.request.storageState() })
  ).newPage();
  await page.goto('/fr/profile/feedback?tab=ideas');
  await expect(page.getByText('Les nouvelles propositions et les votes sont fermés')).toBeVisible();
  await expect(page.getByText('Proposer une idée')).toHaveCount(0);
});
