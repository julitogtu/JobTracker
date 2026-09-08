import { Skeleton } from '@/presentation/components/atoms/skeleton.component';

/** Server-safe: rendered both by <Suspense fallback> and by app/jobs/loading.tsx. */
export function JobListSkeleton({ rows = 6 }: { rows?: number }) {
  return (
    <div className="card" role="status" aria-label="Loading jobs">
      <div className="skeleton-row skeleton-row--head">
        <Skeleton width="28%" />
        <Skeleton width="14%" />
        <Skeleton width="20%" />
        <Skeleton width="12%" />
      </div>
      {Array.from({ length: rows }, (_, index) => (
        <div className="skeleton-row" key={index}>
          <Skeleton width="34%" height={14} />
          <Skeleton width="12%" height={14} />
          <Skeleton width="22%" height={14} />
          <Skeleton width="10%" height={14} />
        </div>
      ))}
    </div>
  );
}
