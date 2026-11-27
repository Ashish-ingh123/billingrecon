import { keepPreviousData, useQuery } from '@tanstack/react-query';

/**
 * A page of a list (Prompt 12.2; ADR 0063): the rows on screen stay while a newer page is read, and a request a
 * newer one has replaced is cancelled (its `signal`), so an answer that comes late is never shown.
 */
export function useListQuery<T>(key: readonly unknown[], read: (signal: AbortSignal) => Promise<T>) {
  return useQuery({
    queryKey: key,
    queryFn: ({ signal }) => read(signal),
    placeholderData: keepPreviousData,
  });
}

/** The query string of a list: the API's names, with what is not set left out. */
export function apiQuery(parts: Record<string, string | number | undefined>): Record<string, string | number> {
  return Object.fromEntries(
    Object.entries(parts).filter(
      (entry): entry is [string, string | number] => entry[1] !== undefined && entry[1] !== '',
    ),
  );
}
