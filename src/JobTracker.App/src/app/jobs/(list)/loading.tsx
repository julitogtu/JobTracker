import { JobListSkeleton } from '@/presentation/components/organisms/job-list-skeleton.component';

/** Route-level skeleton shown while the /jobs segment itself is loading. */
export default function JobsLoading() {
  return (
    <main className="page">
      <header className="page__head">
        <h1>Jobs</h1>
      </header>
      <JobListSkeleton rows={8} />
    </main>
  );
}
