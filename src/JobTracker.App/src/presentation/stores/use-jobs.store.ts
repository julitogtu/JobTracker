import { createStore } from 'zustand/vanilla';

import type { Job, JobPage, JobStatus } from '@/domain/entities/job.entity';

export type SortField = 'scheduledDateUtc' | 'title' | 'updatedAtUtc';
export type SortDirection = 'asc' | 'desc';

export interface SortConfig {
  field: SortField;
  direction: SortDirection;
}

export interface JobFilters {
  searchTerm: string;
  statuses: JobStatus[];
  fromDate: string;
  toDate: string;
}

export interface PaginationState {
  pageSize: number;
  nextCursor: string | null;
  hasNextPage: boolean;
}

interface OptimisticSnapshot {
  jobId: string;
  previousStatus: JobStatus;
}

export interface JobsState {
  jobs: Job[];
  selectedJobIds: string[];
  filters: JobFilters;
  pagination: PaginationState;
  sortConfig: SortConfig;
  optimistic: Record<string, OptimisticSnapshot>;

  hydrate: (page: JobPage) => void;
  setSearchTerm: (searchTerm: string) => void;
  toggleStatus: (status: JobStatus) => void;
  setDateRange: (fromDate: string, toDate: string) => void;
  resetFilters: () => void;
  toggleSelected: (jobId: string) => void;
  clearSelection: () => void;
  setSort: (field: SortField) => void;
  beginStatusChange: (jobId: string, nextStatus: JobStatus) => string;
  commitStatusChange: (token: string) => void;
  rollbackStatusChange: (token: string) => void;
}

const EMPTY_FILTERS: JobFilters = {
  searchTerm: '',
  statuses: [],
  fromDate: '',
  toDate: '',
};

export type JobsStore = ReturnType<typeof createJobsStore>;

/**
 * Client-side UI state only.
 *
 * `jobs` is a hydrated working copy, not a second source of truth: it is seeded from the
 * Server Component's props and is never fetched here. It exists so a status change can be
 * applied optimistically and rolled back on failure -- impossible without a local copy.
 * The server stays authoritative; revalidatePath('/jobs') re-hydrates after every mutation.
 *
 * A FACTORY, not a module-level singleton, for two reasons:
 *  1. Zustand v5 serves `getInitialState` as the useSyncExternalStore server snapshot, so a
 *     store mutated during render is invisible to SSR -- the list would render empty and
 *     only fill in after hydration. Seeding the initial state fixes that.
 *  2. A module-level store on the server is shared by every concurrent request, which would
 *     leak one tenant's jobs into another's response.
 */
export function createJobsStore(initialPage: JobPage) {
  return createStore<JobsState>()((set) => ({
    jobs: initialPage.items,
    selectedJobIds: [],
    filters: EMPTY_FILTERS,
    pagination: {
      pageSize: initialPage.pageSize,
      nextCursor: initialPage.nextCursor,
      hasNextPage: initialPage.hasNextPage,
    },
    sortConfig: { field: 'scheduledDateUtc', direction: 'asc' },
    optimistic: {},

    hydrate: (page) =>
      set((state) => ({
        jobs: page.items,
        optimistic: {},
        pagination: {
          pageSize: page.pageSize,
          nextCursor: page.nextCursor,
          hasNextPage: page.hasNextPage,
        },
        // Drop selections for rows that no longer exist in the fresh page.
        selectedJobIds: state.selectedJobIds.filter((id) =>
          page.items.some((job) => job.id === id),
        ),
      })),

    setSearchTerm: (searchTerm) =>
      set((state) => ({ filters: { ...state.filters, searchTerm } })),

    toggleStatus: (status) =>
      set((state) => ({
        filters: {
          ...state.filters,
          statuses: state.filters.statuses.includes(status)
            ? state.filters.statuses.filter((value) => value !== status)
            : [...state.filters.statuses, status],
        },
      })),

    setDateRange: (fromDate, toDate) =>
      set((state) => ({ filters: { ...state.filters, fromDate, toDate } })),

    resetFilters: () => set({ filters: EMPTY_FILTERS }),

    toggleSelected: (jobId) =>
      set((state) => ({
        selectedJobIds: state.selectedJobIds.includes(jobId)
          ? state.selectedJobIds.filter((id) => id !== jobId)
          : [...state.selectedJobIds, jobId],
      })),

    clearSelection: () => set({ selectedJobIds: [] }),

    setSort: (field) =>
      set((state) => ({
        sortConfig: {
          field,
          direction:
            state.sortConfig.field === field && state.sortConfig.direction === 'asc'
              ? 'desc'
              : 'asc',
        },
      })),

    /** Applies the new status immediately and returns a token used to commit or roll back. */
    beginStatusChange: (jobId, nextStatus) => {
      const token = crypto.randomUUID();

      set((state) => {
        const target = state.jobs.find((job) => job.id === jobId);

        return target === undefined
          ? state
          : {
              jobs: state.jobs.map((job) =>
                job.id === jobId ? { ...job, status: nextStatus } : job,
              ),
              optimistic: {
                ...state.optimistic,
                [token]: { jobId, previousStatus: target.status },
              },
            };
      });

      return token;
    },

    commitStatusChange: (token) =>
      set((state) => {
        const { [token]: _committed, ...rest } = state.optimistic;
        return { optimistic: rest };
      }),

    rollbackStatusChange: (token) =>
      set((state) => {
        const snapshot = state.optimistic[token];

        if (snapshot === undefined) {
          return state;
        }

        const { [token]: _reverted, ...rest } = state.optimistic;

        return {
          jobs: state.jobs.map((job) =>
            job.id === snapshot.jobId ? { ...job, status: snapshot.previousStatus } : job,
          ),
          optimistic: rest,
        };
      }),
}));
}

/* ---------------------------------------------------------------------------------------
 * Selectors -- derived state is computed here, never with useEffect + setState.
 * ------------------------------------------------------------------------------------ */

export const selectJobs = (state: JobsState): Job[] => state.jobs;
export const selectFilters = (state: JobsState): JobFilters => state.filters;
export const selectSortConfig = (state: JobsState): SortConfig => state.sortConfig;
export const selectPagination = (state: JobsState): PaginationState => state.pagination;
export const selectSelectedJobIds = (state: JobsState): string[] => state.selectedJobIds;
export const selectHasPendingWrites = (state: JobsState): boolean =>
  Object.keys(state.optimistic).length > 0;

function matchesFilters(job: Job, filters: JobFilters): boolean {
  const term = filters.searchTerm.trim().toLowerCase();
  const matchesTerm =
    term === ''
      ? true
      : job.title.toLowerCase().includes(term) ||
        job.description.toLowerCase().includes(term);

  const matchesStatus =
    filters.statuses.length === 0 ? true : filters.statuses.includes(job.status);

  const scheduled = job.scheduledDateUtc;
  const matchesFrom =
    filters.fromDate === '' || scheduled === null ? true : scheduled >= filters.fromDate;
  const matchesTo =
    filters.toDate === '' || scheduled === null ? true : scheduled <= `${filters.toDate}T23:59:59Z`;

  return matchesTerm && matchesStatus && matchesFrom && matchesTo;
}

function compareJobs(left: Job, right: Job, sortConfig: SortConfig): number {
  const factor = sortConfig.direction === 'asc' ? 1 : -1;

  const leftValue =
    sortConfig.field === 'title' ? left.title : (left[sortConfig.field] ?? '');
  const rightValue =
    sortConfig.field === 'title' ? right.title : (right[sortConfig.field] ?? '');

  return leftValue === rightValue ? 0 : (leftValue < rightValue ? -1 : 1) * factor;
}

/**
 * Derived selector. Returns a new array, so consume it with `useShallow` to keep
 * referential churn from re-rendering the list on unrelated state changes.
 */
export const selectFilteredJobs = (state: JobsState): Job[] =>
  state.jobs
    .filter((job) => matchesFilters(job, state.filters))
    .sort((left, right) => compareJobs(left, right, state.sortConfig));

export const selectStatusTotals = (state: JobsState): Record<number, number> =>
  state.jobs.reduce<Record<number, number>>((totals, job) => {
    totals[job.status] = (totals[job.status] ?? 0) + 1;
    return totals;
  }, {});
