import { useMemo, type ReactNode } from 'react';
import { useAuth } from '../auth/AuthContext';
import { ApiClientContext } from './ApiClientContext';
import { createApiClient } from './client';

/**
 * Provides the app's client, which sends the current user's token and ends the session when the API refuses it. A new
 * session gets a new client, so a request never carries the token of the session before. `fetch` is for tests.
 */
export function ApiClientProvider({
  children,
  fetch,
}: {
  children: ReactNode;
  fetch?: (request: Request) => Promise<Response>;
}) {
  const { session, signOut } = useAuth();
  const token = session?.accessToken;

  const client = useMemo(
    () =>
      createApiClient({
        baseUrl: window.location.origin,
        getToken: () => token,
        onUnauthorized: () => signOut('expired'),
        ...(fetch === undefined ? {} : { fetch }),
      }),
    [token, signOut, fetch],
  );

  return <ApiClientContext value={client}>{children}</ApiClientContext>;
}
