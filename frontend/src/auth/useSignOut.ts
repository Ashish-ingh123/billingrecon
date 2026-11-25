import { useCallback } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { useAuth } from './AuthContext';

/**
 * What the Sign out buttons do (ADR 0057): the API is told, so that it refuses the token from now on, on every device,
 * and the session is forgotten here at once, whatever the API answers and whether or not it answers at all. A token the
 * API could not be told about still ends at its expiry.
 */
export function useSignOut(): () => void {
  const client = useApiClient();
  const { signOut } = useAuth();

  return useCallback(() => {
    // Sent with the token of the session that ends: the client was made for it.
    client.POST('/api/v1/auth/logout').catch(() => undefined);
    signOut('signed-out');
  }, [client, signOut]);
}
