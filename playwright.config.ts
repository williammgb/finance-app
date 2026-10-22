import { defineConfig, devices } from '@playwright/test';

// The browser checks in tests/ui. `./run ui` starts the app and sets UI_URL.
export default defineConfig({
  testDir: 'tests/ui',
  outputDir: '.work/tmp/playwright',
  reporter: 'list',
  workers: 1,
  use: {
    ...devices['Desktop Chrome'],
    baseURL: process.env.UI_URL ?? 'http://localhost:5080',
    viewport: { width: 1280, height: 800 },
  },
});
