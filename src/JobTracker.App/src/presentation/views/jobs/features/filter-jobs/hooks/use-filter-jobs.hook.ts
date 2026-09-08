'use client';

import { useMemo } from 'react';
import { useShallow } from 'zustand/react/shallow';

import { JobStatus, type Job } from '@/domain/entities/job.entity';
import {
  selectFilteredJobs,
  selectFilters,
  selectSortConfig,
  selectStatusTotals,
  type JobFilters,
  type SortConfig,
  type SortField,
} from '@/presentation/stores/use-jobs.store';
import { useJobsStore } from '@/presentation/stores/jobs-store.provider';

export interface JobsSummary {
  visible: number;
  total: number;
  inProgress: number;
  completed: number;
}

export interface UseFilterJobsResult {
  filters: JobFilters;
  filteredJobs: Job[];
  sortConfig: SortConfig;
  summary: JobsSummary;
  isFiltered: boolean;
  setSearchTerm: (value: string) => void;
  toggleStatus: (status: JobStatus) => void;
  setDateRange: (fromDate: string, toDate: string) => void;
  resetFilters: () => void;
  setSort: (field: SortField) => void;
}

export function useFilterJobs(): UseFilterJobsResult {
  // Derived state comes from selectors, never from useEffect + setState.
  // useShallow keeps the new array/object each selector returns from re-rendering on
  // unrelated store changes.
  const filters = useJobsStore(useShallow(selectFilters));
  const sortConfig = useJobsStore(useShallow(selectSortConfig));
  const filteredJobs = useJobsStore(useShallow(selectFilteredJobs));
  const statusTotals = useJobsStore(useShallow(selectStatusTotals));
  const totalJobs = useJobsStore((state) => state.jobs.length);

  const setSearchTerm = useJobsStore((state) => state.setSearchTerm);
  const toggleStatus = useJobsStore((state) => state.toggleStatus);
  const setDateRange = useJobsStore((state) => state.setDateRange);
  const resetFilters = useJobsStore((state) => state.resetFilters);
  const setSort = useJobsStore((state) => state.setSort);

  // useMemo for derived totals -- recomputed only when the inputs actually change.
  const summary = useMemo<JobsSummary>(
    () => ({
      visible: filteredJobs.length,
      total: totalJobs,
      inProgress: statusTotals[JobStatus.InProgress] ?? 0,
      completed: statusTotals[JobStatus.Completed] ?? 0,
    }),
    [filteredJobs.length, totalJobs, statusTotals],
  );

  const isFiltered = useMemo(
    () =>
      filters.searchTerm.trim() !== '' ||
      filters.statuses.length > 0 ||
      filters.fromDate !== '' ||
      filters.toDate !== '',
    [filters],
  );

  return {
    filters,
    filteredJobs,
    sortConfig,
    summary,
    isFiltered,
    setSearchTerm,
    toggleStatus,
    setDateRange,
    resetFilters,
    setSort,
  };
}
