import { useSearchParams } from 'react-router';

/**
 * The filters, the order and the page of a list, kept in the address (Prompt 12.2; ADR 0063): `?status=Posted&sort=-total
 * &page=2`. Every change takes the place of the address's entry instead of adding one, so Back leaves the list rather
 * than stepping through its filters, and a change of a filter or of the order goes back to the first page. A filter that
 * is empty is left out of the address.
 */
export interface ListState<K extends string> {
  /** The value of each filter, `''` when it is not set. */
  values: Readonly<Record<K, string>>;
  /** The order: an API column name, with `-` in front for the largest or latest first; `''` for the list's own. */
  sort: string;
  page: number;
  /** Sets some filters; the page goes back to 1. */
  change: (next: Partial<Record<K, string>>) => void;
  setSort: (sort: string) => void;
  setPage: (page: number) => void;
  /** Empties the filters in `clearable`; the order stays. */
  clear: () => void;
  /** How many of the filters in `clearable` are in use. */
  inUse: number;
}

/**
 * The state of a list. `keys` are every filter the address may hold; `clearable` are those of the filter bar, which
 * "Clear filters" empties and the count says (a list opened for one payer keeps its payer).
 */
export function useListState<K extends string>(keys: readonly K[], clearable: readonly K[] = keys): ListState<K> {
  const [params, setParams] = useSearchParams();
  const values = Object.fromEntries(keys.map((key) => [key, params.get(key) ?? ''])) as Record<K, string>;
  const sort = params.get('sort') ?? '';
  const parsed = Number.parseInt(params.get('page') ?? '1', 10);
  const page = Number.isFinite(parsed) && parsed >= 1 ? parsed : 1;

  const write = (nextValues: Record<K, string>, nextSort: string, nextPage: number) => {
    const query = new URLSearchParams();
    for (const key of keys) {
      const value = nextValues[key].trim();
      if (value !== '') {
        query.set(key, value);
      }
    }

    if (nextSort !== '') {
      query.set('sort', nextSort);
    }

    if (nextPage > 1) {
      query.set('page', String(nextPage));
    }

    setParams(query, { replace: true });
  };

  return {
    values,
    sort,
    page,
    change: (next) => write({ ...values, ...next }, sort, 1),
    setSort: (next) => write(values, next, 1),
    setPage: (next) => write(values, sort, next),
    clear: () => write({ ...values, ...Object.fromEntries(clearable.map((key) => [key, ''])) }, sort, 1),
    inUse: clearable.filter((key) => values[key].trim() !== '').length,
  };
}

/** "No filter in use", "1 filter in use", "3 filters in use". */
export function filtersInUse(count: number): string {
  return count === 0 ? 'No filter in use' : `${String(count)} ${count === 1 ? 'filter' : 'filters'} in use`;
}

/** A value of the address that is one of `list`, or nothing: an unknown status in a link filters nothing. */
export function known<T extends string>(value: string, list: readonly T[]): T | undefined {
  return (list as readonly string[]).includes(value) ? (value as T) : undefined;
}
