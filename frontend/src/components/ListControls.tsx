import { useId } from 'react';
import buttons from './buttons.module.css';
import styles from './ListControls.module.css';
import { pageSize, type ListParams, type StatusFilter } from './listParams';
import { useLiveSearch } from './useLiveSearch';

/**
 * The search and the status filter of a list, sent while the user types (ADR 0039 point 1, amended 2026-11-25): the search after a
 * pause, the status when it is chosen. `searchLabel` says what the search looks in.
 */
export function ListFilters({
  params,
  onChange,
  searchLabel,
}: {
  params: ListParams;
  onChange: (next: Partial<ListParams>, options?: { replace?: boolean }) => void;
  searchLabel: string;
}) {
  // A search replaces the entry of the address, so Back leaves the list and does not walk through every key.
  const search = useLiveSearch(params.search, (text) => onChange({ search: text }, { replace: true }));
  const searchId = useId();
  const statusId = useId();

  return (
    <form className={styles.filters} role="search" onSubmit={(event) => event.preventDefault()}>
      <div className={styles.field}>
        <label htmlFor={searchId}>{searchLabel}</label>
        <input id={searchId} className={styles.input} type="search" autoComplete="off" {...search} />
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
