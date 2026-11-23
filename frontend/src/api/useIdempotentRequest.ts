import { useRef } from 'react';
import { ApiError } from './call';

/**
 * Whether the same request may be sent again under the same key and finish the same work (`docs/api.md` section 7): it
 * got no answer, a fault (5xx, which frees the key), or 409 `idempotency_in_progress` (the first is still running).
 * A success or any other refusal ends the operation; a new attempt is a new request with a new key.
 */
export function isRetryable(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.status === 0 || error.status >= 500 || error.problem.errorCode === 'idempotency_in_progress')
  );
}

/**
 * The `Idempotency-Key` of a financial request (ADR 0040 point 2): one key per operation, kept in memory. `identity` is
 * the request as sent (method, path, `If-Match`, body): sending the same identity again while its operation is open
 * reuses the key, so the API does the work at most once; anything else is a new operation with a new key.
 */
export function useIdempotentRequest() {
  const open = useRef<{ identity: string; key: string } | null>(null);

  return async function send<T>(identity: string, request: (key: string) => Promise<T>): Promise<T> {
    const key = open.current?.identity === identity ? open.current.key : crypto.randomUUID();
    open.current = { identity, key };

    try {
      const result = await request(key);
      open.current = null;
      return result;
    } catch (error) {
      if (!isRetryable(error)) {
        open.current = null;
      }

      throw error;
    }
  };
}
