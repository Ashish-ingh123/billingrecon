import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { signedOutSignalKey } from '../auth/AuthProvider';
import { sessionStorageKey } from '../auth/session';
import { json, renderApp, routedApi, sessionFor } from '../test/app';

describe('the shell of a signed-in user', () => {
  it('shows the hospital, the user and their roles, and the sections their roles may open', async () => {
    const { router } = renderApp('/', { session: sessionFor(['TenantAdmin']) });

    // The dashboard loads on demand with its charts, which can take more than the default second in a full run
    // (backlog B35, closed by Prompt 9.2): the wait is longer, what is expected is the same.
    expect(await screen.findByRole('heading', { name: 'Dashboard' }, { timeout: 5000 })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/dashboard');
    expect(screen.getByText('city-hospital')).toBeInTheDocument();
    expect(screen.getByText('asha@example.org')).toBeInTheDocument();
    expect(screen.getByText('Administrator')).toBeInTheDocument();

    const navigation = screen.getByRole('navigation', { name: 'Main' });
    expect(within(navigation).getAllByRole('link')).toHaveLength(13);
    expect(within(navigation).getByRole('link', { name: 'Dashboard' })).toHaveAttribute('aria-current', 'page');
  });

  it('shows a cashier only what a cashier may open, and refuses a page of another role by its address', async () => {
    renderApp('/dashboard', { session: sessionFor(['Cashier']) });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
    const links = within(screen.getByRole('navigation', { name: 'Main' }))
      .getAllByRole('link')
      .map((link) => link.textContent);
    expect(links).toEqual([
      'Patients',
      'Payers',
      'Service items',
      'Bills',
      'Payments',
      'Refunds',
      'Shifts',
      'Credit notes',
      'Reports',
    ]);
  });

  // Until Prompt 10.1 this section was the last placeholder ("No prompt builds these screens yet"); every section is built
  // now, so the test holds that an administrator reaches its screen (ADR 0051).
  it('shows Users and settings to an administrator, the last section that was a placeholder', async () => {
    const api = routedApi({
      'GET /api/v1/users': () =>
        new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    });
    renderApp('/administration', { session: sessionFor(['TenantAdmin']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Users and settings' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Users and settings' })).toBeInTheDocument();
    expect(await screen.findByText('No user is set up yet.')).toBeInTheDocument();
  });

  it('moves between sections without signing in again', async () => {
    // The inbox reads what waits; here, nothing.
    const api = routedApi({
      'GET /api/v1/reports/pending-approvals': () =>
        json(200, {
          period: null,
          items: [],
          page: 1,
          pageSize: 20,
          totalCount: 0,
          totals: { requests: 0, oldestRequestedAtUtc: null },
        }),
    });
    const { user, router } = renderApp('/', { session: sessionFor(['Supervisor']), fetch: api.fetch });

    await user.click(await screen.findByRole('link', { name: 'Approvals' }));

    expect(await screen.findByRole('heading', { name: 'Approvals' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/approvals');
  });

  it('answers an address that is no page', async () => {
    renderApp('/nowhere', { session: sessionFor(['Viewer']) });

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('signs out: the session is forgotten and the sign-in says so', async () => {
    const { user, router } = renderApp('/reports', { session: sessionFor(['BillingClerk']) });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/sign-in');
    expect(router.state.location.search).toBe('');
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('tells the API of the sign-out, with the token of the session that ends (ADR 0057)', async () => {
    const session = sessionFor(['BillingClerk']);
    const api = routedApi({ 'POST /api/v1/auth/logout': () => new Response(null, { status: 204 }) });
    const { user } = renderApp('/reports', { session, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();
    await waitFor(() => expect(api.sent('POST', '/api/v1/auth/logout')).toHaveLength(1));
    expect(api.sent('POST', '/api/v1/auth/logout')[0]?.headers.get('Authorization')).toBe(
      `Bearer ${session.accessToken}`,
    );
  });

  it.each([
    ['refuses the sign-out', () => json(401, { title: 'Unauthorized', status: 401, errorCode: 'session_ended' })],
    ['cannot be reached', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])('signs out here all the same when the API %s', async (_, answer) => {
    const api = routedApi({ 'POST /api/v1/auth/logout': answer });
    const { user, router } = renderApp('/reports', { session: sessionFor(['Viewer']), fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    await waitFor(() => expect(api.sent('POST', '/api/v1/auth/logout')).toHaveLength(1));

    // The refusal of the sign-out's own request does not turn "signed out" into "your session has ended".
    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();
    expect(screen.queryByText('Your session has ended. Sign in again to continue.')).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/sign-in');
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('tells the other tabs of the browser who signed out, and keeps nothing of it', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const session = sessionFor(['Viewer']);
    const api = routedApi({ 'POST /api/v1/auth/logout': () => new Response(null, { status: 204 }) });
    const { user } = renderApp('/reports', { session, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();
    expect(setItem.mock.calls.filter(([key]) => key === signedOutSignalKey)).toEqual([
      [signedOutSignalKey, session.userId],
    ]);
    expect(window.localStorage.getItem(signedOutSignalKey)).toBeNull();
    setItem.mockRestore();
  });

  it('signs out when another tab says the same user signed out, and not for another user', async () => {
    const session = sessionFor(['Viewer']);
    const api = routedApi({});
    const { router } = renderApp('/reports', { session, fetch: api.fetch });
    await screen.findByRole('button', { name: 'Sign out' });

    const say = (userId: string | null) =>
      act(() => {
        window.dispatchEvent(new StorageEvent('storage', { key: signedOutSignalKey, newValue: userId }));
      });

    // Another user of this browser signed out, and the signal's own removal: neither concerns this tab.
    say('d0000000-0000-4000-8000-00000000ffff');
    say(null);
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(window.sessionStorage.getItem(sessionStorageKey)).not.toBeNull();

    say(session.userId);
    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/sign-in');
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
    // The tab that signed out told the API; this one sends nothing.
    expect(api.requests).toHaveLength(0);
  });

  it('replaces the notice of the sign-out with what the next attempt met', async () => {
    // The sign-out is a request of its own since ADR 0057, so each path has its answer.
    const api = routedApi({
      'POST /api/v1/auth/logout': () => new Response(null, { status: 204 }),
      'POST /api/v1/auth/login': () =>
        json(401, { title: 'Unauthorized', status: 401, detail: 'Not right.', errorCode: 'invalid_credentials' }),
    });
    const { user } = renderApp('/reports', { session: sessionFor(['BillingClerk']), fetch: api.fetch });
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    expect(await screen.findByText('You have signed out.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Hospital code'), 'city-hospital');
    await user.type(screen.getByLabelText('E-mail address'), 'asha@example.org');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Not right.');
    expect(screen.queryByText('You have signed out.')).not.toBeInTheDocument();
  });

  it('ends the session when the token expires, and brings the user back to the same page after signing in', async () => {
    const { router } = renderApp('/reports', { session: sessionFor(['Cashier'], 150) });

    expect(await screen.findByRole('heading', { name: 'Reports' })).toBeInTheDocument();

    expect(await screen.findByText('Your session has ended. Sign in again to continue.')).toBeInTheDocument();
    await waitFor(() => expect(router.state.location.search).toBe('?returnTo=%2Freports'));
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('does not take up a kept session that has expired', async () => {
    const { router } = renderApp('/bills', { session: sessionFor(['BillingClerk'], -1000) });

    expect(await screen.findByRole('heading', { name: 'Sign in to BillingRecon' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?returnTo=%2Fbills');
  });
});
