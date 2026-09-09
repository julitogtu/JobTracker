import { expect, test } from './fixtures/jobs-api';

/**
 * The read path end to end: Server Component fetches through the DI container, hands the page
 * to the client view as props, and the store hydrates from those props without a second fetch.
 */
test.describe('jobs list', () => {
  test('redirects the root to /jobs', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/jobs$/);
    await expect(page.getByRole('heading', { name: 'Jobs', level: 1 })).toBeVisible();
  });

  test('shows the empty state for a tenant with no jobs', async ({ page }) => {
    await page.goto('/jobs');

    await expect(page.getByText('No jobs match the current filters.')).toBeVisible();
    await expect(page.getByRole('button', { name: 'New job' })).toBeVisible();
  });

  test('renders a seeded job with its status, location and schedule', async ({ page, jobs }) => {
    const job = await jobs.seed({ city: 'Dallas', state: 'TX', scheduledInMinutes: 120 });

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await expect(row).toBeVisible();
    await expect(row.getByText('Dallas, TX')).toBeVisible();
    await expect(row.getByText('Scheduled', { exact: true })).toBeVisible();

    // Anything but "Unscheduled" means the date column rendered a real timestamp.
    await expect(row.getByText('Unscheduled')).toHaveCount(0);
  });

  test('labels an unscheduled job as Unscheduled and shows it as a Draft', async ({ page, jobs }) => {
    const job = await jobs.seed();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await expect(row.getByText('Unscheduled')).toBeVisible();
    await expect(row.getByText('Draft', { exact: true })).toBeVisible();
  });

  test('offers Complete only for an in-progress job', async ({ page, jobs }) => {
    const draft = await jobs.seed();
    const inProgress = await jobs.seedInProgress();

    await page.goto('/jobs');

    const draftRow = page.getByRole('row').filter({ hasText: draft.title });
    const inProgressRow = page.getByRole('row').filter({ hasText: inProgress.title });

    await expect(inProgressRow.getByText('In progress')).toBeVisible();
    await expect(inProgressRow.getByRole('button', { name: 'Complete', exact: true })).toBeVisible();

    // The row renders an em dash instead of a button when the transition is not allowed.
    await expect(draftRow.getByRole('button', { name: 'Complete', exact: true })).toHaveCount(0);
  });

  test('counts visible and total jobs in the toolbar', async ({ page, jobs }) => {
    await jobs.seed();
    await jobs.seed();
    await jobs.seedInProgress();

    await page.goto('/jobs');

    await expect(page.getByText('3 of 3')).toBeVisible();
    await expect(page.getByText('1 in progress')).toBeVisible();
  });

  test('selects a row through its checkbox', async ({ page, jobs }) => {
    const job = await jobs.seed();

    await page.goto('/jobs');

    const checkbox = page.getByRole('checkbox', { name: `Select ${job.title}` });
    await expect(checkbox).not.toBeChecked();

    await checkbox.check();
    await expect(checkbox).toBeChecked();

    await checkbox.uncheck();
    await expect(checkbox).not.toBeChecked();
  });

  test('is scoped to one organization', async ({ page, jobs }) => {
    const mine = await jobs.seed();

    await page.goto('/jobs');

    // The run owns a private tenant, so its own job is the only row on the page.
    await expect(page.getByRole('row').filter({ hasText: mine.title })).toBeVisible();
    await expect(page.getByText('1 of 1')).toBeVisible();
  });
});
