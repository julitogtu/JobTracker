/**
 * Runs the Playwright suite against a private tenant.
 *
 * Every JobTracker API read and write is scoped by organization id, and that id reaches the
 * app as server config -- never from the client. So minting a fresh one per run gives the
 * suite an empty, private slice of the shared database: no seeded fixtures to reset, no
 * cleanup, no interference from whatever is already in the dev database.
 *
 * It has to be an environment variable rather than something the config computes, because
 * playwright.config.ts is loaded by both the main process and each worker -- a `randomUUID()`
 * in the config would differ between them, and the browser would look at a different tenant
 * than the seeding fixture wrote to.
 */
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';

const organizationId = process.env.JOBTRACKER_ORGANIZATION_ID ?? randomUUID();

console.log(`[jobtracker] e2e organization: ${organizationId}`);

const child = spawn(
  process.execPath,
  [
    new URL('../node_modules/@playwright/test/cli.js', import.meta.url).pathname.slice(1),
    'test',
    ...process.argv.slice(2),
  ],
  {
    stdio: 'inherit',
    env: {
      ...process.env,
      JOBTRACKER_ORGANIZATION_ID: organizationId,
      // The Next server is spawned by Playwright and does a server-side fetch to the API over
      // http, so the --use-system-ca dance the https profile needs does not apply here.
      JOBTRACKER_API_URL: process.env.JOBTRACKER_API_URL ?? 'http://localhost:5238',
    },
  },
);

child.on('exit', (code, signal) => {
  signal === null ? process.exit(code ?? 0) : process.kill(process.pid, signal);
});
