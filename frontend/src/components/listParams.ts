import { useSearchParams } from 'react-router';

/** The size of a page of every list (the API's default, `docs/api.md` section 6). */
export const pageSize = 20;

export type StatusFilter = 'Active' | 'Inactive' | undefined;

export interface ListParams {
  search: string;
  status: StatusFilter;
  page: number;
}

/**
 * A list's search, status filter and page, kept in the address (ADR 0039 point 1): `?search=&status=&page=`. A new search
 * or filter starts again at the first page. A change asked with `replace` takes the place of the entry of the address
 * (a search typed key by key, amended 2026-11-25), so Back leaves the list.
 */
export function useListParams(): ListParams & {
  change: (next: Partial<ListParams>, options?: { replace?: boolean }) => void;
} {
  const [params, setParams] = useSearchParams();
  const status = params.get('status');
  const page = Number.parseInt(params.get('page') ?? '1', 10);

  const current: ListParams = {
    search: params.get('search') ?? '',
    status: status === 'Active' || status === 'Inactive' ? status : undefined,
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };

  const change = (next: Partial<ListParams>, options?: { replace?: boolean }) => {
    const merged = { ...current, ...next };
    if (next.search !== undefined || 'status' in next) {
      merged.page = next.page ?? 1;
    }

    const query = new URLSearchParams();
    if (merged.search.trim() !== '') {
      query.set('search', merged.search.trim());
    }

    if (merged.status !== undefined) {
      query.set('status', merged.status);
    }

    if (merged.page > 1) {
      query.set('page', String(merged.page));
    }

    setParams(query, { replace: options?.replace === true });
  };

  return { ...current, change };
}

/** The query of a list request: the API's parameter names, with what is not set left out. */
export function listQuery({ search, status, page }: ListParams) {
  return {
    ...(search === '' ? {} : { Search: search }),
    ...(status === undefined ? {} : { Status: status }),
    Page: page,
    PageSize: pageSize,
  };
}
