import { QueryClient } from '@tanstack/react-query';

/** A refusal of the API (4xx) is an answer, not a fault: it is never retried. A fault or no answer is, once. */
function retryOnce(failures: number, error: unknown): boolean {
  const status = typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
  return failures < 1 && !(typeof status === 'number' && status >= 400 && status < 500);
}

/** The query cache of the app (ADR 0038, point 3). */
export function newQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: retryOnce, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  });
}
