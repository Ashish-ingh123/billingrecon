import { createContext, useContext } from 'react';
import type { Session } from './session';

/** Why a session ended: the user signed out, or the token expired or was refused by the API. */
export type SessionEnd = 'signed-out' | 'expired';

export interface AuthState {
  session: Session | null;
  /** How the last session ended, until the next sign-in; null when nobody has signed out yet. */
  ended: SessionEnd | null;
  signIn: (session: Session) => void;
  signOut: (reason: SessionEnd) => void;
}

export const AuthContext = createContext<AuthState | null>(null);

export function useAuth(): AuthState {
  const state = useContext(AuthContext);
  if (state === null) {
    throw new Error('useAuth is used outside AuthProvider.');
  }

  return state;
}

/** The session of a page that only a signed-in user reaches. */
export function useSession(): Session {
  const { session } = useAuth();
  if (session === null) {
    throw new Error('useSession is used on a page that does not require a session.');
  }

  return session;
}
