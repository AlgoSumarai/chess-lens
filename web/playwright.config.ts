import { defineConfig, devices } from '@playwright/test';
import { browserTimeout } from './e2e/environment';
export default defineConfig({
  testDir: './e2e', fullyParallel: false, workers: 1, timeout: browserTimeout(180000),
  expect: { timeout: browserTimeout(5000) },
  use: { baseURL: process.env.CHESSLENS_URL ?? 'http://127.0.0.1:5173', actionTimeout: browserTimeout(15000), navigationTimeout: browserTimeout(30000), trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  reporter: [['list'], ['html', { open: 'never' }]],
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1050 } } },
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
});
