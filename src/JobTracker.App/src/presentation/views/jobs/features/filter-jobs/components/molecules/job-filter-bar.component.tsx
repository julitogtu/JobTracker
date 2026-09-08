'use client';

import { createContext, useContext, type ReactNode } from 'react';

import { JOB_STATUS_LABELS, JOB_STATUS_VALUES, type JobStatus } from '@/domain/entities/job.entity';
import type { JobFilters } from '@/presentation/stores/use-jobs.store';

/* -----------------------------------------------------------------------------------------
 * Compound Component.
 *
 * The root owns nothing: it receives the filter value and change handlers from its parent
 * (Controlled Component pattern) and shares them with its children through context, so the
 * caller composes <FilterBar><FilterBar.Status /><FilterBar.DateRange /></FilterBar> in any
 * order without prop drilling.
 * -------------------------------------------------------------------------------------- */

interface FilterBarContextValue {
  value: JobFilters;
  onSearchChange: (value: string) => void;
  onStatusToggle: (status: JobStatus) => void;
  onDateRangeChange: (fromDate: string, toDate: string) => void;
  onReset: () => void;
  isFiltered: boolean;
}

const FilterBarContext = createContext<FilterBarContextValue | null>(null);

function useFilterBarContext(): FilterBarContextValue {
  const context = useContext(FilterBarContext);

  return context === null
    ? (() => {
        throw new Error('FilterBar.* must be rendered inside <FilterBar>.');
      })()
    : context;
}

interface FilterBarRootProps extends FilterBarContextValue {
  children: ReactNode;
}

function FilterBarRoot({ children, ...contextValue }: FilterBarRootProps) {
  return (
    <FilterBarContext.Provider value={contextValue}>
      <section className="filter-bar" aria-label="Job filters">
        {children}
      </section>
    </FilterBarContext.Provider>
  );
}

function FilterBarSearch() {
  const { value, onSearchChange } = useFilterBarContext();

  return (
    <label className="field field--grow">
      <span className="field__label">Search</span>
      <input
        type="search"
        className="input"
        placeholder="Title or description"
        value={value.searchTerm}
        onChange={(event) => onSearchChange(event.target.value)}
      />
    </label>
  );
}

function FilterBarStatus() {
  const { value, onStatusToggle } = useFilterBarContext();

  return (
    <fieldset className="field">
      <legend className="field__label">Status</legend>
      <div className="chip-row">
        {JOB_STATUS_VALUES.map((status) => (
          <button
            key={status}
            type="button"
            className={value.statuses.includes(status) ? 'chip chip--on' : 'chip'}
            aria-pressed={value.statuses.includes(status)}
            onClick={() => onStatusToggle(status)}
          >
            {JOB_STATUS_LABELS[status]}
          </button>
        ))}
      </div>
    </fieldset>
  );
}

function FilterBarDateRange() {
  const { value, onDateRangeChange } = useFilterBarContext();

  return (
    <div className="field-group">
      <label className="field">
        <span className="field__label">Scheduled from</span>
        <input
          type="date"
          className="input"
          value={value.fromDate}
          onChange={(event) => onDateRangeChange(event.target.value, value.toDate)}
        />
      </label>
      <label className="field">
        <span className="field__label">Scheduled to</span>
        <input
          type="date"
          className="input"
          value={value.toDate}
          onChange={(event) => onDateRangeChange(value.fromDate, event.target.value)}
        />
      </label>
    </div>
  );
}

function FilterBarReset() {
  const { onReset, isFiltered } = useFilterBarContext();

  return isFiltered ? (
    <button type="button" className="btn btn--ghost" onClick={onReset}>
      Clear filters
    </button>
  ) : null;
}

export const FilterBar = Object.assign(FilterBarRoot, {
  Search: FilterBarSearch,
  Status: FilterBarStatus,
  DateRange: FilterBarDateRange,
  Reset: FilterBarReset,
});
