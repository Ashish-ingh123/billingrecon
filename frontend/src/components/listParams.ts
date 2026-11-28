import { useSearchParams } from 'react-router';

/** The size of a page of every list (the API's default, `docs/api.md` section 6). */
export const pageSize = 20;

export type StatusFilter = 'Active' | 'Inactive' | undefined;

export interface ListParams {
  search: string;
  status: StatusFilter;
  page: number;
  /** The order asked for, as the API names it (`name`, `-name`); `''` for the list's own (Prompt 12.2, ADR 0063). */
  sort: string;
  /** The filters of the list's own beyond the search and the status (a payer's kind), by their name in the address. */
  extra: Readonly<Record<string, string>>;
}

export type ListChange = Partial<Omit<ListParams, 'extra'>> & { extra?: Readonly<Record<string, string>> };

/**
 * A list's search, status filter, filters of its own, order and page, kept in the address (ADR 0039 point 1):
 * `?search=&status=&<extra>=&sort=&page=`. A new search, filter or order starts again at the first page. Every change
 * takes the place of the entry of the address (amended 2026-11-25 for a search; Prompt 12.2 for the rest), so Back
 * leaves the list.
 */
export function useListParams(extraKeys: readonly string[] = []): ListParams & {
  change: (next: ListChange, options?: { replace?: boolean }) => void;
  /** The filters in use: the search, the status and each filter of the list's own that is set. */
  inUse: number;
  /** Empties the search, the status and the list's own filters; the order stays. */
  clear: () => void;
} {
  const [params, setParams] = useSearchParams();
  const status = params.get('status');
  const page = Number.parseInt(params.get('page') ?? '1', 10);

  const current: ListParams = {
    search: params.get('search') ?? '',
    status: status === 'Active' || status === 'Inactive' ? status : undefined,
    page: Number.isFinite(page) && page >= 1 ? page : 1,
    sort: params.get('sort') ?? '',
    extra: Object.fromEntries(extraKeys.map((key) => [key, params.get(key) ?? ''])),
  };

  const change = (next: ListChange) => {
    const merged: ListParams = { ...current, ...next, extra: { ...current.extra, ...next.extra } };
    if (next.search !== undefined || 'status' in next || next.sort !== undefined || next.extra !== undefined) {
      merged.page = next.page ?? 1;
    }

    const query = new URLSearchParams();
    if (merged.search.trim() !== '') {
      query.set('search', merged.search.trim());
    }

    if (merged.status !== undefined) {
      query.set('status', merged.status);
    }

    for (const key of extraKeys) {
      const value = merged.extra[key] ?? '';
      if (value !== '') {
        query.set(key, value);
      }
    }

    if (merged.sort !== '') {
      query.set('sort', merged.sort);
    }

    if (merged.page > 1) {
      query.set('page', String(merged.page));
    }

    setParams(query, { replace: true });
  };

  const inUse =
    (current.search.trim() === '' ? 0 : 1) +
    (current.status === undefined ? 0 : 1) +
    extraKeys.filter((key) => (current.extra[key] ?? '') !== '').length;

  const clear = () =>
    change({ search: '', status: undefined, extra: Object.fromEntries(extraKeys.map((key) => [key, ''])) });

  return { ...current, change, inUse, clear };
}

/** The query of a list request: the API's parameter names, with what is not set left out. */
export function listQuery({
  search,
  status,
  page,
  sort,
}: Pick<ListParams, 'search' | 'status' | 'page'> & { sort?: string }) {
  return {
    ...(search === '' ? {} : { Search: search }),
    ...(status === undefined ? {} : { Status: status }),
    ...(sort === undefined || sort === '' ? {} : { Sort: sort }),
    Page: page,
    PageSize: pageSize,
  };
}
