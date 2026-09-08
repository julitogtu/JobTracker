'use client';

import { canComplete, type Job } from '@/domain/entities/job.entity';
import { StatusBadge } from '@/presentation/components/atoms/status-badge.component';

/**
 * Controlled Component: owns no state. The parent supplies `isSelected` and is notified
 * through `onSelectedChange` -- value + onChange, the same contract as an <input>.
 */
interface JobRowProps {
  job: Job;
  isSelected: boolean;
  onSelectedChange: (jobId: string) => void;
  onComplete: (job: Job) => void;
}

function formatScheduled(value: string | null): string {
  return value === null ? 'Unscheduled' : new Date(value).toLocaleString();
}

export function JobRow({ job, isSelected, onSelectedChange, onComplete }: JobRowProps) {
  return (
    <tr className={isSelected ? 'row row--selected' : 'row'}>
      <td>
        <input
          type="checkbox"
          checked={isSelected}
          onChange={() => onSelectedChange(job.id)}
          aria-label={`Select ${job.title}`}
        />
      </td>
      <td>
        <span className="row__title">{job.title}</span>
        <span className="row__sub">
          {job.address.city}, {job.address.state}
        </span>
      </td>
      <td>
        <StatusBadge status={job.status} />
      </td>
      <td>{formatScheduled(job.scheduledDateUtc)}</td>
      <td className="row__actions">
        {canComplete(job.status) ? (
          <button type="button" className="btn btn--sm" onClick={() => onComplete(job)}>
            Complete
          </button>
        ) : (
          <span className="row__muted">&mdash;</span>
        )}
      </td>
    </tr>
  );
}
