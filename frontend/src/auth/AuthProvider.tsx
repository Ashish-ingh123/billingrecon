import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { AuthContext, type SessionEnd } from './AuthContext';
import { clearSession, readSession, storeSession, type Session } from './session';

// setTimeout takes at most a 32-bit signed number of milliseconds; a token lives 60 minutes, far below it.
const longestTimeout = 2_147_483_647;

const systemNow = () => new Date();

/**
 * How a tab tells the other tabs of the browser that a user signed out (ADR 0057): the session lives in sessionStorage,
 * one per tab, so the word goes through localStorage, whose changes every other tab hears. The value names the user by id
 * and is removed at once: nothing of a session is kept there.
 */
export const signedOutSignalKey = 'billingrecon.signed-out';

function tellOtherTabs(userId: string): void {
  try {
    window.localStorage.setItem(signedOutSignalKey, userId);
    window.localStorage.removeItem(signedOutSignalKey);
  } catch {
    // Storage is off: the other tabs learn it from the API, which refuses their token at the next request.
  }
}

/**
 * Holds the session (ADR 0038, point 9): read from sessionStorage when the page loads, kept there on sign-in, forgotten
 * on sign-out, at the token's expiry, and when the API refuses the token. Whatever was read for one user is dropped
 * from the query cache when the session ends, so the next user of the tab cannot see it. A sign-out in one tab ends the
 * session of the same user in the others (ADR 0057).
 */
export function AuthProvider({ children, now = systemNow }: { children: ReactNode; now?: () => Date }) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState<Session | null>(() => readSession(now()));
  const [ended, setEnded] = useState<SessionEnd | null>(null);
  // The session as it is now, for what runs outside a render: a late answer, another tab's word.
  const current = useRef(session);

  const signIn = useCallback((next: Session) => {
    storeSession(next);
    current.current = next;
    setEnded(null);
    setSession(next);
  }, []);

  const end = useCallback(
    (reason: SessionEnd): Session | null => {
      const was = current.current;
      // A session ends once: the 401 of a request still on its way when the user signed out changes nothing.
      if (was === null) {
        return null;
      }

      current.current = null;
      clearSession();
      queryClient.clear();
      setEnded(reason);
      setSession(null);
      return was;
    },
    [queryClient],
  );

  const signOut = useCallback(
    (reason: SessionEnd) => {
      const was = end(reason);
      if (was !== null && reason === 'signed-out') {
        tellOtherTabs(was.userId);
      }
    },
    [end],
  );

  useEffect(() => {
    const heard = (event: StorageEvent) => {
      if (event.key === signedOutSignalKey && event.newValue !== null && event.newValue === current.current?.userId) {
        end('signed-out');
      }
    };

    window.addEventListener('storage', heard);
    return () => window.removeEventListener('storage', heard);
  }, [end]);

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
