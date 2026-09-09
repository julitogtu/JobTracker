import { expect, test } from './fixtures/jobs-api';

/**
 * Filtering is entirely client-side: the Server Component ships one page of rows, the Zustand
 * store holds them, and selectors derive the filtered view. Nothing here should produce a
 * network request, which is what makes it worth asserting in a browser rather than against
 * the store in isolation.
 */
test.describe('filtering', () => {
  test('narrows the list by search term', async ({ page, jobs }) => {
    const furnace = await jobs.seed({ title: 'Replace furnace igniter' });
    const sink = await jobs.seed({ title: 'Unclog kitchen sink' });

    await page.goto('/jobs');
    await expect(page.getByText('2 of 2')).toBeVisible();

    await page.getByLabel('Search').fill('furnace');

    await expect(page.getByRole('row').filter({ hasText: furnace.title })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: sink.title })).toHaveCount(0);
    await expect(page.getByText('1 of 2')).toBeVisible();
  });

  test('matches on description as well as title', async ({ page, jobs }) => {
    const match = await jobs.seed({ description: 'Compressor replacement on the roof unit.' });
    await jobs.seed({ description: 'Drain cleaning.' });

    await page.goto('/jobs');
    await page.getByLabel('Search').fill('compressor');

    await expect(page.getByRole('row').filter({ hasText: match.title })).toBeVisible();
    await expect(page.getByText('1 of 2')).toBeVisible();
  });

  test('shows the empty state when nothing matches', async ({ page, jobs }) => {
    await jobs.seed();

    await page.goto('/jobs');
    await page.getByLabel('Search').fill('nothing-matches-this');

    await expect(page.getByText('No jobs match the current filters.')).toBeVisible();
    await expect(page.getByText('0 of 1')).toBeVisible();
  });

  test('filters by status chip and reflects it in aria-pressed', async ({ page, jobs }) => {
    const draft = await jobs.seed();
    const inProgress = await jobs.seedInProgress();

    await page.goto('/jobs');

    const chip = page.getByRole('button', { name: 'In progress' });
    await expect(chip).toHaveAttribute('aria-pressed', 'false');

    await chip.click();

    await expect(chip).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('row').filter({ hasText: inProgress.title })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: draft.title })).toHaveCount(0);
  });

  test('combines several status chips', async ({ page, jobs }) => {
    const draft = await jobs.seed();
    const scheduled = await jobs.seed({ scheduledInMinutes: 60 });
    const inProgress = await jobs.seedInProgress();

    await page.goto('/jobs');

    await page.getByRole('button', { name: 'Draft' }).click();
    await page.getByRole('button', { name: 'In progress' }).click();

    await expect(page.getByRole('row').filter({ hasText: draft.title })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: inProgress.title })).toBeVisible();
    await expect(page.getByRole('row').filter({ hasText: scheduled.title })).toHaveCount(0);
    await expect(page.getByText('2 of 3')).toBeVisible();
  });

  test('toggles a status chip back off', async ({ page, jobs }) => {
    await jobs.seed();
    await jobs.seedInProgress();

    await page.goto('/jobs');

    const chip = page.getByRole('button', { name: 'In progress' });
    await chip.click();
    await expect(page.getByText('1 of 2')).toBeVisible();

    await chip.click();
    await expect(chip).toHaveAttribute('aria-pressed', 'false');
    await expect(page.getByText('2 of 2')).toBeVisible();
  });

  test('offers Clear filters only once a filter is applied', async ({ page, jobs }) => {
    await jobs.seed();

    await page.goto('/jobs');

    const clear = page.getByRole('button', { name: 'Clear filters' });
    await expect(clear).toHaveCount(0);

    await page.getByLabel('Search').fill('anything');
    await expect(clear).toBeVisible();

    await clear.click();

    await expect(clear).toHaveCount(0);
    await expect(page.getByLabel('Search')).toHaveValue('');
    await expect(page.getByText('1 of 1')).toBeVisible();
  });

  test('clears the search term and the status chips together', async ({ page, jobs }) => {
    await jobs.seed();
    await jobs.seedInProgress();

    await page.goto('/jobs');

    await page.getByLabel('Search').fill('E2E');
    await page.getByRole('button', { name: 'In progress' }).click();
    await expect(page.getByText('1 of 2')).toBeVisible();

    await page.getByRole('button', { name: 'Clear filters' }).click();

    await expect(page.getByRole('button', { name: 'In progress' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    await expect(page.getByText('2 of 2')).toBeVisible();
  });

  test('filters without navigating or refetching', async ({ page, jobs }) => {
    await jobs.seed({ title: 'Replace furnace igniter' });
    await jobs.seed({ title: 'Unclog kitchen sink' });

    await page.goto('/jobs');

    const requests: string[] = [];
    page.on('request', (request) => requests.push(request.url()));

    await page.getByLabel('Search').fill('furnace');
    await expect(page.getByText('1 of 2')).toBeVisible();

    // Derived from store selectors, so the filter costs nothing over the wire.
    expect(requests).toHaveLength(0);
  });
});
