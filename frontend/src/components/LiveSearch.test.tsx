import { act, configure, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { page, patient } from '../test/records';
import { searchPause } from './useLiveSearch';

/**
 * A list answers while its search is typed (Prompt 10.2b; ADR 0039, amended 2026-11-25): after a pause, from two
 * characters, with the rows on screen kept until the new ones arrive, a stale request cancelled and its answer
 * dropped, the address replaced and not pushed, and the first page after a search. No screen has a Search or Find
 * button. The timers are real: a test waits for what a user would see.
 */
// A search is sent after its pause: what follows it is waited for longer than the second a test waits by default.
configure({ asyncUtilTimeout: 3000 });

const clerk = sessionFor(['BillingClerk']);
const asha = patient();
const ravi = patient({ id: 'a1000000-0000-4000-8000-000000000002', mrn: 'MRN-2002', fullName: 'Ravi Iyer' });
const patients = '/api/v1/patients';

function rest(ms: number): Promise<void> {
  return act(() => new Promise<void>((resolve) => setTimeout(resolve, ms)));
}

function searchOf(request: Request | undefined): string | null {
  return new URL(request?.url ?? 'http://none').searchParams.get('Search');
}

describe('the search of a list, sent while it is typed', () => {
  it('sends nothing before the pause and one request after it, with no button to press', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    expect(screen.queryByRole('button', { name: 'Search' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Find' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('MRN or name'), 'ver');
    expect(api.sent('GET', patients)).toHaveLength(1);

    await waitFor(() => expect(api.sent('GET', patients)).toHaveLength(2));
    expect(searchOf(api.sent('GET', patients)[1])).toBe('ver');

    // The three keys were one search: nothing follows it.
    await rest(searchPause + 100);
    expect(api.sent('GET', patients)).toHaveLength(2);
  });

  it('sends nothing for one character, and the whole list again when the box is emptied', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user, router } = renderApp('/patients?search=asha', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');

    await user.clear(box);
    await user.type(box, 'v');
    await rest(searchPause + 100);
    expect(api.sent('GET', patients).map(searchOf)).toEqual(['asha']);
    expect(router.state.location.search).toBe('?search=asha');

    await user.type(box, '{Backspace}');
    await waitFor(() => expect(router.state.location.search).toBe(''));
    await waitFor(() => expect(api.sent('GET', patients).map(searchOf)).toEqual(['asha', null]));
  });

  it('sends at once on Enter, without waiting for the pause', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user, router } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });

    await user.type(screen.getByLabelText('MRN or name'), 'verma{Enter}');

    expect(router.state.location.search).toBe('?search=verma');
    await rest(searchPause + 100);
    expect(api.sent('GET', patients).map(searchOf)).toEqual([null, 'verma']);
  });

  it('keeps the rows on screen until the new ones arrive', async () => {
    let answerSearch: (response: Response) => void = () => undefined;
    const api = routedApi({
      [`GET ${patients}`]: (request) =>
        searchOf(request) === null
          ? json(200, page([asha]))
          : new Promise<Response>((resolve) => {
              answerSearch = resolve;
            }),
    });
    const { user } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });

    await user.type(screen.getByLabelText('MRN or name'), 'iyer');
    await waitFor(() => expect(api.sent('GET', patients)).toHaveLength(2));
    expect(screen.getByRole('link', { name: 'Asha Verma' })).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();

    answerSearch(json(200, page([ravi])));
    expect(await screen.findByRole('link', { name: 'Ravi Iyer' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Asha Verma' })).not.toBeInTheDocument();
  });

  it('cancels a request a later key made stale, and never shows its answer', async () => {
    const waiting = new Map<string, { request: Request; answer: (response: Response) => void }>();
    const api = routedApi({
      [`GET ${patients}`]: (request) => {
        const search = searchOf(request);
        return search === null
          ? json(200, page([]))
          : new Promise<Response>((resolve) => {
              waiting.set(search, { request, answer: resolve });
            });
      },
    });
    const { user } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByText('No patient is registered yet.');
    const box = screen.getByLabelText('MRN or name');

    await user.type(box, 'as');
    await waitFor(() => expect(waiting.has('as')).toBe(true));
    await user.type(box, 'ha');
    await waitFor(() => expect(waiting.has('asha')).toBe(true));
    expect(waiting.get('as')?.request.signal.aborted).toBe(true);

    // The newer answer arrives first, the stale one after it: the stale one changes nothing.
    waiting.get('asha')?.answer(json(200, page([asha])));
    expect(await screen.findByRole('link', { name: 'Asha Verma' })).toBeInTheDocument();
    waiting.get('as')?.answer(json(200, page([ravi])));
    await rest(50);
    expect(screen.getByRole('link', { name: 'Asha Verma' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Ravi Iyer' })).not.toBeInTheDocument();
  });

  it('replaces the address at each search, so Back leaves the list, and returns to the first page', async () => {
    const api = routedApi({
      [`GET ${patients}`]: () => json(200, page([asha], 45, 1)),
      'GET /api/v1/payers': () => json(200, page([])),
    });
    const { user, router } = renderApp('/payers', { session: clerk, fetch: api.fetch });
    await screen.findByRole('heading', { name: 'Payers' });
    await act(() => router.navigate('/patients?page=3'));
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');

    await user.type(box, 'ver');
    await waitFor(() => expect(router.state.location.search).toBe('?search=ver'));
    await user.type(box, 'ma');
    await waitFor(() => expect(router.state.location.search).toBe('?search=verma'));
    expect(box).toHaveValue('verma');
    expect(new URL(api.sent('GET', patients).at(-1)?.url ?? '').searchParams.get('Page')).toBe('1');

    await act(() => router.navigate(-1));
    await waitFor(() => expect(router.state.location.pathname).toBe('/payers'));
  });

  it('keeps a space typed between two words, and shows the search of the address after Back', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user, router } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');

    await user.type(box, 'asha ');
    await waitFor(() => expect(router.state.location.search).toBe('?search=asha'));
    expect(box).toHaveValue('asha ');

    // A filter is a step of its own in the history; going back over it shows the search it had.
    await user.selectOptions(screen.getByLabelText('Status'), 'Active');
    await waitFor(() => expect(router.state.location.search).toBe('?search=asha&status=Active'));
    await act(() => router.navigate('/patients?search=iyer'));
    await waitFor(() => expect(box).toHaveValue('iyer'));
  });
});

describe('the other search boxes', () => {
  it('finds users by a part of their address while it is typed', async () => {
    const api = routedApi({ 'GET /api/v1/users': () => json(200, page([])) });
    const { user, router } = renderApp('/administration', { session: sessionFor(['TenantAdmin']), fetch: api.fetch });
    await screen.findByText('No user is set up yet.');
    expect(screen.queryByRole('button', { name: 'Find' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('E-mail address'), 'r');
    await rest(searchPause + 100);
    expect(api.sent('GET', '/api/v1/users')).toHaveLength(1);

    await user.type(screen.getByLabelText('E-mail address'), 'ao@city');
    await waitFor(() => expect(router.state.location.search).toBe('?email=rao%40city'));
    await waitFor(() =>
      expect(new URL(api.sent('GET', '/api/v1/users').at(-1)?.url ?? '').searchParams.get('Email')).toBe('rao@city'),
    );
    expect(api.sent('GET', '/api/v1/users')).toHaveLength(2);
    expect(await screen.findByText('No user matches.')).toBeInTheDocument();
  });

  it('narrows the outstanding report by a part of the payer name while it is typed', async () => {
    const report = {
      period: null,
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totals: { payers: 0, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 },
    };
    const outstanding = '/api/v1/reports/outstanding-by-payer';
    const api = routedApi({ [`GET ${outstanding}`]: () => json(200, report) });
    const { user, router } = renderApp('/reports/outstanding', { session: sessionFor(['Viewer']), fetch: api.fetch });
    await screen.findByText('No payer owes or holds anything.');
    expect(screen.queryByRole('button', { name: 'Search' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Payer name'), 'ver');
    expect(api.sent('GET', outstanding)).toHaveLength(1);
    await waitFor(() => expect(router.state.location.search).toBe('?search=ver'));
    await waitFor(() => expect(searchOf(api.sent('GET', outstanding).at(-1))).toBe('ver'));
    expect(api.sent('GET', outstanding)).toHaveLength(2);
    expect(await screen.findByText('No payer owes or holds anything by that name.')).toBeInTheDocument();
  });
});
