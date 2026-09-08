import Link from 'next/link';

/**
 * Site chrome. A Server Component -- no state, no client JS, so it costs nothing in the
 * browser bundle.
 */
export function SiteHeader() {
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link href="/jobs" className="brand" aria-label="JobTracker home">
          <span className="brand__mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 7h16M4 12h10M4 17h7" />
              <circle cx="18.5" cy="16.5" r="3.5" />
              <path d="m21.5 19.5 1.2 1.2" />
            </svg>
          </span>
          <span className="brand__text">
            Job<span className="brand__accent">Tracker</span>
          </span>
        </Link>

        <nav className="site-nav" aria-label="Main">
          <Link className="site-nav__link" href="/jobs">
            Jobs
          </Link>
          <span className="site-nav__env" title="Data source">
            <span className="dot" aria-hidden="true" />
            .NET API
          </span>
        </nav>
      </div>
    </header>
  );
}
