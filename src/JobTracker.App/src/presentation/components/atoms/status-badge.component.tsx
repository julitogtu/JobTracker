import { JOB_STATUS_LABELS, type JobStatus } from '@/domain/entities/job.entity';

const STATUS_CLASS: Readonly<Record<JobStatus, string>> = {
  1: 'badge badge--draft',
  2: 'badge badge--scheduled',
  3: 'badge badge--progress',
  4: 'badge badge--completed',
  5: 'badge badge--cancelled',
};

export function StatusBadge({ status }: { status: JobStatus }) {
  return <span className={STATUS_CLASS[status]}>{JOB_STATUS_LABELS[status]}</span>;
}
