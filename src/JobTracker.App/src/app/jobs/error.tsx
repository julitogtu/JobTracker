'use client';

import { useEffect } from 'react';

/** Route error boundary. `reset` re-renders the segment, which retries the server fetch. */
export default function JobsError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[JobTracker] /jobs failed', error);
  }, [error]);

  return (
    <main className="page">
      <div className="card alert alert--error">
        <h1>Could not load jobs</h1>
        <p>{error.message}</p>
        <p className="hint">
          The API must be running: <code>dotnet run --project src/JobTracker.Api</code>, with
          Postgres up via <code>docker compose up -d</code>.
        </p>
        {error.digest === undefined ? null : <p className="hint">Digest: {error.digest}</p>}
        <button type="button" className="btn btn--primary" onClick={reset}>
          Retry
        </button>
      </div>
    </main>
  );
}
