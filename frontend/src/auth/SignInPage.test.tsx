import { render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { supportEmail } from '../config';
import { json, renderApp, tokenFor } from '../test/app';
import { SignInBackdrop } from './SignInPage';

/**
 * The sign-in page as the reference shows it (Prompt 9.1, ADR 0047; docs/ui/sign-in.png): what it shows around the
 * form, and what the form gained (the password's show/hide, the pending button, the support link). What signing in does
 * is held by SignIn.test.tsx, unchanged.
 */
afterEach(() => {
  vi.unstubAllEnvs();
});

describe('the sign-in page (ADR 0047)', () => {
  it('shows the product, its three promises and the company that makes it', async () => {
    renderApp('/sign-in');

    expect(await screen.findByRole('heading', { level: 1, name: 'Sign in to BillingRecon' })).toBeInTheDocument();
    expect(screen.getByText('Access your hospital’s billing dashboard')).toBeInTheDocument();

    const brand = screen.getByRole('region', { name: 'BillingRecon' });
    expect(within(brand).getByText('Reconcile. Verify. Resolve.')).toBeInTheDocument();
    expect(
      within(brand)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual([
      'Secure hospital billing reconciliation',
      'Trusted financial insights',
      'Better care through accurate billing',
    ]);

    // The company's logo is named once, by its alternative text; the name set beside it is for the eye.
    expect(screen.getByText('A product of')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'ashline' })).toBeInTheDocument();
    expect(document.title).toBe('Sign in · BillingRecon');
  });

  it('ties each label to its field, with its placeholder and what a password manager needs', async () => {
    renderApp('/sign-in');

    expect(await screen.findByLabelText('Hospital code')).toHaveAttribute('autocomplete', 'organization');
    const email = screen.getByLabelText('E-mail address');
    expect(email).toHaveAttribute('autocomplete', 'username');
    expect(email).toHaveAttribute('placeholder', 'you@hospital.org');
    const password = screen.getByLabelText('Password');
    expect(password).toHaveAttribute('autocomplete', 'current-password');
    expect(password).toHaveAttribute('placeholder', 'Enter your password');
    expect(screen.getByLabelText('Theme')).toHaveValue('system');
  });

  it('shows and hides the password with one button that says which', async () => {
    const { user } = renderApp('/sign-in');
    const password = await screen.findByLabelText('Password');
    const toggle = screen.getByRole('button', { name: 'Show password' });

    expect(password).toHaveAttribute('type', 'password');
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    expect(toggle).toHaveAttribute('type', 'button');

    await user.type(password, 'correct horse battery');
    await user.click(toggle);

    expect(password).toHaveAttribute('type', 'text');
    expect(password).toHaveValue('correct horse battery');
    expect(screen.getByRole('button', { name: 'Show password' })).toHaveAttribute('aria-pressed', 'true');

    await user.click(screen.getByRole('button', { name: 'Show password' }));

    expect(password).toHaveAttribute('type', 'password');
    expect(password).toHaveValue('correct horse battery');
  });

  it('says it is signing in, and takes no second press, while the request runs', async () => {
    let answer: (response: Response) => void = () => undefined;
    const requests: Request[] = [];
    const fetch = (request: Request) => {
      requests.push(request);
      return new Promise<Response>((resolve) => (answer = resolve));
    };
    const { user } = renderApp('/sign-in', { fetch });

    await user.type(await screen.findByLabelText('Hospital code'), 'city-hospital');
    await user.type(screen.getByLabelText('E-mail address'), 'asha@example.org');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    const pending = await screen.findByRole('button', { name: 'Signing in…' });
    expect(pending).toBeDisabled();
    expect(requests).toHaveLength(1);

    answer(
      json(200, {
        accessToken: tokenFor(['Cashier']),
        tokenType: 'Bearer',
        expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
        expiresInSeconds: 3600,
      }),
    );

    expect(await screen.findByRole('heading', { name: 'Payments' })).toBeInTheDocument();
  });

  it('offers "Contact support" as a mailto when the build was given an address', async () => {
    vi.stubEnv('VITE_SUPPORT_EMAIL', 'support@hospital.example');
    renderApp('/sign-in');

    expect(await screen.findByText('Need help?')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Contact support' })).toHaveAttribute(
      'href',
      'mailto:support@hospital.example',
    );
  });

  it('leaves the divider and the link out when the build was given none', async () => {
    vi.stubEnv('VITE_SUPPORT_EMAIL', '');
    renderApp('/sign-in');

    await screen.findByRole('heading', { name: 'Sign in to BillingRecon' });
    expect(screen.queryByText('Need help?')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Contact support' })).not.toBeInTheDocument();
  });
});

describe('the support address of the build', () => {
  it('is an e-mail address or nothing', () => {
    expect(supportEmail(' support@hospital.example ')).toBe('support@hospital.example');
    expect(supportEmail(undefined)).toBeNull();
    expect(supportEmail('')).toBeNull();
    expect(supportEmail('not an address')).toBeNull();
    expect(supportEmail('a@b.c?subject=<script>')).toBeNull();
    expect(supportEmail(42)).toBeNull();
  });
});

describe('the ground of the sign-in page', () => {
  const widen = (wide: boolean) => {
    const before = window.matchMedia.bind(window);
    window.matchMedia = (query: string) => ({ ...before(query), matches: query === '(min-width: 900px)' && wide });
  };

  it('is decoration: hidden from assistive technology, the photograph with an empty alternative text', () => {
    widen(true);
    const { container } = render(<SignInBackdrop photo="/photo.webp" />);

    expect(container.firstElementChild).toHaveAttribute('aria-hidden', 'true');
    const photograph = container.querySelector('img');
    expect(photograph).toHaveAttribute('alt', '');
    expect(photograph).toHaveAttribute('src', '/photo.webp');
  });

  it('does not ask for the photograph on a narrow window, where it would not show', () => {
    widen(false);
    const { container } = render(<SignInBackdrop photo="/photo.webp" />);

    expect(container.querySelector('img')).toBeNull();
  });

  it('stands without a photograph', () => {
    widen(true);
    const { container } = render(<SignInBackdrop photo={null} />);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelectorAll('svg').length).toBeGreaterThan(0);
  });
});
