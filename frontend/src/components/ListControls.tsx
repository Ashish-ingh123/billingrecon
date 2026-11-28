import { useId, type ReactNode } from 'react';
import buttons from './buttons.module.css';
import styles from './ListControls.module.css';
import { pageSize, type ListParams, type StatusFilter } from './listParams';
import { filtersInUse } from './table/listState';
import { SearchInput } from './SearchInput';
import { useLiveSearch } from './useLiveSearch';

/**
 * The search and the status filter of a list, sent while the user types (ADR 0039 point 1, amended 2026-11-25): the search after a
 * pause, the status when it is chosen. `searchLabel` says what the search looks in. A list's own filters come after
 * them (`children`); with `inUse` and `onClear` the bar says how many filters are in use, in words, and offers "Clear
 * filters" (Prompt 12.2, ADR 0063), on a line of its own so that nothing moves when it comes and goes.
 */
export function ListFilters({
  params,
  onChange,
  searchLabel,
  busy = false,
  children,
  inUse,
  onClear,
}: {
  params: Pick<ListParams, 'search' | 'status'>;
  onChange: (next: Partial<Pick<ListParams, 'search' | 'status'>>, options?: { replace?: boolean }) => void;
  searchLabel: string;
  /** Whether the list is reading: the search box shows its spinner in the lens's place. */
  busy?: boolean;
  children?: ReactNode;
  inUse?: number;
  onClear?: () => void;
}) {
  // A search replaces the entry of the address, so Back leaves the list and does not walk through every key.
  const search = useLiveSearch(params.search, (text) => onChange({ search: text }, { replace: true }));
  const searchId = useId();
  const statusId = useId();

  return (
    <form className={styles.filters} role="search" onSubmit={(event) => event.preventDefault()}>
      <div className={styles.field}>
        <label htmlFor={searchId}>{searchLabel}</label>
        <SearchInput id={searchId} type="search" busy={busy} {...search} />
      </div>
      <div className={styles.field}>
        <label htmlFor={statusId}>Status</label>
        <select
          id={statusId}
          className={styles.input}
          value={params.status ?? ''}
          onChange={(event) => onChange({ status: (event.target.value || undefined) as StatusFilter })}
        >
          <option value="">All</option>
          <option value="Active">Active</option>
          <option value="Inactive">Inactive</option>
        </select>
      </div>
      {children}
      {inUse !== undefined && (
        // Polite, and no `status` role: the loading line of a list stays the one status of the page.
        <p className={styles.inUse} aria-live="polite">
          <span>{filtersInUse(inUse)}</span>
          {inUse > 0 && onClear !== undefined && (
            <button type="button" className={buttons.link} onClick={onClear}>
              Clear filters
            </button>
          )}
        </p>
      )}
    </form>
  );
}

/** Where a list is: the records it holds, the page, and the way to the previous and next pages. */
export function Pagination({
  page,
  totalCount,
  onChange,
}: {
  page: number;
  totalCount: number;
  onChange: (page: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <nav className={styles.pagination} aria-label="Pages">
      <span>
        {totalCount === 1 ? '1 record' : `${totalCount} records`}, page {Math.min(page, pages)} of {pages}
      </span>
      <button type="button" className={buttons.secondary} disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Previous
      </button>
      <button type="button" className={buttons.secondary} disabled={page >= pages} onClick={() => onChange(page + 1)}>
        Next
      </button>
    </nav>
  );
}
