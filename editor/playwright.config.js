import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './tests', fullyParallel: true, workers: 2, timeout: 15000, globalTimeout: 120000,
  reporter: 'list', use: { headless: true },
  projects: [{ name: 'chromium', use: { browserName: 'chromium', ...(process.env.PAPERNOTE_EDGE ? { channel: 'msedge' } : {}) } }, { name: 'webkit', use: { browserName: 'webkit' } }] });
