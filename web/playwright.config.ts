import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests against the running stack (`docker compose up`, dev mode).
 * Tests share one database and one simulated clock, so they run one at a time.
 *
 * Two families of projects:
 * - the whole game loop in five browsers (desktop engines and two phones);
 * - the responsive layout (ART_DIRECTION, "Responsive") at 390 × 844, 820 × 1180 and
 *   1440 × 900: structure everywhere; reference screenshots only inside the official
 *   Playwright Linux image (`pnpm e2e:screenshots [--update]`), where fonts render as in CI.
 */
const viewports = [
  { name: 'phone-390', width: 390, height: 844 },
  { name: 'tablet-820', width: 820, height: 1180 },
  { name: 'desktop-1440', width: 1440, height: 900 },
];

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  timeout: 90_000,
  expect: {
    timeout: 15_000,
    // Characters sway from a phase derived from their (random) ids: a still frame
    // differs slightly from run to run, never more than this.
    toHaveScreenshot: { maxDiffPixelRatio: 0.02, threshold: 0.25 },
  },
  snapshotPathTemplate: '{testDir}/__screenshots__/{projectName}/{arg}{ext}',
  reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:8080',
    locale: 'fr-FR',
    trace: 'retain-on-failure',
  },
  projects: [
    ...[
      { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
      { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
      { name: 'webkit', use: { ...devices['Desktop Safari'] } },
      { name: 'mobile-chrome', use: { ...devices['Pixel 7'] } },
      { name: 'mobile-safari', use: { ...devices['iPhone 14'] } },
    ].map((project) => ({ ...project, testIgnore: /responsive\.spec/ })),
    ...viewports.map(({ name, width, height }) => ({
      name,
      testMatch: /responsive\.spec/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width, height },
        deviceScaleFactor: 1,
        hasTouch: width < 1024,
        reducedMotion: 'reduce' as const,
      },
    })),
  ],
});
