import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

import { expect, request, test as base, type APIRequestContext } from '@playwright/test';

const run = promisify(execFile);

const API_URL = process.env.JOBTRACKER_API_URL ?? 'http://localhost:5238';

/**
 * The tenant this run owns. run-e2e.mjs mints it and passes the same value to the Next
 * server, so what the fixture seeds is exactly what the browser sees.
 */
export const ORGANIZATION_ID = process.env.JOBTRACKER_ORGANIZATION_ID ?? '';

let sequence = 0;

/** Unique per job, so a test can find its own row by text with no risk of collision. */
export function uniqueTitle(label: string): string {
  sequence += 1;
  return `E2E ${label} ${Date.now().toString(36)}-${sequence}`;
}

export interface SeedOptions {
  title?: string;
  description?: string;
  city?: string;
  state?: string;
  /** Minutes from now. Omit for an unscheduled Draft. */
  scheduledInMinutes?: number;
}

export interface SeededJob {
  id: string;
  title: string;
}

/**
 * Seeds through the .NET API rather than the UI.
 *
 * Two reasons. Setting up a Completed job through the browser would take three flows to reach
 * the state a single test is about, and — more to the point — the UI has no way to start a
 * job at all: there is no start-job feature in the React app, so InProgress is unreachable
 * from the browser. Arranging state over HTTP keeps each test's clicking to the behaviour it
 * actually asserts.
 */
export class JobsApi {
  constructor(private readonly api: APIRequestContext) {}

  async seed(options: SeedOptions = {}): Promise<SeededJob> {
    const title = options.title ?? uniqueTitle('job');

    const response = await this.api.post('/api/jobs', {
      data: {
        title,
        description: options.description ?? 'Seeded by the end-to-end suite.',
        street: '123 Main St',
        city: options.city ?? 'Austin',
        state: options.state ?? 'TX',
        zipCode: '78701',
        latitude: 30.2672,
        longitude: -97.7431,
        customerId: crypto.randomUUID(),
        organizationId: ORGANIZATION_ID,
        scheduledDateUtc:
          options.scheduledInMinutes === undefined
            ? null
            : new Date(Date.now() + options.scheduledInMinutes * 60_000).toISOString(),
        assigneeId: options.scheduledInMinutes === undefined ? null : crypto.randomUUID(),
      },
    });

    expect(response.status(), `create job: ${await response.text()}`).toBe(201);
    const { id } = (await response.json()) as { id: string };

    return { id, title };
  }

  async start(jobId: string): Promise<void> {
    const response = await this.api.post('/api/jobs/start', {
      data: { organizationId: ORGANIZATION_ID, jobId },
    });

    expect(response.status(), `start job: ${await response.text()}`).toBe(204);
  }

  async complete(jobId: string, signatureUrl = 'https://example.com/signatures/e2e.png'): Promise<void> {
    const response = await this.api.post('/api/jobs/complete', {
      data: { organizationId: ORGANIZATION_ID, jobId, signatureUrl },
    });

    expect(response.status(), `complete job: ${await response.text()}`).toBe(204);
  }

  /** Seeds a job already moved to InProgress — the only status the UI offers an action for. */
  async seedInProgress(options: SeedOptions = {}): Promise<SeededJob> {
    const job = await this.seed({ scheduledInMinutes: 60, ...options });
    await this.start(job.id);

    return job;
  }

  async get(jobId: string): Promise<Record<string, unknown>> {
    const response = await this.api.get(`/api/jobs/${ORGANIZATION_ID}/${jobId}`);
    expect(response.status()).toBe(200);

    return (await response.json()) as Record<string, unknown>;
  }
}

/**
 * Empties the run's tenant.
 *
 * The organization id is per run, not per test -- it reaches the app as server environment,
 * and the Next server is started once. So without this, each test inherits the rows the
 * previous one left behind, and any assertion about a total ("2 of 2", the empty state) is
 * really an assertion about test order.
 *
 * It goes straight to PostgreSQL because the API exposes no delete: jobs are only ever
 * created, and the domain has no removal transition. Deleting the job cascades to its photos.
 * The container is the one docker-compose.yaml defines, which the suite already requires.
 */
async function resetTenant(): Promise<void> {
  await run('docker', [
    'exec',
    'jobtracker-postgres',
    'psql',
    '-U',
    'jobtracker',
    '-d',
    'jobtracker',
    '-v',
    'ON_ERROR_STOP=1',
    '-c',
    `DELETE FROM jobs.jobs WHERE organization_id = '${ORGANIZATION_ID}'`,
  ]);
}

export const test = base.extend<{ cleanTenant: void; jobs: JobsApi }>({
  cleanTenant: [
    async ({}, use) => {
      await resetTenant();
      await use();
    },
    { auto: true },
  ],

  jobs: async ({}, use) => {
    const context = await request.newContext({
      baseURL: API_URL,
      extraHTTPHeaders: { 'x-api-version': '1.0' },
      ignoreHTTPSErrors: true,
    });

    await use(new JobsApi(context));
    await context.dispose();
  },
});

export { expect };
