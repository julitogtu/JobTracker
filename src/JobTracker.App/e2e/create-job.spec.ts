import type { Locator } from '@playwright/test';

import { expect, test, uniqueTitle } from './fixtures/jobs-api';

/**
 * The write path: modal form -> Server Action -> use case -> .NET API -> PostgreSQL, then
 * revalidatePath('/jobs') sends a fresh server page and the store re-hydrates from it.
 */
test.describe('creating a job', () => {
  const CUSTOMER_ID = '9f2b6c41-3d5e-4a17-8b92-5c4d1e0a7f36';

  async function fillRequiredFields(dialog: Locator, title: string): Promise<void> {
    await dialog.getByLabel('Title').fill(title);
    await dialog.getByLabel('Description').fill('Created by the end-to-end suite.');
    await dialog.getByLabel('Street').fill('120 Main St');
    await dialog.getByLabel('City').fill('Austin');
    await dialog.getByLabel('State').fill('TX');
    await dialog.getByLabel('Zip code').fill('78701');
    await dialog.getByLabel('Latitude').fill('30.2672');
    await dialog.getByLabel('Longitude').fill('-97.7431');
    await dialog.getByLabel('Customer id (GUID)').fill(CUSTOMER_ID);
  }

  test('opens and closes the modal', async ({ page }) => {
    await page.goto('/jobs');

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await expect(dialog).toHaveCount(0);

    await page.getByRole('button', { name: 'New job' }).click();
    await expect(dialog).toBeVisible();

    await dialog.getByRole('button', { name: 'Cancel' }).click();
    await expect(dialog).toHaveCount(0);
  });

  test('closes the modal on Escape', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await expect(dialog).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
  });

  test('keeps submit disabled until the form is valid', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    const submit = dialog.getByRole('button', { name: 'Create job' });

    await expect(submit).toBeDisabled();
    await expect(dialog.getByText('Title is required.')).toBeVisible();
    await expect(dialog.getByText('Customer id must be a GUID.')).toBeVisible();

    await fillRequiredFields(dialog, uniqueTitle('valid'));

    await expect(submit).toBeEnabled();
  });

  test('rejects a customer id that is not a GUID', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await fillRequiredFields(dialog, uniqueTitle('bad-guid'));
    await dialog.getByLabel('Customer id (GUID)').fill('not-a-guid');

    await expect(dialog.getByText('Customer id must be a GUID.')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Create job' })).toBeDisabled();
  });

  test('rejects a latitude outside its range', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await fillRequiredFields(dialog, uniqueTitle('bad-lat'));
    await dialog.getByLabel('Latitude').fill('120');

    await expect(dialog.getByText('Latitude must be between -90 and 90.')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Create job' })).toBeDisabled();
  });

  /**
   * Mirrors the Job.Create domain rule: scheduled date and assignee are supplied together or
   * not at all. The UI enforces it as a form-level error rather than a field-level one.
   */
  test('rejects an assignee without a scheduled date', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await fillRequiredFields(dialog, uniqueTitle('half-scheduled'));
    await dialog.getByLabel('Assignee id (optional GUID)').fill(CUSTOMER_ID);

    await expect(
      dialog.getByText('Scheduled date and assignee must be provided together.'),
    ).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Create job' })).toBeDisabled();
  });

  test('creates a draft and shows it in the list', async ({ page }) => {
    const title = uniqueTitle('created');

    await page.goto('/jobs');
    await expect(page.getByText('No jobs match the current filters.')).toBeVisible();

    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await fillRequiredFields(dialog, title);
    await dialog.getByRole('button', { name: 'Create job' }).click();

    await expect(dialog).toHaveCount(0);

    const row = page.getByRole('row').filter({ hasText: title });
    await expect(row).toBeVisible();
    await expect(row.getByText('Draft', { exact: true })).toBeVisible();
    await expect(row.getByText('Austin, TX')).toBeVisible();
    await expect(row.getByText('Unscheduled')).toBeVisible();
  });

  test('the created job is really in the API, not just the store', async ({ page, jobs }) => {
    const title = uniqueTitle('persisted');

    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await fillRequiredFields(dialog, title);
    await dialog.getByRole('button', { name: 'Create job' }).click();

    await expect(page.getByRole('row').filter({ hasText: title })).toBeVisible();

    // Reloading throws the store away, so surviving a reload proves the write landed.
    await page.reload();
    await expect(page.getByRole('row').filter({ hasText: title })).toBeVisible();
  });

  test('starts from a blank form on reopen', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByRole('button', { name: 'New job' }).click();

    const dialog = page.getByRole('dialog', { name: 'New job' });
    await dialog.getByLabel('Title').fill('discarded draft');
    await dialog.getByRole('button', { name: 'Cancel' }).click();

    await page.getByRole('button', { name: 'New job' }).click();

    await expect(dialog.getByLabel('Title')).toHaveValue('');
  });
});
