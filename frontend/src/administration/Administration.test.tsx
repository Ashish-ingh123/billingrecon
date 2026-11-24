import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { problem } from '../test/records';
import { json, renderApp, routedApi, sessionFor, tokenFor } from '../test/app';
import type { TenantSettings, User } from './api';

/**
 * Users and settings for the hospital's administrator (Prompt 10.1; US-01; ADR 0030, ADR 0033, ADR 0051): the list with
 * its filters, a new user with roles, a user's page with disable, enable and a password reset, the edit of details and
 * roles, the settings with If-Match, a prefix change as a request, the change of one's own password, and who may come.
 */
const admin = sessionFor(['TenantAdmin']);
const asha: User = {
  id: 'a7000000-0000-4000-8000-000000000001',
  email: 'asha.rao@cityhospital.org',
  displayName: 'Asha Rao',
  status: 'Active',
  roles: ['Supervisor', 'Cashier'],
  credentialsChangeDue: false,
  lastSignInAtUtc: '2026-11-20T05:00:00+00:00',
  createdAtUtc: '2026-11-01T05:00:00+00:00',
  updatedAtUtc: '2026-11-20T05:00:00+00:00',
  version: 'AAAAAAAAB9E=',
};
const settings: TenantSettings = {
  postingApprovalThreshold: 100000,
  maxBackdateDays: 7,
  defaultDueDays: 15,
  prefixes: { bill: 'BIL', creditNote: 'CRN', payment: 'PAY', adjustment: 'ADJ', refund: 'REF' },
  version: 'AAAAAAAAC1E=',
};
const page = (items: object[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const one = `/api/v1/users/${asha.id}`;
const trail = { 'GET /api/v1/audit-records': () => json(200, page([])) };

describe('the users of the hospital (US-01, ADR 0051)', () => {
  it('lists them with their roles, status and last sign-in, and sends the filters it keeps in the address', async () => {
    const api = routedApi({
      'GET /api/v1/users': () =>
        json(
          200,
          page([
            asha,
            {
              ...asha,
              id: 'a7000000-0000-4000-8000-000000000002',
              displayName: 'Ravi Iyer',
              status: 'Disabled',
              roles: ['Viewer'],
            },
          ]),
        ),
    });
    renderApp('/administration?status=Active&role=Cashier&email=rao', { session: admin, fetch: api.fetch });

    const table = await screen.findByRole('table');
    expect(within(table).getByRole('link', { name: 'Asha Rao' })).toHaveAttribute(
      'href',
      `/administration/users/${asha.id}`,
    );
    expect(within(table).getByText('Supervisor, Cashier')).toBeInTheDocument();
    expect(within(table).getByText('Disabled')).toBeInTheDocument();
    const query = Object.fromEntries(new URL(api.sent('GET', '/api/v1/users')[0]?.url ?? '').searchParams);
    expect(query).toEqual({ Email: 'rao', Status: 'Active', Role: 'Cashier', Page: '1', PageSize: '20' });
  });

  it('creates a user with roles, asking for at least one, and shows the refusal of an address already taken', async () => {
    const api = routedApi({
      'POST /api/v1/users': (request) =>
        request.headers.get('content-type') === null
          ? json(400, {})
          : json(
              409,
              problem(
                409,
                'user_email_taken',
                'Another user of this hospital already has this e-mail address (BR-601).',
              ),
            ),
    });
    const { user } = renderApp('/administration/users/new', { session: admin, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^E-mail address/), 'asha.rao@cityhospital.org');
    await user.type(screen.getByLabelText(/^Name/), 'Asha Rao');
    await user.type(screen.getByLabelText(/^First password/), 'a long first password');
    await user.click(screen.getByRole('button', { name: 'Create user' }));
    expect(await screen.findByText('Choose at least one role.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/users')).toHaveLength(0);

    await user.click(screen.getByRole('checkbox', { name: 'Cashier' }));
    await user.click(screen.getByRole('checkbox', { name: 'Supervisor' }));
    await user.click(screen.getByRole('button', { name: 'Create user' }));

    expect(await screen.findByText(/already has this e-mail address/)).toBeInTheDocument();
    const body = (await api.sent('POST', '/api/v1/users')[0]?.json()) as Record<string, unknown>;
    expect(body).toEqual({
      email: 'asha.rao@cityhospital.org',
      displayName: 'Asha Rao',
      password: 'a long first password',
      roles: ['Supervisor', 'Cashier'],
    });
  });

  it('disables a user after asking once more, with its version, and shows a refusal of the API', async () => {
    const api = routedApi({
      [`GET ${one}`]: () => json(200, asha),
      [`POST ${one}/disable`]: () =>
        json(
          422,
          problem(
            422,
            'user_last_tenant_admin',
            'This is the last active tenant administrator; make another user a TenantAdmin first.',
          ),
        ),
      ...trail,
    });
    const { user } = renderApp(`/administration/users/${asha.id}`, { session: admin, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Disable' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: disable' }));

    expect(await screen.findByText(/last active tenant administrator/)).toBeInTheDocument();
    expect(api.sent('POST', `${one}/disable`)[0]?.headers.get('If-Match')).toBe(`"${asha.version}"`);
  });

  it('offers no disable or reset of oneself', async () => {
    const api = routedApi({
      [`GET /api/v1/users/${admin.userId}`]: () => json(200, { ...asha, id: admin.userId }),
      ...trail,
    });
    renderApp(`/administration/users/${admin.userId}`, { session: admin, fetch: api.fetch });

    expect(await screen.findByText(/This is you/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Disable' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reset password' })).not.toBeInTheDocument();
  });

  it('resets a password, which the user must change at the next sign-in', async () => {
    const api = routedApi({
      [`GET ${one}`]: () => json(200, asha),
      [`POST ${one}/password`]: () => json(200, { ...asha, credentialsChangeDue: true, version: 'AAAAAAAAB9F=' }),
      ...trail,
    });
    const { user } = renderApp(`/administration/users/${asha.id}`, { session: admin, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Reset password' }));
    await user.type(screen.getByLabelText(/^New password for the user/), 'short');
    await user.click(screen.getByRole('button', { name: 'Set the password' }));
    expect(screen.getByText('The password has 12 to 128 characters.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText(/^New password for the user/));
    await user.type(screen.getByLabelText(/^New password for the user/), 'a new long password');
    await user.click(screen.getByRole('button', { name: 'Set the password' }));

    expect(await screen.findByText(/must change it at the next sign-in/)).toBeInTheDocument();
    expect(screen.getByText('Password to be changed')).toBeInTheDocument();
    expect(await api.sent('POST', `${one}/password`)[0]?.json()).toEqual({ password: 'a new long password' });
  });

  it('saves a new name, then the roles with the version the name came back with', async () => {
    const renamed = { ...asha, displayName: 'Asha R. Rao', version: 'AAAAAAAAB9G=' };
    const api = routedApi({
      [`GET ${one}`]: () => json(200, asha),
      [`PATCH ${one}`]: () => json(200, renamed),
      [`PUT ${one}/roles`]: () => json(200, { ...renamed, roles: ['Cashier'], version: 'AAAAAAAAB9H=' }),
      ...trail,
    });
    const { user } = renderApp(`/administration/users/${asha.id}/edit`, { session: admin, fetch: api.fetch });

    const name = await screen.findByLabelText(/^Name/);
    await user.clear(name);
    await user.type(name, 'Asha R. Rao');
    await user.click(screen.getByRole('checkbox', { name: 'Supervisor' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(api.sent('PUT', `${one}/roles`)).toHaveLength(1));
    expect(api.sent('PATCH', one)[0]?.headers.get('If-Match')).toBe(`"${asha.version}"`);
    const roles = api.sent('PUT', `${one}/roles`)[0];
    expect(roles?.headers.get('If-Match')).toBe(`"${renamed.version}"`);
    expect(await roles?.json()).toEqual({ roles: ['Cashier'] });
  });
});

describe('the settings of the hospital (ADR 0033, ADR 0051)', () => {
  it('saves the three settings with the version read', async () => {
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, settings),
      'PATCH /api/v1/settings': () => json(200, { ...settings, defaultDueDays: 30, version: 'AAAAAAAAC1F=' }),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });

    const due = await screen.findByLabelText(/^Default due days/, undefined, { timeout: 3000 });
    await user.clear(due);
    await user.type(due, '30');
    await user.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(await screen.findByText('The settings apply to every document from now on.')).toBeInTheDocument();
    const sent = api.sent('PATCH', '/api/v1/settings')[0];
    expect(sent?.headers.get('If-Match')).toBe(`"${settings.version}"`);
    expect(await sent?.json()).toEqual({ postingApprovalThreshold: 100000, maxBackdateDays: 7, defaultDueDays: 30 });
  });

  it('sends a new prefix as a request a second person approves', async () => {
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, settings),
      'POST /api/v1/settings/prefix-changes': () =>
        json(201, {
          id: 'pc000000-0000-4000-8000-000000000001',
          documentType: 'Bill',
          fromPrefix: 'BIL',
          toPrefix: 'INV',
          status: 'Pending',
          requestedAtUtc: '2026-11-24T05:00:00+00:00',
          requestedByUserId: admin.userId,
          decidedAtUtc: null,
          version: 'AAAAAAAAD1E=',
        }),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });

    await user.click(
      await screen.findByRole('button', { name: /Request a change of the prefix of bills/ }, { timeout: 3000 }),
    );
    await user.type(screen.getByLabelText(/^New prefix for bills/), 'inv2');
    await user.click(screen.getByRole('button', { name: 'Send for approval' }));
    expect(screen.getByText('A prefix has 1 to 3 characters, each a letter or a digit.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText(/^New prefix for bills/));
    await user.type(screen.getByLabelText(/^New prefix for bills/), 'inv');
    await user.click(screen.getByRole('button', { name: 'Send for approval' }));

    expect(await screen.findByText(/becomes INV once a second person approves it/)).toBeInTheDocument();
    expect(await api.sent('POST', '/api/v1/settings/prefix-changes')[0]?.json()).toEqual({
      documentType: 'Bill',
      prefix: 'INV',
      comment: null,
    });
  });

  it('is the administrator’s alone', async () => {
    renderApp('/administration/settings', { session: sessionFor(['Supervisor']), fetch: routedApi({}).fetch });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
  });
});

describe('a password of one’s own (ADR 0051)', () => {
  const due = { ...sessionFor(['Cashier']), credentialsChangeDue: true };

  it('is asked for before any screen, and a wrong current password is said at its field', async () => {
    const api = routedApi({
      'POST /api/v1/auth/password': () =>
        json(422, problem(422, 'password_current_wrong', 'The current password is not right; nothing was changed.')),
    });
    const { user, router } = renderApp('/payments', { session: due, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Set a password of your own' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/change-password');

    await user.type(screen.getByLabelText(/^The password you were given/), 'given password');
    await user.type(screen.getByLabelText(/^New password$/), 'my own long passphrase');
    await user.type(screen.getByLabelText(/^New password again/), 'my own long passphrase');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));

    expect(await screen.findByText('The current password is not right; nothing was changed.')).toBeInTheDocument();
    expect(screen.getByLabelText(/^The password you were given/)).toHaveAttribute('aria-invalid', 'true');
  });

  it('keeps the new token, and goes on to the start page with nothing due', async () => {
    const expires = new Date(Date.now() + 3_600_000).toISOString();
    const api = routedApi({
      'POST /api/v1/auth/password': () =>
        json(200, {
          accessToken: tokenFor(['Cashier']),
          tokenType: 'Bearer',
          expiresAtUtc: expires,
          expiresInSeconds: 3600,
          credentialsChangeDue: false,
        }),
      'GET /api/v1/payments': () => json(200, page([])),
    });
    const { user, router } = renderApp('/change-password', { session: due, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^The password you were given/), 'given password');
    await user.type(screen.getByLabelText(/^New password$/), 'my own long passphrase');
    await user.type(screen.getByLabelText(/^New password again/), 'not the same at all');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));
    expect(await screen.findByText('The two new passwords are not the same.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/auth/password')).toHaveLength(0);

    await user.clear(screen.getByLabelText(/^New password again/));
    await user.type(screen.getByLabelText(/^New password again/), 'my own long passphrase');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));

    await waitFor(() => expect(router.state.location.pathname).not.toBe('/change-password'));
    expect(JSON.parse(window.sessionStorage.getItem('billingrecon.session') ?? '{}')).toMatchObject({
      credentialsChangeDue: false,
      expiresAtUtc: expires,
    });
    expect(await api.sent('POST', '/api/v1/auth/password')[0]?.json()).toEqual({
      currentPassword: 'given password',
      newPassword: 'my own long passphrase',
    });
  });
});
