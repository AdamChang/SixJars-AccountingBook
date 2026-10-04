import { defineConfig, devices } from '@playwright/test';

// 4301：避開 Windows 保留的 4150–4249 區段，也不和手動開發用的 4300 衝突
export default defineConfig({
  testDir: './e2e',
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:4301',
    timezoneId: 'Asia/Taipei',
    locale: 'zh-TW',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npx ng serve --port 4301 --ssl false',
    url: 'http://localhost:4301',
    reuseExistingServer: true,
    timeout: 180_000,
    env: { NG_CLI_ANALYTICS: 'false' },
  },
});
