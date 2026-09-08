'use client';

import type { JobPage } from '@/domain/entities/job.entity';
import { ErrorBoundary } from '@/presentation/components/organisms/error-boundary.component';
import { JobsStoreProvider } from '@/presentation/stores/jobs-store.provider';
import { CompleteJobModal } from '../../features/complete-job';
import { CreateJobModal } from '../../features/create-job';
import { FilterBar } from '../../features/filter-jobs';
import { useJobsPage } from '../../hooks/use-jobs-page.hook';
import { JobList } from './job-list.component';

/**
 * Thin shell. It holds no state and defines no handlers: useJobsPage orchestrates the
 * create-job / filter-jobs / complete-job slices and this component only wires the result
 * into markup.
 */
export function JobsClient({ initialPage }: { initialPage: JobPage }) {
  return (
    <JobsStoreProvider initialPage={initialPage}>
      <JobsView initialPage={initialPage} />
    </JobsStoreProvider>
  );
}

function JobsView({ initialPage }: { initialPage: JobPage }) {
  const page = useJobsPage(initialPage);
  const { filter, createJob, completeJob, selectedJobIds, pagination, hasPendingWrites } = page;

  return (
    <section className="stack">
      <div className="toolbar">
        <div className="toolbar__meta">
          <strong>
            {filter.summary.visible} of {filter.summary.total}
          </strong>
          <span className="row__sub">
            {filter.summary.inProgress} in progress &middot; {filter.summary.completed} completed
          </span>
          {hasPendingWrites ? <span className="pill pill--pending">Saving...</span> : null}
        </div>
        <button type="button" className="btn btn--primary" onClick={createJob.open}>
          New job
        </button>
      </div>

      <FilterBar
        value={filter.filters}
        isFiltered={filter.isFiltered}
        onSearchChange={filter.setSearchTerm}
        onStatusToggle={filter.toggleStatus}
        onDateRangeChange={filter.setDateRange}
        onReset={filter.resetFilters}
      >
        <FilterBar.Search />
        <FilterBar.Status />
        <FilterBar.DateRange />
        <FilterBar.Reset />
      </FilterBar>

      <ErrorBoundary
        fallback={(error, reset) => (
          <div className="card alert alert--error">
            <p>The job list failed to render: {error.message}</p>
            <button type="button" className="btn" onClick={reset}>
              Try again
            </button>
          </div>
        )}
      >
        <JobList
          jobs={filter.filteredJobs}
          selectedJobIds={selectedJobIds}
          onSelectedChange={page.toggleSelected}
          onComplete={page.onCompleteRequested}
          onSort={filter.setSort}
        />
      </ErrorBoundary>

      {pagination.hasNextPage ? (
        <p className="hint">
          More results available &mdash; the API pages by keyset cursor
          (<code>{pagination.nextCursor}</code>).
        </p>
      ) : null}

      <CreateJobModal controller={createJob} />
      <CompleteJobModal controller={completeJob} />
    </section>
  );
}
