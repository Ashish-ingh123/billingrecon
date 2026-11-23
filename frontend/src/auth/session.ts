import { isRole, type Role } from './roles';

/**
 * A signed-in user as the browser knows them (ADR 0038, point 9): the token the API issued, when it expires, and what
 * the token and the sign-in say about the user. The roles are read from the token to show the right navigation only;
 * the API checks the token on every request and is the authority.
 */
export interface Session {
  accessToken: string;
  expiresAtUtc: string;
  userId: string;
  tenantId: string;
  roles: Role[];
  /** The address the user signed in with: the token does not carry one. */
  email: string;
  /** The hospital's code the user signed in with. */
  hospital: string;
}

/** Where the session is kept: sessionStorage, so it survives a refresh and ends with the tab (row S5). */
export const sessionStorageKey = 'billingrecon.session';

/** Where the hospital's code is remembered for the next sign-in: localStorage, as it is not a secret. */
export const hospitalStorageKey = 'billingrecon.hospital';

interface Claims {
  sub: string;
  tenant_id: string;
  role?: string | string[];
}

/** The claims of a JWT, read without checking its signature (the API does that), or null when it is not one. */
export function readClaims(token: string): Claims | null {
  const payload = token.split('.')[1];
  if (payload === undefined) {
    return null;
  }

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const bytes = Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
    const claims: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (
      typeof claims === 'object' &&
      claims !== null &&
      'sub' in claims &&
      typeof claims.sub === 'string' &&
      'tenant_id' in claims &&
      typeof claims.tenant_id === 'string'
    ) {
      return claims as Claims;
    }

    return null;
  } catch {
    return null;
  }
}

/** The session of a sign-in the API answered, or null when its token cannot be read. */
export function sessionFromSignIn(
  signedIn: { accessToken: string; expiresAtUtc: string },
  email: string,
  hospital: string,
): Session | null {
  const claims = readClaims(signedIn.accessToken);
  if (claims === null) {
    return null;
  }

  const claimed = claims.role === undefined ? [] : Array.isArray(claims.role) ? claims.role : [claims.role];

  return {
    accessToken: signedIn.accessToken,
    expiresAtUtc: signedIn.expiresAtUtc,
    userId: claims.sub,
    tenantId: claims.tenant_id,
    roles: claimed.filter(isRole),
    email: email.trim(),
    hospital: hospital.trim(),
  };
}

export function isExpired(session: Session, now: Date): boolean {
  return Date.parse(session.expiresAtUtc) <= now.getTime();
}

/** The kept session, or null when there is none, it cannot be read, or it has expired (an expired one is forgotten). */
export function readSession(now: Date): Session | null {
  let stored: string | null;
  try {
    stored = window.sessionStorage.getItem(sessionStorageKey);
  } catch {
    return null;
  }

  if (stored === null) {
    return null;
  }

  try {
    const session = JSON.parse(stored) as Session;
    if (typeof session.accessToken !== 'string' || !Array.isArray(session.roles) || isExpired(session, now)) {
      clearSession();
      return null;
    }

    return { ...session, roles: session.roles.filter(isRole) };
  } catch {
    clearSession();
    return null;
  }
}

export function storeSession(session: Session): void {
  try {
    window.sessionStorage.setItem(sessionStorageKey, JSON.stringify(session));
  } catch {
    // Storage is off: the session lasts as long as the page.
  }
}

export function clearSession(): void {
  try {
    window.sessionStorage.removeItem(sessionStorageKey);
  } catch {
    // Nothing was kept.
  }
}

export function readHospital(): string {
  try {
    return window.localStorage.getItem(hospitalStorageKey) ?? '';
  } catch {
    return '';
  }
}

export function storeHospital(hospital: string): void {
  try {
    window.localStorage.setItem(hospitalStorageKey, hospital.trim());
  } catch {
    // Not remembered: the user types it next time.
  }
}
