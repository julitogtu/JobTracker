'use client';

import type { Job } from '@/domain/entities/job.entity';
import type { SortField } from '@/presentation/stores/use-jobs.store';
import { JobRow } from '../molecules/job-row.component';

interface JobListProps {
  jobs: Job[];
  selectedJobIds: string[];
  onSelectedChange: (jobId: string) => void;
  onComplete: (job: Job) => void;
  onSort: (field: SortField) => void;
}

/** Presentational: every value and handler is passed in. */
export function JobList({
  jobs,
  selectedJobIds,
  onSelectedChange,
  onComplete,
  onSort,
}: JobListProps) {
  return jobs.length === 0 ? (
    <div className="card empty">
      <p>No jobs match the current filters.</p>
    </div>
  ) : (
    <div className="card">
      <table className="table">
        <thead>
          <tr>
            <th scope="col" aria-label="Select" />
            <th scope="col">
              <button type="button" className="th-sort" onClick={() => onSort('title')}>
                Job
              </button>
            </th>
            <th scope="col">Status</th>
            <th scope="col">
              <button
                type="button"
                className="th-sort"
                onClick={() => onSort('scheduledDateUtc')}
              >
                Scheduled
              </button>
            </th>
            <th scope="col" className="row__actions">
              Actions
            </th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <JobRow
              key={job.id}
              job={job}
              isSelected={selectedJobIds.includes(job.id)}
              onSelectedChange={onSelectedChange}
              onComplete={onComplete}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
