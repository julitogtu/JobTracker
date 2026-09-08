'use client';

import { useCallback, useEffect, useRef } from 'react';
import { useShallow } from 'zustand/react/shallow';

import type { Job, JobPage } from '@/domain/entities/job.entity';
import {
  selectHasPendingWrites,
  selectPagination,
  selectSelectedJobIds,
  type PaginationState,
} from '@/presentation/stores/use-jobs.store';
import { useJobsStore } from '@/presentation/stores/jobs-store.provider';
import { useCompleteJob, type UseCompleteJobResult } from '../features/complete-job';
import { useCreateJob, type UseCreateJobResult } from '../features/create-job';
import { useFilterJobs, type UseFilterJobsResult } from '../features/filter-jobs';

export interface UseJobsPageResult {
  filter: UseFilterJobsResult;
  createJob: UseCreateJobResult;
  completeJob: UseCompleteJobResult;
  selectedJobIds: string[];
  pagination: PaginationState;
  hasPendingWrites: boolean;
  toggleSelected: (jobId: string) => void;
  clearSelection: () => void;
  onCompleteRequested: (job: Job) => void;
}

/** Orchestrates the three verb slices and seeds the store from the Server Component's props. */
export function useJobsPage(initialPage: JobPage): UseJobsPageResult {
  const hydrate = useJobsStore((state) => state.hydrate);
  const toggleSelected = useJobsStore((state) => state.toggleSelected);
  const clearSelection = useJobsStore((state) => state.clearSelection);
  const selectedJobIds = useJobsStore(useShallow(selectSelectedJobIds));
  const pagination = useJobsStore(useShallow(selectPagination));
  const hasPendingWrites = useJobsStore(selectHasPendingWrites);

  // Initial state is seeded by createJobsStore in the provider, so SSR already has the
  // rows. This only re-seeds when the server sends a *fresh* page after revalidatePath.
  const lastPage = useRef(initialPage);
  useEffect(() => {
    lastPage.current === initialPage
      ? undefined
      : ((lastPage.current = initialPage), hydrate(initialPage));
  }, [initialPage, hydrate]);

  const filter = useFilterJobs();
  const completeJob = useCompleteJob();
  const onCreated = useCallback((): void => clearSelection(), [clearSelection]);
  const createJob = useCreateJob(onCreated);

  const onCompleteRequested = useCallback(
    (job: Job): void => completeJob.open(job),
    [completeJob],
  );

  return {
    filter,
    createJob,
    completeJob,
    selectedJobIds,
    pagination,
    hasPendingWrites,
    toggleSelected,
    clearSelection,
    onCompleteRequested,
  };
}
