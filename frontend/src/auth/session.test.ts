import { describe, expect, it, vi } from 'vitest';
import { sessionFor, tokenFor, tokenWithPayload } from '../test/app';
import { safeReturnPath } from './returnPath';
import {
  clearSession,
  readClaims,
  readSession,
  sessionFromSignIn,
  sessionStorageKey,
  storeHospital,
  readHospital,
  storeSession,
} from './session';

const expiresAtUtc = '2026-11-23T10:00:00.000Z';

describe('the session of a sign-in', () => {
  it('reads the user, the tenant and one role from the token, and the address and hospital as typed, trimmed', () => {
    const session = sessionFromSignIn(
      { accessToken: tokenFor(['Cashier']), expiresAtUtc },
      ' asha@example.org ',
      ' city-hospital ',
    );

    expect(session).toEqual({
      accessToken: tokenFor(['Cashier']),
      expiresAtUtc,
      userId: 'd0000000-0000-4000-8000-000000000002',
      tenantId: 'd0000000-0000-4000-8000-000000000001',
      roles: ['Cashier'],
      credentialsChangeDue: false,
      email: 'asha@example.org',
      hospital: 'city-hospital',
    });
  });

  it('reads several roles, and leaves out a role the app does not know', () => {
    const session = sessionFromSignIn(
      { accessToken: tokenFor(['Supervisor', 'Viewer', 'Janitor']), expiresAtUtc },
      'a@b.c',
      'h',
    );

    expect(session?.roles).toEqual(['Supervisor', 'Viewer']);
  });

  it('reads a payload written in characters beyond ASCII', () => {
    const token = tokenWithPayload({ sub: 'u', tenant_id: 't', role: 'Viewer', name: 'Ānanda Kṛṣṇa' });

    expect(readClaims(token)).toMatchObject({ sub: 'u', tenant_id: 't' });
  });

  it('is null for a token that is not a JWT with a user and a tenant', () => {
    expect(readClaims('not-a-token')).toBeNull();
    expect(readClaims('a.%%%.c')).toBeNull();
    expect(readClaims(tokenWithPayload({ sub: 'x' }))).toBeNull();
    expect(sessionFromSignIn({ accessToken: 'nonsense', expiresAtUtc }, 'a@b.c', 'h')).toBeNull();
  });
});

describe('a password to be changed (ADR 0051)', () => {
  it('is kept from the answer of the sign-in, and read back from the tab', () => {
    const due = sessionFromSignIn(
      {
        accessToken: tokenFor(['Cashier']),
        expiresAtUtc: new Date(Date.now() + 900_000).toISOString(),
        credentialsChangeDue: true,
      },
      'asha@example.org',
      'city-hospital',
    );
    expect(due?.credentialsChangeDue).toBe(true);

    storeSession(due!);
    expect(readSession(new Date())?.credentialsChangeDue).toBe(true);
    clearSession();
  });
});

describe('the kept session (sessionStorage, ADR 0038 point 9)', () => {
  it('is kept in sessionStorage and nowhere else, and read back', () => {
    const session = sessionFor(['Viewer']);
    storeSession(session);

    expect(window.sessionStorage.getItem(sessionStorageKey)).not.toBeNull();
    expect(window.localStorage.getItem(sessionStorageKey)).toBeNull();
    expect(readSession(new Date())).toEqual(session);

    clearSession();
    expect(readSession(new Date())).toBeNull();
  });

  it('is forgotten once expired', () => {
    storeSession(sessionFor(['Viewer'], 1000));

    expect(readSession(new Date(Date.now() + 2000))).toBeNull();
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('is forgotten when it cannot be read', () => {
    window.sessionStorage.setItem(sessionStorageKey, '{not json');

    expect(readSession(new Date())).toBeNull();
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('is none when the browser refuses storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage is disabled.', 'SecurityError');
    });

    expect(readSession(new Date())).toBeNull();
    expect(readHospital()).toBe('');
  });

  it('remembers the hospital code, trimmed', () => {
    storeHospital(' city-hospital ');

    expect(readHospital()).toBe('city-hospital');
  });
});

describe('the address to return to after signing in', () => {
  it.each([
    ['/bills', '/bills'],
    ['/reports?from=2026-10-01', '/reports?from=2026-10-01'],
    [null, null],
    ['', null],
    ['bills', null],
    ['//evil.example', null],
    ['/\\evil.example', null],
    ['https://evil.example', null],
    ['/sign-in?returnTo=%2Fbills', null],
  ])('%j gives %j', (returnTo, expected) => {
    expect(safeReturnPath(returnTo)).toBe(expected);
  });
});
