import 'server-only';

import Link from 'next/link';
import { notFound } from 'next/navigation';

import { JOB_STATUS_LABELS } from '@/domain/entities/job.entity';
import { getContainer, organizationId } from '@/infrastructure/di/container';

interface JobDetailPageProps {
  params: Promise<{ jobId: string }>;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default async function JobDetailPage({ params }: JobDetailPageProps) {
  const { jobId } = await params;

  // Guards use `if` so notFound()'s `never` return narrows `job` below. The
  // ternary-over-&& rule applies to conditional *rendering*, not control flow.
  if (!GUID.test(jobId)) {
    notFound();
  }

  const job = await getContainer().getJob.execute(organizationId(), jobId);

  if (job === null) {
    notFound(); // renders app/jobs/not-found.tsx
  }

  return (
    <main className="page">
      <header className="page__head">
        <Link className="hint" href="/jobs">
          &larr; All jobs
        </Link>
        <h1>{job.title}</h1>
      </header>

      <div className="card stack">
        <p>{job.description}</p>
        <dl className="detail-grid">
          <dt>Status</dt>
          <dd>{JOB_STATUS_LABELS[job.status]}</dd>
          <dt>Scheduled</dt>
          <dd>
            {job.scheduledDateUtc === null
              ? 'Unscheduled'
              : new Date(job.scheduledDateUtc).toLocaleString()}
          </dd>
          <dt>Address</dt>
          <dd>
            {job.address.street}, {job.address.city}, {job.address.state} {job.address.zipCode}
          </dd>
          <dt>Photos</dt>
          <dd>{job.photoCount}</dd>
        </dl>
      </div>
    </main>
  );
}
