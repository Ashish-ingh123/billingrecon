import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { AuthContext, type SessionEnd } from './AuthContext';
import { clearSession, readSession, storeSession, type Session } from './session';

// setTimeout takes at most a 32-bit signed number of milliseconds; a token lives 60 minutes, far below it.
const longestTimeout = 2_147_483_647;

const systemNow = () => new Date();

/**
 * Holds the session (ADR 0038, point 9): read from sessionStorage when the page loads, kept there on sign-in, forgotten
 * on sign-out, at the token's expiry, and when the API refuses the token. Whatever was read for one user is dropped
 * from the query cache when the session ends, so the next user of the tab cannot see it.
 */
export function AuthProvider({ children, now = systemNow }: { children: ReactNode; now?: () => Date }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(() => readSession(now()));
  const [ended, setEnded] = useState<SessionEnd | null>(null);

  const signIn = useCallback((next: Session) => {
    storeSession(next);
    setEnded(null);
    setSession(next);
  }, []);

  const signOut = useCallback(
    (reason: SessionEnd) => {
      clearSession();
      queryClient.clear();
      setEnded(reason);
      setSession(null);
    },
    [queryClient],
  );

  useEffect(() => {
    if (session === null) {
      return;
    }

    const remaining = Date.parse(session.expiresAtUtc) - now().getTime();
    const timer = window.setTimeout(() => signOut('expired'), Math.min(Math.max(remaining, 0), longestTimeout));
    return () => window.clearTimeout(timer);
  }, [session, signOut, now]);

  const state = useMemo(() => ({ session, ended, signIn, signOut }), [session, ended, signIn, signOut]);

  return <AuthContext value={state}>{children}</AuthContext>;
}
