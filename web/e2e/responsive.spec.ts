import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';
import { API_HEADER, resetWorld, signUpByApi } from './support.ts';

/**
 * The layout at 390 × 844, 820 × 1180 and 1440 × 900 (ART_DIRECTION, "Responsive"):
 * structure checks everywhere, and a reference screenshot of every main screen.
 * Runs in the `phone-390`, `tablet-820` and `desktop-1440` projects.
 */

/**
 * Screenshots are compared only inside the official Playwright Linux image, where the
 * references were made (`pnpm e2e:screenshots`); elsewhere fonts render differently,
 * and only the structure is checked.
 */
const SCREENSHOTS = process.env['E2E_SCREENSHOTS'] === '1';

interface World {
  lea: APIRequestContext;
}

/**
 * A small, fixed world: Léa is held by Malo and holds Inès, who holds Malo; Yuki has
 * nobody and hangs on the Edge. Léa and Malo are friends.
 */
async function buildWorld(browser: Browser, page: Page): Promise<World> {
  const others = async (pseudo: string): Promise<{ request: APIRequestContext; id: string }> => {
    const request = (await browser.newContext()).request;
    return { request, id: await signUpByApi(request, pseudo) };
  };
  const leaId = await signUpByApi(page.request, 'lea');
  const malo = await others('malo');
  const ines = await others('ines');
  await others('yuki');

  const hand = async (from: APIRequestContext, to: string): Promise<void> => {
    const response = await from.post('/api/v1/hands', {
      headers: API_HEADER,
      data: { receiverId: to, turnstileToken: 'XXXX.DUMMY.TOKEN.XXXX' },
    });
    expect(response.status()).toBeLessThan(300);
  };
  await hand(malo.request, leaId);
  await hand(page.request, ines.id);
  await hand(ines.request, malo.id);

  await page.request.post('/api/v1/friends/requests', { headers: API_HEADER, data: { pseudo: 'malo' } });
  const requests = (await (await malo.request.get('/api/v1/friends/requests')).json()) as {
    incoming: { id: number }[];
  };
  for (const request of requests.incoming) {
    await malo.request.post(`/api/v1/friends/requests/${request.id}/accept`, { headers: API_HEADER });
  }
  return { lea: page.request };
}

/** The countdown changes every second: it is masked in screenshots. */
function moving(page: Page) {
  return [page.getByRole('link', { name: /NUIT \d+ · / })];
}

/** Invitation codes are random and their width varies: show a fixed one in screenshots. */
async function fixRandomTexts(page: Page): Promise<void> {
  await page.evaluate(() => {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      if (node.textContent && /\/i\/[A-Z0-9]+$/.test(node.textContent)) {
        node.textContent = node.textContent.replace(/\/i\/[A-Z0-9]+$/, '/i/INVITE0000');
      }
    }
  });
}

async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  // Two frames: the scene's still frame and the labels placed by the camera.
  await page.evaluate(
    () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
  );
}

async function shoot(page: Page, name: string): Promise<void> {
  await settle(page);
  await fixRandomTexts(page);
  if (SCREENSHOTS) {
    await expect(page).toHaveScreenshot(`${name}.png`, { mask: moving(page), animations: 'disabled' });
  }
}

async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

/** The scene canvas covers the whole window. */
async function expectFullWindowScene(page: Page): Promise<void> {
  const viewport = page.viewportSize();
  const box = await page.getByRole('img', { name: /^Tu es suspendu/ }).boundingBox();
  expect(box).not.toBeNull();
  expect(box).toEqual({ x: 0, y: 0, width: viewport?.width, height: viewport?.height });
}

test.beforeEach(async ({ request }) => {
  await resetWorld(request);
});

test('onboarding', async ({ page }) => {
  await page.goto('/fr/');
  await expect(page.getByRole('button', { name: 'Suivant' })).toBeVisible();
  await expectNoHorizontalScroll(page);
  const width = page.viewportSize()?.width ?? 0;
  const column = await page.getByRole('button', { name: 'Suivant' }).boundingBox();
  // A phone uses the whole width; larger screens keep a reading column.
  expect(column?.width).toBeLessThanOrEqual(width >= 640 ? 560 : width);
  await shoot(page, 'onboarding');
});

test('main screens', async ({ page, browser }) => {
  await buildWorld(browser, page);
  const viewport = page.viewportSize() ?? { width: 0, height: 0 };
  const desktop = viewport.width >= 1024;
  const navigation = page.getByRole('navigation', { name: 'Navigation principale' });

  // Tonight: the scene fills the window; the text at the bottom, or in a side panel on desktops.
  await page.goto('/fr/');
  await expect(page.getByRole('heading', { name: 'Ta première nuit.' })).toBeVisible();
  await expectFullWindowScene(page);
  await expectNoHorizontalScroll(page);
  const nav = await navigation.boundingBox();
  const panel = await page.getByRole('heading', { name: 'Ta première nuit.' }).boundingBox();
  if (desktop) {
    expect(nav).toMatchObject({ x: 0, y: 0, width: 88, height: viewport.height });
    expect(panel?.x).toBeGreaterThanOrEqual(viewport.width - 420);
  } else {
    expect((nav?.y ?? 0) + (nav?.height ?? 0)).toBeCloseTo(viewport.height, 0);
    expect(panel?.y).toBeGreaterThan(viewport.height / 2);
  }
  for (const link of await navigation.getByRole('link').all()) {
    expect((await link.boundingBox())?.height).toBeGreaterThanOrEqual(44);
  }
  await expect(page.getByText('MALO', { exact: true })).toBeVisible();
  await shoot(page, 'tonight');

  // The Edge: a list; on desktops, next to a preview of the chosen player.
  await navigation.getByRole('link', { name: 'Bord du vide', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Au bord du vide' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Tenir yuki' })).toBeVisible();
  await expectNoHorizontalScroll(page);
  const list = await page.getByRole('list').filter({ hasText: 'yuki' }).boundingBox();
  expect(list?.width).toBeLessThanOrEqual(desktop ? 760 : viewport.width >= 640 ? 640 : viewport.width);
  if (desktop) {
    const preview = page.getByRole('complementary', { name: 'yuki' });
    await expect(preview).toBeVisible();
    await expect(preview.getByRole('img', { name: 'yuki' })).toBeVisible();
  } else {
    await expect(page.getByRole('complementary')).toHaveCount(0);
  }
  await shoot(page, 'edge');

  for (const [tab, heading, name] of [
    ['Amis', 'Amis', 'friends'],
    ['Profil', 'lea', 'profile'],
  ] as const) {
    await navigation.getByRole('link', { name: tab }).click();
    await expect(page.getByRole('heading', { name: heading, exact: true }).first()).toBeVisible();
    await expectNoHorizontalScroll(page);
    await shoot(page, name);
  }

  await page.goto('/fr/world');
  await expect(page.getByRole('heading').first()).toBeVisible();
  await expectNoHorizontalScroll(page);
  await shoot(page, 'world');
});

test('desktop keyboard shortcuts', async ({ page, browser }) => {
  test.skip((page.viewportSize()?.width ?? 0) < 1024, 'Shortcuts are a desktop feature.');
  await buildWorld(browser, page);
  await page.goto('/fr/');
  await expect(page.getByRole('heading', { name: 'Ta première nuit.' })).toBeVisible();

  await page.keyboard.press('2');
  await expect(page.getByRole('heading', { name: 'Au bord du vide' })).toBeVisible();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Il te reste une main.')).toBeVisible();

  await page.keyboard.press('?');
  const help = page.getByRole('dialog', { name: 'Raccourcis clavier' });
  await expect(help).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(help).toBeHidden();

  await page.keyboard.press('4');
  await expect(page.getByRole('heading', { name: 'lea' })).toBeVisible();
  // Focus is visible on every control.
  await page.keyboard.press('Tab');
  const outline = await page.evaluate(
    () => getComputedStyle(document.activeElement ?? document.body).outlineStyle,
  );
  expect(outline).toBe('solid');
});
