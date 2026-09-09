import { expect, test } from './fixtures/jobs-api';

/**
 * The optimistic write. The row flips to Completed the moment the action is dispatched, then
 * commits on success or rolls back to the captured previous status on failure — behaviour that
 * only exists in the browser, since it lives entirely in the Zustand store.
 *
 * Every job here is seeded straight to InProgress: the React app has no start-job feature, so
 * that status is unreachable through the UI.
 */
test.describe('completing a job', () => {
  const SIGNATURE_URL = 'https://example.com/signatures/e2e.png';

  test('opens the modal titled after the job', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');
    await page
      .getByRole('row')
      .filter({ hasText: job.title })
      .getByRole('button', { name: 'Complete', exact: true })
      .click();

    await expect(page.getByRole('dialog', { name: `Complete "${job.title}"` })).toBeVisible();
  });

  test('requires a signature URL before submitting', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');
    await page
      .getByRole('row')
      .filter({ hasText: job.title })
      .getByRole('button', { name: 'Complete', exact: true })
      .click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });

    await expect(dialog.getByText('A signature URL is required.')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Complete job' })).toBeDisabled();
  });

  test('rejects a signature URL that is not absolute', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');
    await page
      .getByRole('row')
      .filter({ hasText: job.title })
      .getByRole('button', { name: 'Complete', exact: true })
      .click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill('/signatures/abc.png');

    await expect(
      dialog.getByText('Signature URL must be an absolute http or https URL.'),
    ).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Complete job' })).toBeDisabled();
  });

  test('completes the job and flips the row to Completed', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await expect(row.getByText('In progress')).toBeVisible();

    await row.getByRole('button', { name: 'Complete', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill(SIGNATURE_URL);
    await dialog.getByRole('button', { name: 'Complete job' }).click();

    await expect(dialog).toHaveCount(0);
    await expect(row.getByText('Completed')).toBeVisible();

    // Terminal, so the row no longer offers an action.
    await expect(row.getByRole('button', { name: 'Complete', exact: true })).toHaveCount(0);
  });

  test('persists the completion through to the API', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await row.getByRole('button', { name: 'Complete', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill(SIGNATURE_URL);
    await dialog.getByRole('button', { name: 'Complete job' }).click();

    await expect(row.getByText('Completed')).toBeVisible();

    // 4 is JobStatus.Completed: the API serialises the enum as a number.
    await expect
      .poll(async () => (await jobs.get(job.id)).status, { timeout: 10_000 })
      .toBe(4);
  });

  test('survives a reload, so the flip was not only optimistic', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await row.getByRole('button', { name: 'Complete', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill(SIGNATURE_URL);
    await dialog.getByRole('button', { name: 'Complete job' }).click();

    await expect(row.getByText('Completed')).toBeVisible();

    await page.reload();

    await expect(
      page.getByRole('row').filter({ hasText: job.title }).getByText('Completed'),
    ).toBeVisible();
  });

  /**
   * The pessimistic half of the optimistic update: when the API rejects the change, the store
   * rolls the row back to the status it captured before dispatching.
   */
  test('rolls the row back when the API rejects the completion', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await expect(row.getByText('In progress')).toBeVisible();

    // Complete it behind the UI's back, so the browser's attempt hits a terminal job.
    await jobs.complete(job.id);

    await row.getByRole('button', { name: 'Complete', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill(SIGNATURE_URL);
    await dialog.getByRole('button', { name: 'Complete job' }).click();

    // The modal stays open and reports the failure rather than closing on a lie.
    await expect(dialog.locator('.alert--error')).toBeVisible();
    await expect(dialog).toBeVisible();
  });

  test('closes without completing when cancelled', async ({ page, jobs }) => {
    const job = await jobs.seedInProgress();

    await page.goto('/jobs');

    const row = page.getByRole('row').filter({ hasText: job.title });
    await row.getByRole('button', { name: 'Complete', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: `Complete "${job.title}"` });
    await dialog.getByLabel('Signature URL').fill(SIGNATURE_URL);
    await dialog.getByRole('button', { name: 'Cancel' }).click();

    await expect(dialog).toHaveCount(0);
    await expect(row.getByText('In progress')).toBeVisible();
    await expect.poll(async () => (await jobs.get(job.id)).status).toBe(3);
  });
});
