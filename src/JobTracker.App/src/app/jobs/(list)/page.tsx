import 'server-only';

import { Suspense } from 'react';

import { getContainer, organizationId } from '@/infrastructure/di/container';
import { JobListSkeleton } from '@/presentation/components/organisms/job-list-skeleton.component';
import { JobsClient } from '@/presentation/views/jobs';

export const dynamic = 'force-dynamic';

interface JobsPageProps {
  searchParams: Promise<{ q?: string; cursor?: string }>;
}

/**
 * The awaited fetch lives in this child, not in the page: Suspense can only show a
 * fallback for work that suspends *inside* its boundary.
 *
 * Reads go through a use case from the DI container -- never through a Server Action.
 */
async function JobsSection({ searchParams }: JobsPageProps) {
  const { q, cursor } = await searchParams;

  const page = await getContainer().listJobs.execute({
    organizationId: organizationId(),
    searchTerm: q ?? null,
    cursor: cursor ?? null,
    pageSize: 25,
  });

  return <JobsClient initialPage={page} />;
}

export default async function JobsPage({ searchParams }: JobsPageProps) {
  const { q, cursor } = await searchParams;

  return (
    <main className="page">
      <header className="page__head">
        <h1>Jobs</h1>
        <p className="hint">
          Server Component fetches the first page through the DI container and hands it to the
          client view as props.
        </p>
      </header>

      <Suspense key={`${q ?? ''}:${cursor ?? ''}`} fallback={<JobListSkeleton />}>
        <JobsSection searchParams={searchParams} />
      </Suspense>
    </main>
  );
}
