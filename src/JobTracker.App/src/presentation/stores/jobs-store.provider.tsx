'use client';

import { createContext, useContext, useRef, type ReactNode } from 'react';
import { useStore } from 'zustand';

import type { JobPage } from '@/domain/entities/job.entity';
import { createJobsStore, type JobsState, type JobsStore } from './use-jobs.store';

const JobsStoreContext = createContext<JobsStore | null>(null);

/**
 * One store per mounted view, seeded from the Server Component's props, so SSR renders the
 * real list instead of an empty one and concurrent server requests never share state.
 */
export function JobsStoreProvider({
  initialPage,
  children,
}: {
  initialPage: JobPage;
  children: ReactNode;
}) {
  const storeRef = useRef<JobsStore | null>(null);
  storeRef.current ??= createJobsStore(initialPage);

  return (
    <JobsStoreContext.Provider value={storeRef.current}>{children}</JobsStoreContext.Provider>
  );
}

export function useJobsStore<T>(selector: (state: JobsState) => T): T {
  const store = useContext(JobsStoreContext);

  return store === null
    ? (() => {
        throw new Error('useJobsStore must be used inside <JobsStoreProvider>.');
      })()
    : useStore(store, selector);
}
