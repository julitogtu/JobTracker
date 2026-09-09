import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end config: real browser -> Next dev server -> .NET API -> PostgreSQL.
 *
 * Run it with `npm run test:e2e`, not `npx playwright test`. The wrapper in
 * scripts/run-e2e.mjs mints a fresh JOBTRACKER_ORGANIZATION_ID for the run, which is what
 * gives the suite a private slice of the database — every API read and write is tenant-scoped
 * by that id, so a run never sees another run's jobs and needs no cleanup.
 */

const API_URL = process.env.JOBTRACKER_API_URL ?? 'http://localhost:5238';
const APP_PORT = process.env.JOBTRACKER_APP_PORT ?? '3100';
const APP_URL = `http://localhost:${APP_PORT}`;

export default defineConfig({
  testDir: './e2e',

  /*
   * One worker, no parallelism. The suite shares a single Next server bound to one
   * organization id, so parallel tests would see each other's rows in the same list.
   */
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],

  timeout: 45_000,
  expect: { timeout: 10_000 },

  use: {
    baseURL: APP_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // No video: Playwright's ffmpeg download is blocked on this network, and a missing
    // binary fails the test itself rather than degrading. Traces carry the same evidence.
  },

  projects: [
    {
      name: 'chrome',
      use: {
        ...devices['Desktop Chrome'],
        /*
         * Uses the Chrome already installed on the machine rather than Playwright's bundled
         * Chromium. `npx playwright install chromium` times out here — the ~150MB download
         * exceeds Playwright's 30s per-request limit on this connection. If you want the
         * pinned build instead, run the install and drop this line.
         */
        channel: 'chrome',
      },
    },
  ],

  webServer: [
    {
      /*
       * Reused if you already have the API running; org-agnostic, so sharing it is safe.
       *
       * ignoreHTTPSErrors matters even though this URL is http: the `https` launch profile that
       * Visual Studio uses by default binds 5238 as well and redirects it to 7163, so the probe
       * follows the redirect into the ASP.NET dev certificate. Without this it reports a
       * self-signed error, concludes nothing is listening, and tries to start a second API --
       * which then cannot build, because the running one holds a lock on its own DLLs.
       */
      command: 'dotnet run --project ../JobTracker.Api --launch-profile http',
      url: `${API_URL}/`,
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      /*
       * Its own port so an already-running `npm run dev` on 3000 is left alone. This server
       * must NOT be reused: it is pinned to the run's organization id, and a server started
       * with a different one would serve the wrong tenant's jobs.
       *
       * Next's env loader skips keys already present in process.env, so the values passed
       * here win over .env.local.
       */
      command: `npm run dev -- --port ${APP_PORT}`,
      url: APP_URL,
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        JOBTRACKER_API_URL: API_URL,
        JOBTRACKER_ORGANIZATION_ID: process.env.JOBTRACKER_ORGANIZATION_ID ?? '',
      },
    },
  ],
});
