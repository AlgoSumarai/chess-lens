import { defineConfig, devices } from '@playwright/test';
import { browserTimeout } from './e2e/environment';
const baseURL = process.env.CHESSLENS_URL ?? 'http://127.0.0.1:5173';
const localTestCertificate = process.env.CHESSLENS_LOCAL_TEST_CERTIFICATE === '1';
if (localTestCertificate) {
  const address = new URL(baseURL);
  if (address.protocol !== 'https:' || !['localhost', '127.0.0.1'].includes(address.hostname)) throw new Error('Private test certificates are allowed only for a loopback HTTPS browser profile.');
}
export default defineConfig({
  testDir: './e2e', fullyParallel: false, workers: 1, timeout: browserTimeout(180000),
  expect: { timeout: browserTimeout(5000) },
  // Independently verify this private CA/hostname with check-https.py first.
  // This option is scoped to the disposable local browser context, never the app.
  use: { baseURL, ignoreHTTPSErrors: localTestCertificate, actionTimeout: browserTimeout(15000), navigationTimeout: browserTimeout(30000), trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  reporter: [['list'], ['html', { open: 'never' }]],
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1050 } } },
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
});
