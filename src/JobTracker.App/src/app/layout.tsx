import type { Metadata } from 'next';
import type { ReactNode } from 'react';

import { SiteFooter } from '@/presentation/components/organisms/site-footer.component';
import { SiteHeader } from '@/presentation/components/organisms/site-header.component';

import './globals.css';

export const metadata: Metadata = {
  title: 'JobTracker',
  description: 'Next.js client for the JobTracker API',
  authors: [{ name: 'Julio Avellaneda' }],
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <div className="shell">
          <SiteHeader />
          <div className="shell__main">{children}</div>
          <SiteFooter />
        </div>
      </body>
    </html>
  );
}
