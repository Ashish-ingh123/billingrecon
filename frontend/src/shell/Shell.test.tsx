import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { sessionStorageKey } from '../auth/session';
import { fakeApi, json, renderApp, routedApi, sessionFor } from '../test/app';

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
    expect(within(navigation).getAllByRole('link')).toHaveLength(12);
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

  it('replaces the notice of the sign-out with what the next attempt met', async () => {
    const api = fakeApi(
      json(401, { title: 'Unauthorized', status: 401, detail: 'Not right.', errorCode: 'invalid_credentials' }),
    );
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
