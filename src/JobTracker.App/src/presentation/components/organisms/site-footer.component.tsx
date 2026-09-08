const TWITTER_HANDLE = 'julitogtu';
const EMAIL = 'julio.avellaneda@outlook.com';

export function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="site-footer__inner">
        <div className="site-footer__brand">
          <p className="site-footer__name">Julio Avellaneda</p>
          <p className="site-footer__role">Full-stack engineer &middot; .NET &amp; React</p>
        </div>

        <nav className="site-footer__contact" aria-label="Contact">
          <a
            className="contact-link"
            href={`https://x.com/${TWITTER_HANDLE}`}
            target="_blank"
            rel="noopener noreferrer"
          >
            <svg viewBox="0 0 24 24" width="15" height="15" fill="currentColor" aria-hidden="true">
              <path d="M18.9 2.6h3.4l-7.4 8.5 8.7 11.5h-6.8l-5.3-7-6.1 7H1.9l7.9-9.1L1.5 2.6h7l4.8 6.4ZM17.7 20.5h1.9L7.4 4.5H5.4Z" />
            </svg>
            <span>@{TWITTER_HANDLE}</span>
          </a>

          <a className="contact-link" href={`mailto:${EMAIL}`}>
            <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <rect x="2.5" y="4.5" width="19" height="15" rx="2.5" />
              <path d="m3 7 9 6 9-6" />
            </svg>
            <span>{EMAIL}</span>
          </a>
        </nav>
      </div>

      <div className="site-footer__meta">
        <span>&copy; {new Date().getFullYear()} Julio Avellaneda</span>
        <span className="site-footer__stack">Next.js 16 &middot; React 19 &middot; .NET 10</span>
      </div>
    </footer>
  );
}
