import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor, tokenFor } from '../test/app';
import { page, problem } from '../test/records';
import { sessionStorageKey } from './session';

/**
 * The second step of a sign-in in the browser (ADR 0061), with the API faked: a session that waits for its code
 * reaches the page that gives it and nothing else; one whose role needs a second step reaches its enrolment alone,
 * where the secret is shown as a QR code and as text, confirmed by a code, and the recovery codes are shown once; and
 * anyone else is offered the enrolment from the shell.
 */
const secret = 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP';
const uri = `otpauth://totp/BillingRecon:asha%40example.org?secret=${secret}&issuer=BillingRecon&algorithm=SHA1&digits=6&period=30`;
const recoveryCodes = Array.from({ length: 10 }, (_, index) => `ABCD-EFGH-IJKL-MN${String(index).padStart(2, '0')}`);
const signedIn = (roles: Parameters<typeof tokenFor>[0], secondStep: string) => ({
  accessToken: tokenFor(roles),
  tokenType: 'Bearer',
  expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
  expiresInSeconds: 3600,
  credentialsChangeDue: false,
  secondStep,
});
const kept = () =>
  JSON.parse(window.sessionStorage.getItem(sessionStorageKey) ?? '{}') as { secondStep?: string; accessToken?: string };

describe('giving the code of a second step', () => {
  const pending = { ...sessionFor(['Supervisor']), secondStep: 'Pending' as const };

  it('is asked for before any screen, and a right code makes the session a full one', async () => {
    const api = routedApi({
      'POST /api/v1/auth/second-step': () => json(200, signedIn(['Supervisor'], 'Done')),
      'GET /api/v1/dashboard/summary': () => json(200, {}),
    });
    const { user, router } = renderApp('/approvals', { session: pending, fetch: api.fetch });

    // Whatever page was asked for, the code comes first.
    expect(await screen.findByRole('heading', { name: 'Enter your code' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/second-step');
    expect(screen.getByLabelText(/^Code/)).toHaveAttribute('autocomplete', 'one-time-code');

    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('Enter the code.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/auth/second-step')).toHaveLength(0);

    await user.type(screen.getByLabelText(/^Code/), ' 287082 ');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(router.state.location.pathname).not.toBe('/second-step'));
    const sent = api.sent('POST', '/api/v1/auth/second-step')[0];
    expect(await sent?.json()).toEqual({ code: '287082' });
    // The code goes with the token the password gave.
    expect(sent?.headers.get('Authorization')).toBe(`Bearer ${pending.accessToken}`);
    expect(kept().secondStep).toBe('Done');
  });

  it("shows the API's refusal of a wrong code, and empties the box for the next one", async () => {
    const api = routedApi({
      'POST /api/v1/auth/second-step': () =>
        json(
          422,
          problem(
            422,
            'second_step_code_wrong',
            'The code is not right. Give the code your authenticator app shows now.',
          ),
        ),
    });
    const { user } = renderApp('/second-step', { session: pending, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^Code/), '000000');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(
      await screen.findByText('The code is not right. Give the code your authenticator app shows now.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/^Code/)).toHaveValue('');
    expect(kept().secondStep).toBe('Pending');
  });

  it('has nothing to ask of a session whose second step is given', async () => {
    const api = routedApi({ 'GET /api/v1/payments': () => json(200, page([])) });
    const { router } = renderApp('/second-step', { session: sessionFor(['Cashier']), fetch: api.fetch });

    await waitFor(() => expect(router.state.location.pathname).not.toBe('/second-step'));
  });
});

describe('setting a second step up', () => {
  const enrolling = { ...sessionFor(['Supervisor']), secondStep: 'Enrolment' as const };
  const routes = {
    'POST /api/v1/auth/second-step/enrolment': () => json(200, { secret, provisioningUri: uri }),
    'POST /api/v1/auth/second-step/enrolment/confirm': () =>
      json(200, { recoveryCodes, signedIn: signedIn(['Supervisor'], 'Done') }),
    'GET /api/v1/dashboard/summary': () => json(200, {}),
  };

  it('is the only page of a user whose role needs one, shows the QR code and the key, and the recovery codes once', async () => {
    const api = routedApi(routes);
    const { user, router } = renderApp('/dashboard', { session: enrolling, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Set up your second step' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/second-step/enrol');
    // Nothing is made until the user begins: a visit alone changes nothing on the server.
    expect(api.requests).toHaveLength(0);
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Begin' }));

    const code = await screen.findByRole('img', { name: 'QR code for your authenticator app' });
    // A drawn symbol: a square view box of the symbol and its quiet zone, and a path of dark modules.
    const [, , width, height] = (code.getAttribute('viewBox') ?? '').split(' ').map(Number);
    expect(width).toBe(height);
    expect((width! - 8 - 17) % 4).toBe(0);
    expect(code.querySelector('path')?.getAttribute('d')).toMatch(/^M4,4h1v1h-1z/);
    // The key as text, in groups of four, for a phone that cannot scan.
    expect(screen.getByText('JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByText('Enter the six digits your app shows.')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/^Code of the app/), '287082');
    await user.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByRole('heading', { name: 'Keep your recovery codes' })).toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Recovery codes' });
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(recoveryCodes);
    expect(await api.sent('POST', '/api/v1/auth/second-step/enrolment/confirm')[0]?.json()).toEqual({ code: '287082' });
    // The session is the new one already: the old ended with the enrolment.
    expect(kept().secondStep).toBe('Done');
    expect(router.state.location.pathname).toBe('/second-step/enrol');

    await user.click(screen.getByRole('button', { name: 'I have kept them: continue' }));
    await waitFor(() => expect(router.state.location.pathname).not.toBe('/second-step/enrol'));
  });

  it("shows the API's refusal of a wrong first code and keeps the same QR code", async () => {
    const api = routedApi({
      ...routes,
      'POST /api/v1/auth/second-step/enrolment/confirm': () =>
        json(422, problem(422, 'second_step_code_wrong', 'The code is not right.')),
    });
    const { user } = renderApp('/second-step/enrol', { session: enrolling, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Begin' }));
    const before = (await screen.findByRole('img', { name: 'QR code for your authenticator app' })).innerHTML;
    await user.type(screen.getByLabelText(/^Code of the app/), '000000');
    await user.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('The code is not right.')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'QR code for your authenticator app' }).innerHTML).toBe(before);
    expect(api.sent('POST', '/api/v1/auth/second-step/enrolment')).toHaveLength(1);
    expect(kept().secondStep).toBe('Enrolment');
  });

  it('is offered from the shell to a user who has none, and not to one who has', async () => {
    const api = routedApi({ 'GET /api/v1/payments': () => json(200, page([])) });
    const without = renderApp('/payments', {
      session: { ...sessionFor(['Cashier']), secondStep: 'None' },
      fetch: api.fetch,
    });

    expect(await screen.findByRole('link', { name: 'Set up a second step' })).toHaveAttribute(
      'href',
      '/second-step/enrol',
    );
    without.unmount();

    renderApp('/payments', { session: sessionFor(['Cashier']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'Payments' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Set up a second step' })).not.toBeInTheDocument();
  });

  it('sends a session that waits for its code to give it, not to enrol another', async () => {
    const api = routedApi({});
    const { router } = renderApp('/second-step/enrol', {
      session: { ...sessionFor(['Supervisor']), secondStep: 'Pending' },
      fetch: api.fetch,
    });

    expect(await screen.findByRole('heading', { name: 'Enter your code' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/second-step');
    expect(api.requests).toHaveLength(0);
  });
});
