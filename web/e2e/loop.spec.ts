import { expect, test } from '@playwright/test';
import {
  anotherPlayer,
  API_HEADER,
  dev,
  myId,
  resetWorld,
  signUpThroughTheInterface,
  uniqueName,
} from './support.ts';

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

/**
 * The whole loop (phase 1 "done" criterion): sign up, give and receive hands, watch
 * midnight hold or drop people. Played through the interface, against the real stack.
 */
test('sign up, give and receive hands, and live through midnight', async ({ page, browser }, info) => {
  const me = uniqueName('moi', info.project.name);
  await signUpThroughTheInterface(page, me);

  // A world to play in: fake players, some of them on the edge.
  await dev(page.request, '/dev/seed', { players: 10, friendsPerPlayer: 0 });

  // Give a hand from the Edge.
  await page.getByRole('link', { name: 'Bord du vide', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Au bord du vide' })).toBeVisible();
  await expect(page.getByText('Il te reste 3 mains.')).toBeVisible();
  // Someone missing exactly one hand: mine saves them, which earns Guardian points at midnight.
  const oneShort = page.getByRole('listitem').filter({ has: page.getByText(/^(0\/1|1\/2|2\/3)$/) });
  await oneShort
    .first()
    .getByRole('button', { name: /^Tenir / })
    .click();
  await expect(page.getByText('Il te reste 2 mains.')).toBeVisible();

  // Someone holds me: the neighbourhood updates live, without reloading.
  await page.getByRole('link', { name: 'Ce soir' }).click();
  const friendName = uniqueName('ami', info.project.name);
  const friend = await anotherPlayer(browser, friendName);
  const hand = await friend.request.post('/api/v1/hands', {
    headers: API_HEADER,
    data: { receiverId: await myId(page), turnstileToken: 'XXXX.DUMMY.TOKEN.XXXX' },
  });
  expect(hand.status()).toBe(201);
  await expect(page.getByRole('img', { name: new RegExp(`retenu par ${friendName}`) })).toBeAttached();

  // Midnight: the night is resolved by the server on its own, and the screen follows.
  const night = Number(
    (await page.getByRole('link', { name: /NUIT \d+/ }).textContent())?.match(/NUIT (\d+)/)?.[1],
  );
  await dev(page.request, '/dev/clock', { toMidnightMinusSeconds: 3 });
  await expect(page.getByRole('link', { name: new RegExp(`NUIT ${night + 1} `) })).toBeVisible({
    timeout: 30_000,
  });

  // The hand I gave saved someone who could fall: Guardian points.
  await page.getByRole('link', { name: 'Profil' }).click();
  await expect(page.getByText('SCORE GARDIEN')).toBeVisible();
  const score = page.locator('dt', { hasText: 'SCORE GARDIEN' }).locator('xpath=following-sibling::dd');
  await expect(score).not.toHaveText('0');
});
