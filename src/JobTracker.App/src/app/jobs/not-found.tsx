import Link from 'next/link';

/** Custom 404 for the /jobs segment -- rendered whenever notFound() is called below it. */
export default function JobNotFound() {
  return (
    <main className="page">
      <div className="card empty">
        <h1>Job not found</h1>
        <p>
          No job with that id exists in this organization, or it belongs to a different tenant.
        </p>
        <Link className="btn btn--primary" href="/jobs">
          Back to all jobs
        </Link>
      </div>
    </main>
  );
}
