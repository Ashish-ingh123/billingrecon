import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { fakeApi, json, renderApp, sessionFor, tokenFor } from '../test/app';
import { hospitalStorageKey, sessionStorageKey } from './session';

const signedIn = (roles: Parameters<typeof tokenFor>[0]) =>
  json(200, {
    accessToken: tokenFor(roles),
    tokenType: 'Bearer',
    expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
    expiresInSeconds: 3600,
  });

async function fillAndSubmit(user: ReturnType<typeof renderApp>['user'], hospital = 'city-hospital') {
  const code = screen.getByLabelText('Hospital code');
  await user.clear(code);
  if (hospital !== '') {
    await user.type(code, hospital);
  }

  await user.type(screen.getByLabelText('E-mail address'), 'asha@example.org');
  await user.type(screen.getByLabelText('Password'), 'correct horse battery');
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('signing in (US-02)', () => {
  it('sends the three fields without a token, keeps the session for the tab and the hospital for next time, and returns to the page asked for', async () => {
    const api = fakeApi(signedIn(['BillingClerk']));
    const { user, router } = renderApp('/bills', { fetch: api.fetch });

    // Not signed in: the sign-in, which will bring the user back to the bills.
    expect(await screen.findByRole('heading', { name: 'Sign in to BillingRecon' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?returnTo=%2Fbills');

    await fillAndSubmit(user);

    expect(await screen.findByRole('heading', { name: 'Bills' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/bills');

    const [request] = api.requests;
    expect(request?.method).toBe('POST');
    expect(new URL(request?.url ?? '').pathname).toBe('/api/v1/auth/login');
    expect(await request?.json()).toEqual({
      tenant: 'city-hospital',
      email: 'asha@example.org',
      password: 'correct horse battery',
    });
    expect(request?.headers.has('Authorization')).toBe(false);
    expect(request?.headers.get('X-Correlation-Id')).toMatch(/^[0-9a-f-]{36}$/);

    const kept = JSON.parse(window.sessionStorage.getItem(sessionStorageKey) ?? 'null') as { roles: string[] };
    expect(kept.roles).toEqual(['BillingClerk']);
    expect(window.localStorage.getItem(hospitalStorageKey)).toBe('city-hospital');
    expect(window.localStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it('lands each role on its start page when no page was asked for', async () => {
    const api = fakeApi(signedIn(['Cashier']));
    const { user, router } = renderApp('/sign-in', { fetch: api.fetch });

    await fillAndSubmit(user);

    expect(await screen.findByRole('heading', { name: 'Payments' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/payments');
  });

  it('offers the remembered hospital code', async () => {
    window.localStorage.setItem(hospitalStorageKey, 'city-hospital');
    renderApp('/sign-in');

    expect(await screen.findByLabelText('Hospital code')).toHaveValue('city-hospital');
  });

  it('asks for every field before it sends anything', async () => {
    const api = fakeApi();
    const { user } = renderApp('/sign-in', { fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Enter the hospital code.')).toBeInTheDocument();
    expect(screen.getByText('Enter your e-mail address.')).toBeInTheDocument();
    expect(screen.getByText('Enter your password.')).toBeInTheDocument();
    expect(screen.getByLabelText('Hospital code')).toHaveAttribute('aria-invalid', 'true');
    expect(api.requests).toHaveLength(0);
  });

  it("shows the API's one answer to a refused sign-in, with the reference to quote, and keeps no session", async () => {
    const api = fakeApi(
      json(401, {
        title: 'Unauthorized',
        status: 401,
        detail: 'The hospital code, e-mail address or password is not right.',
        errorCode: 'invalid_credentials',
        correlationId: '0f8fad5b-d9cb-469f-a165-70867728950e',
      }),
    );
    const { user, router } = renderApp('/sign-in', { fetch: api.fetch });

    await fillAndSubmit(user);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('The hospital code, e-mail address or password is not right.');
    expect(alert).toHaveTextContent('0f8fad5b-d9cb-469f-a165-70867728950e');
    expect(alert).toHaveAttribute('data-error-code', 'invalid_credentials');
    expect(router.state.location.pathname).toBe('/sign-in');
    expect(window.sessionStorage.getItem(sessionStorageKey)).toBeNull();
  });

  it("puts the API's messages for a field beside the field", async () => {
    const api = fakeApi(
      json(400, {
        title: 'One or more fields are invalid.',
        status: 400,
        errorCode: 'validation_failed',
        errors: { email: ['The e-mail address is at most 254 characters.'] },
      }),
    );
    const { user } = renderApp('/sign-in', { fetch: api.fetch });

    await fillAndSubmit(user);

    expect(await screen.findByText('The e-mail address is at most 254 characters.')).toBeInTheDocument();
    expect(screen.getByLabelText('E-mail address')).toHaveAttribute('aria-invalid', 'true');
  });

  it('says how long to wait when sign-ins are limited', async () => {
    const api = fakeApi(
      json(
        429,
        { title: 'Too many sign-ins.', status: 429, errorCode: 'too_many_sign_ins', detail: 'Too many sign-ins.' },
        { 'Retry-After': '900' },
      ),
    );
    const { user } = renderApp('/sign-in', { fetch: api.fetch });

    await fillAndSubmit(user);

    expect(await screen.findByRole('alert')).toHaveTextContent('Try again in 15 minutes.');
  });

  it('says so when the server cannot be reached', async () => {
    const api = fakeApi(new TypeError('Failed to fetch'));
    const { user } = renderApp('/sign-in', { fetch: api.fetch });

    await fillAndSubmit(user);

    expect(await screen.findByRole('alert')).toHaveTextContent('The server could not be reached.');
  });

  it('never follows a return address that leaves the app', async () => {
    const api = fakeApi(signedIn(['Viewer']));
    const { user, router } = renderApp('/sign-in?returnTo=%2F%2Fevil.example%2Fsteal', { fetch: api.fetch });

    await fillAndSubmit(user);

    await waitFor(() => expect(router.state.location.pathname).toBe('/dashboard'));
  });

  it('sends a signed-in user who opens the sign-in to their start page', async () => {
    const { router } = renderApp('/sign-in', { session: sessionFor(['Supervisor']) });

    await waitFor(() => expect(router.state.location.pathname).toBe('/dashboard'));
  });
});
