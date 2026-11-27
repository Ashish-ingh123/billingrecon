import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { json, renderApp, routedApi } from '../test/app';
import { problem } from '../test/records';
import { resetLinkFrom } from './resetLink';
import { hospitalStorageKey } from './session';

/**
 * A forgotten password in the browser (ADR 0061, amended), with the API faked: the sign-in page offers it without
 * asking the API anything; the page that asks for the link says the same whatever the API found, and says where the
 * product sends no mail; the page the link opens reads the link from after the `#`, takes it out of the address bar,
 * sets the password once and shows a spent link as one.
 */
const token = 'QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2Nzg5LV9hYmNkZWZnaGlqa2xtbm9w';
const empty = (status: number) => () => Promise.resolve(new Response(null, { status }));

afterEach(() => {
  window.history.replaceState(null, '', '/');
});

describe('the sign-in page', () => {
  it('offers the way to a forgotten password and asks the API nothing for it', async () => {
    const api = routedApi({ 'GET /api/v1/auth/options': () => json(200, { passwordResetByMail: true }) });
    const { user, router } = renderApp('/sign-in', { fetch: api.fetch });

    const link = await screen.findByRole('link', { name: 'Forgot password?' });
    expect(api.requests).toHaveLength(0);

    await user.click(link);

    expect(await screen.findByRole('heading', { name: 'Forgot your password?' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/forgot-password');
  });
});

describe('asking for the link', () => {
  it('sends the hospital and the address, trimmed, and says the same whatever the API found', async () => {
    window.localStorage.setItem(hospitalStorageKey, 'city-hospital');
    const api = routedApi({
      'GET /api/v1/auth/options': () => json(200, { passwordResetByMail: true }),
      'POST /api/v1/auth/forgot-password': empty(202),
    });
    const { user } = renderApp('/forgot-password', { fetch: api.fetch });

    // The hospital of the last sign-in is filled in, as on the sign-in page.
    expect(await screen.findByLabelText(/^Hospital code/)).toHaveValue('city-hospital');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send the link' })).toBeEnabled());

    await user.click(screen.getByRole('button', { name: 'Send the link' }));
    expect(await screen.findByText('Enter your e-mail address.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/auth/forgot-password')).toHaveLength(0);

    await user.type(screen.getByLabelText(/^E-mail address/), '  meera@cityhospital.org ');
    await user.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'If that hospital code and address belong to a user, a mail with a link is on its way.',
    );
    expect(await api.sent('POST', '/api/v1/auth/forgot-password')[0]?.json()).toEqual({
      tenant: 'city-hospital',
      email: 'meera@cityhospital.org',
    });
    expect(screen.getByRole('link', { name: 'Back to the sign-in' })).toHaveAttribute('href', '/sign-in');
    expect(screen.queryByRole('button', { name: 'Send the link' })).not.toBeInTheDocument();
  });

  it('says to ask the administrator where the product sends no mail, and offers no form', async () => {
    const api = routedApi({ 'GET /api/v1/auth/options': () => json(200, { passwordResetByMail: false }) });
    renderApp('/forgot-password', { fetch: api.fetch });

    expect(await screen.findByText(/This BillingRecon does not send mail/)).toBeInTheDocument();
    expect(screen.getByText(/administrator to set a new password for you/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send the link' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^E-mail address/)).not.toBeInTheDocument();
  });

  it("shows the API's refusal when too many were asked for from one place", async () => {
    const api = routedApi({
      'GET /api/v1/auth/options': () => json(200, { passwordResetByMail: true }),
      'POST /api/v1/auth/forgot-password': () =>
        json(429, problem(429, 'rate_limited', 'Too many attempts. Wait a minute and try again.')),
    });
    const { user } = renderApp('/forgot-password', { fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^Hospital code/), 'city-hospital');
    await user.type(screen.getByLabelText(/^E-mail address/), 'meera@cityhospital.org');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Send the link' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Send the link' }));

    expect(await screen.findByText('Too many attempts. Wait a minute and try again.')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('the page the link opens', () => {
  it('reads the link from after the #, takes it out of the address bar and sets the password once', async () => {
    window.history.replaceState(null, '', `/reset-password#token=${token}&hospital=city-hospital`);
    const api = routedApi({ 'POST /api/v1/auth/reset-password': empty(204) });
    const { user } = renderApp('/reset-password', { fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Choose a new password' })).toBeInTheDocument();
    expect(screen.getByText('city-hospital')).toBeInTheDocument();
    expect(window.location.hash).toBe('');
    expect(document.body).not.toHaveTextContent(token);

    await user.type(screen.getByLabelText(/^New password\b(?! again)/), 'too short');
    await user.type(screen.getByLabelText(/^New password again/), 'too short');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));
    expect(await screen.findByText('The new password has at least 12 characters.')).toBeInTheDocument();

    await user.clear(screen.getByLabelText(/^New password\b(?! again)/));
    await user.type(screen.getByLabelText(/^New password\b(?! again)/), 'a passphrase of my own');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));
    expect(await screen.findByText('The two new passwords are not the same.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/auth/reset-password')).toHaveLength(0);

    await user.clear(screen.getByLabelText(/^New password again/));
    await user.type(screen.getByLabelText(/^New password again/), 'a passphrase of my own');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Your new password is set');
    expect(await api.sent('POST', '/api/v1/auth/reset-password')[0]?.json()).toEqual({
      tenant: 'city-hospital',
      token,
      newPassword: 'a passphrase of my own',
    });
    // The sign-in that follows has the hospital filled in.
    expect(window.localStorage.getItem(hospitalStorageKey)).toBe('city-hospital');
    expect(screen.getByRole('link', { name: 'Go to the sign-in' })).toHaveAttribute('href', '/sign-in');
  });

  it('says a spent link is one, and offers a new one', async () => {
    window.history.replaceState(null, '', `/reset-password#token=${token}&hospital=city-hospital`);
    const api = routedApi({
      'POST /api/v1/auth/reset-password': () =>
        json(
          422,
          problem(
            422,
            'password_reset_link_invalid',
            'This link is not good any more: it works once, for 30 minutes. Ask for a new one.',
          ),
        ),
    });
    const { user } = renderApp('/reset-password', { fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^New password\b(?! again)/), 'a passphrase of my own');
    await user.type(screen.getByLabelText(/^New password again/), 'a passphrase of my own');
    await user.click(screen.getByRole('button', { name: 'Save the new password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('This link is not good any more');
    expect(screen.getByRole('link', { name: 'Ask for a new link' })).toHaveAttribute('href', '/forgot-password');
    expect(screen.queryByRole('button', { name: 'Save the new password' })).not.toBeInTheDocument();
  });

  it('opened without a link, says so and asks the API nothing', async () => {
    const api = routedApi({});
    renderApp('/reset-password', { fetch: api.fetch });

    expect(await screen.findByRole('alert')).toHaveTextContent('This link is not good any more');
    expect(api.requests).toHaveLength(0);
  });
});

describe('the parts of a link', () => {
  it('are read from the fragment, with or without its #', () => {
    expect(resetLinkFrom(`#token=${token}&hospital=city-hospital`)).toEqual({ token, hospital: 'city-hospital' });
    expect(resetLinkFrom(`hospital=apollo%20delhi&token=${token}`)).toEqual({ token, hospital: 'apollo delhi' });
  });

  it.each([
    '',
    '#',
    `#token=${token}`,
    '#hospital=city-hospital',
    '#token=short&hospital=city-hospital',
    `#token=${token}%3Cscript%3E&hospital=city-hospital`,
    `#token=${'A'.repeat(101)}&hospital=city-hospital`,
    `#token=${token}&hospital=${'h'.repeat(65)}`,
  ])('are nothing for %j', (hash) => {
    expect(resetLinkFrom(hash)).toBeNull();
  });
});
