import { useId, useState, type FormEvent } from 'react';
import buttons from './buttons.module.css';
import styles from './ListControls.module.css';
import { pageSize, type ListParams, type StatusFilter } from './listParams';

/**
 * The search and the status filter of a list, sent when the user submits them (ADR 0039 point 1). `searchLabel` says
 * what the search looks in.
 */
export function ListFilters({
  params,
  onChange,
  searchLabel,
}: {
  params: ListParams;
  onChange: (next: Partial<ListParams>) => void;
  searchLabel: string;
}) {
  const [search, setSearch] = useState(params.search);
  const [seen, setSeen] = useState(params.search);
  const searchId = useId();
  const statusId = useId();

  // When the address changes the search (Back, a link), the box shows it.
  if (params.search !== seen) {
    setSeen(params.search);
    setSearch(params.search);
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onChange({ search });
  };

  return (
    <form className={styles.filters} role="search" onSubmit={submit}>
      <div className={styles.field}>
        <label htmlFor={searchId}>{searchLabel}</label>
        <input
          id={searchId}
          className={styles.input}
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>
      <button type="submit" className={buttons.secondary}>
        Search
      </button>
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
