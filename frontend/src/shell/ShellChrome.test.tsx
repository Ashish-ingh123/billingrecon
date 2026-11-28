import { render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Badge } from '../components/Badge';
import { LoadState } from '../components/Loading';
import { StatusBadge } from '../components/StatusBadge';
import { toneIcons, type Tone } from '../components/tones';
import { renderApp, routedApi, sessionFor } from '../test/app';
import { railStorageKey } from './AppShell';

/**
 * The shell and the shared pieces of Prompt 9.2 (ADR 0048): the rail, the drawer, the title of the tab, the focus after a
 * navigation, the company in the sidebar, the theme pill, the badges and the skeleton. What the shell shows each role is
 * held by Shell.test.tsx, unchanged.
 */
const patientsApi = () =>
  routedApi({
    'GET /api/v1/patients': () =>
      new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    'GET /api/v1/payers': () =>
      new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
  });

describe('the shell (ADR 0048)', () => {
  it('names the product at the top of the sidebar and the company at its foot', async () => {
    renderApp('/patients', { session: sessionFor(['BillingClerk']), fetch: patientsApi().fetch });

    const sidebar = await screen.findByRole('complementary', { name: 'Sections' });
    expect(within(sidebar).getByText('BillingRecon')).toBeInTheDocument();
    expect(within(sidebar).getByText('BillingRecon by')).toBeInTheDocument();
    expect(within(sidebar).getByRole('img', { name: 'Ashvanta Technologies' })).toBeInTheDocument();
  });

  it('collapses the navigation to a rail, keeps its names, and remembers the choice', async () => {
    const { user, unmount } = renderApp('/patients', {
      session: sessionFor(['BillingClerk']),
      fetch: patientsApi().fetch,
    });
    const toggle = await screen.findByRole('button', { name: 'Collapse the navigation' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');

    await user.click(toggle);

    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    expect(window.localStorage.getItem(railStorageKey)).toBe('rail');
    // The names stay for assistive technology, and as tooltips.
    const navigation = screen.getByRole('navigation', { name: 'Main' });
    expect(within(navigation).getByRole('link', { name: 'Bills' })).toHaveAttribute('title', 'Bills');
    unmount();

    renderApp('/patients', { session: sessionFor(['BillingClerk']), fetch: patientsApi().fetch });
    expect(await screen.findByRole('button', { name: 'Collapse the navigation' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('opens the drawer from the menu button, closes it with Escape, and gives the focus back', async () => {
    const { user } = renderApp('/patients', { session: sessionFor(['BillingClerk']), fetch: patientsApi().fetch });
    const menu = await screen.findByRole('button', { name: 'Menu' });
    expect(menu).toHaveAttribute('aria-expanded', 'false');
    expect(menu).toHaveAttribute('aria-controls', 'sidebar');

    await user.click(menu);

    expect(menu).toHaveAttribute('aria-expanded', 'true');
    await waitFor(() => expect(document.activeElement?.closest('#sidebar')).not.toBeNull());

    await user.keyboard('{Escape}');

    expect(menu).toHaveAttribute('aria-expanded', 'false');
    expect(menu).toHaveFocus();
  });

  it('names the section in the tab, and gives the focus to the heading of the page it opened', async () => {
    const { user } = renderApp('/patients', { session: sessionFor(['BillingClerk']), fetch: patientsApi().fetch });
    await screen.findByRole('heading', { level: 1, name: 'Patients' });
    expect(document.title).toBe('Patients · BillingRecon');

    await user.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', { name: 'Payers' }));

    const heading = await screen.findByRole('heading', { level: 1, name: 'Payers' });
    expect(document.title).toBe('Payers · BillingRecon');
    await waitFor(() => expect(heading).toHaveFocus());
  });

  it('offers the theme as a pill of three, the choice applied at once', async () => {
    const { user } = renderApp('/patients', { session: sessionFor(['BillingClerk']), fetch: patientsApi().fetch });
    const pill = await screen.findByRole('radiogroup', { name: 'Theme' });

    expect(
      within(pill)
        .getAllByRole('radio')
        .map((radio) => radio.getAttribute('aria-label')),
    ).toEqual(['Light', 'Dark', 'System']);
    await user.click(within(pill).getByRole('radio', { name: 'Dark' }));
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark');
  });
});

describe('the shared pieces (ADR 0048)', () => {
  it('says a status by its word and an icon as well as its colour, for every tone', () => {
    const tones = Object.keys(toneIcons) as Tone[];
    expect(tones.sort()).toEqual(['accent', 'danger', 'muted', 'success', 'warning']);

    for (const tone of tones) {
      const { container, unmount } = render(<Badge tone={tone}>{tone}</Badge>);
      expect(container.querySelector('svg'), tone).not.toBeNull();
      expect(container).toHaveTextContent(tone);
      unmount();
    }

    render(<StatusBadge status="Inactive" />);
    expect(screen.getByText('Inactive')).toBeInTheDocument();
  });

  it('shows a skeleton while it reads, busy, with the words said once', () => {
    const { container } = render(<LoadState error={null} what="patients" />);

    expect(container.firstElementChild).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Loading patients…');
  });
});
