import { expect, test } from './fixtures/jobs-api';

/**
 * The dynamic route and its two failure boundaries. Nothing in the list links here, so the
 * page is reached by URL — which is exactly why the not-found handling matters.
 */
test.describe('job detail', () => {
  test('renders the job at its own URL', async ({ page, jobs }) => {
    const job = await jobs.seed({ city: 'Dallas', state: 'TX', scheduledInMinutes: 90 });

    await page.goto(`/jobs/${job.id}`);

    await expect(page.getByRole('heading', { name: job.title, level: 1 })).toBeVisible();
    await expect(page.getByText('Seeded by the end-to-end suite.')).toBeVisible();
    await expect(page.getByText('123 Main St, Dallas, TX 78701')).toBeVisible();
    await expect(page.getByRole('link', { name: 'All jobs' })).toBeVisible();
  });

  test('shows Unscheduled for a draft', async ({ page, jobs }) => {
    const job = await jobs.seed();

    await page.goto(`/jobs/${job.id}`);

    await expect(page.getByText('Unscheduled')).toBeVisible();
    await expect(page.getByText('Draft', { exact: true })).toBeVisible();
  });

  test('links back to the list', async ({ page, jobs }) => {
    const job = await jobs.seed();

    await page.goto(`/jobs/${job.id}`);
    await page.getByRole('link', { name: 'All jobs' }).click();

    await expect(page).toHaveURL(/\/jobs$/);
    await expect(page.getByRole('heading', { name: 'Jobs', level: 1 })).toBeVisible();
  });

  /** The route guards the shape before calling the API, so a malformed id never leaves the app. */
  test('renders the 404 page for an id that is not a GUID', async ({ page }) => {
    await page.goto('/jobs/not-a-guid');

    await expect(page.getByRole('heading', { name: 'Job not found' })).toBeVisible();
  });

  /**
   * A well-formed id belonging to nobody. The same page appears for a job in another tenant,
   * which is the point: a cross-tenant read is indistinguishable from a miss.
   */
  test('renders the 404 page for an unknown job', async ({ page }) => {
    await page.goto('/jobs/3f2a1b4c-5d6e-4f70-8912-a3b4c5d6e7f8');

    await expect(page.getByRole('heading', { name: 'Job not found' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Back to all jobs' })).toBeVisible();
  });

  test('recovers from the 404 back to the list', async ({ page, jobs }) => {
    const job = await jobs.seed();

    await page.goto('/jobs/3f2a1b4c-5d6e-4f70-8912-a3b4c5d6e7f8');
    await page.getByRole('link', { name: 'Back to all jobs' }).click();

    await expect(page.getByRole('row').filter({ hasText: job.title })).toBeVisible();
  });
});
