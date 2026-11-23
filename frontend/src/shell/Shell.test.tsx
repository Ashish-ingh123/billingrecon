import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { sessionStorageKey } from '../auth/session';
import { fakeApi, json, renderApp, sessionFor } from '../test/app';

describe('the shell of a signed-in user', () => {
  it('shows the hospital, the user and their roles, and the sections their roles may open', async () => {
    const { router } = renderApp('/', { session: sessionFor(['TenantAdmin']) });

    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/dashboard');
    expect(screen.getByText('city-hospital')).toBeInTheDocument();
    expect(screen.getByText('asha@example.org')).toBeInTheDocument();
    expect(screen.getByText('Administrator')).toBeInTheDocument();

    const navigation = screen.getByRole('navigation', { name: 'Main' });
    expect(within(navigation).getAllByRole('link')).toHaveLength(12);
    expect(within(navigation).getByRole('link', { name: 'Dashboard' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('These screens arrive with Prompt 7.6.')).toBeInTheDocument();
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

  it('moves between sections without signing in again', async () => {
    const { user, router } = renderApp('/', { session: sessionFor(['Supervisor']) });

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
    const { router } = renderApp('/payments', { session: sessionFor(['Cashier'], 150) });

    expect(await screen.findByRole('heading', { name: 'Payments' })).toBeInTheDocument();

    expect(await screen.findByText('Your session has ended. Sign in again to continue.')).toBeInTheDocument();
    await waitFor(() => expect(router.state.location.search).toBe('?returnTo=%2Fpayments'));
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('does not take up a kept session that has expired', async () => {
    const { router } = renderApp('/bills', { session: sessionFor(['BillingClerk'], -1000) });

    expect(await screen.findByRole('heading', { name: 'Sign in to BillingRecon' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?returnTo=%2Fbills');
  });
});
