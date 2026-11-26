import { act, configure, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { page, patient, payer, problem } from '../test/records';
import { searchPause } from './useLiveSearch';

/**
 * The record picker as an autocomplete (Prompt 10.2b; ADR 0040, amended 2026-11-25), on the new bill's patient: the
 * results open while the text is typed, in the combobox pattern of WAI-ARIA; the keyboard alone chooses, and so does
 * the pointer; Enter never submits the form around the box; there is no Find button.
 */
// A search is sent after its pause: what follows it is waited for longer than the second a test waits by default.
configure({ asyncUtilTimeout: 3000 });

const clerk = sessionFor(['BillingClerk']);
const asha = patient();
const ravi = patient({ id: 'a1000000-0000-4000-8000-000000000002', mrn: 'MRN-2002', fullName: 'Ravi Verma' });
const ashasPayer = payer();
const patients = '/api/v1/patients';

/** What the picker of this box announces: each picker of the form has an announcement of its own. */
function announcementOf(box: HTMLElement): HTMLElement {
  return within(box.parentElement?.parentElement ?? box).getByRole('status');
}

function rest(ms: number): Promise<void> {
  return act(() => new Promise<void>((resolve) => setTimeout(resolve, ms)));
}

function routes(find: Handler): Record<string, Handler> {
  return {
    [`GET ${patients}`]: find,
    'GET /api/v1/payers': () => json(200, page([ashasPayer])),
    'GET /api/v1/service-items': () => json(200, page([])),
  };
}

describe('the record picker, an autocomplete', () => {
  it('opens the results while the text is typed: nothing for one character, one request after the pause', async () => {
    const api = routedApi(routes(() => json(200, page([asha, ravi]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });
    expect(screen.queryByRole('button', { name: 'Find' })).not.toBeInTheDocument();
    expect(box).toHaveAttribute('aria-expanded', 'false');
    expect(box).toHaveAttribute('aria-autocomplete', 'list');

    await user.type(box, 'v');
    await rest(searchPause + 100);
    expect(api.sent('GET', patients)).toHaveLength(0);

    await user.type(box, 'erm');
    expect(api.sent('GET', patients)).toHaveLength(0);
    const list = await screen.findByRole('listbox', { name: 'patient found' });
    expect(api.sent('GET', patients)).toHaveLength(1);
    expect(Object.fromEntries(new URL(api.sent('GET', patients)[0]?.url ?? '').searchParams)).toEqual({
      Search: 'verm',
      Status: 'Active',
      Page: '1',
      PageSize: '10',
    });
    expect(within(list).getAllByRole('option')).toHaveLength(2);
    expect(box).toHaveAttribute('aria-expanded', 'true');
    expect(box).toHaveAttribute('aria-controls', list.id);
    expect(announcementOf(box)).toHaveTextContent('2 found. Use the arrow keys to choose one.');
  });

  it('is used by the keyboard alone: the arrows move, Escape closes, Enter chooses and never submits the form', async () => {
    const api = routedApi(routes(() => json(200, page([asha, ravi]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'verma');
    const options = within(await screen.findByRole('listbox')).getAllByRole('option');
    // Since 2026-11-26 (ADR 0040, amended) the first result is the marked one as soon as the results open.
    expect(box).toHaveAttribute('aria-activedescendant', options[0]?.id);
    expect(options[0]).toHaveAttribute('aria-selected', 'true');

    await user.keyboard('{ArrowDown}');
    expect(box).toHaveAttribute('aria-activedescendant', options[1]?.id);
    expect(options[0]).toHaveAttribute('aria-selected', 'false');
    expect(options[1]).toHaveAttribute('aria-selected', 'true');
    await user.keyboard('{ArrowDown}');
    expect(box).toHaveAttribute('aria-activedescendant', options[0]?.id);
    await user.keyboard('{ArrowUp}');
    expect(box).toHaveAttribute('aria-activedescendant', options[1]?.id);
    await user.keyboard('{ArrowUp}');
    expect(box).toHaveAttribute('aria-activedescendant', options[0]?.id);
    await user.keyboard('{ArrowDown}');
    expect(box).toHaveAttribute('aria-activedescendant', options[1]?.id);

    // Escape closes the results and keeps the text; an arrow opens them again.
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(box).toHaveAttribute('aria-expanded', 'false');
    expect(box).toHaveValue('verma');
    expect(box).toHaveFocus();
    await user.keyboard('{ArrowUp}');
    expect(box).toHaveAttribute('aria-activedescendant', screen.getAllByRole('option')[1]?.id);

    await user.keyboard('{Enter}');
    expect(await screen.findByText('Ravi Verma (MRN MRN-2002)')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Patient' })).not.toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/bills')).toHaveLength(0);
    expect(screen.getAllByRole('button', { name: 'Change' }).length).toBeGreaterThan(0);
  });

  it('is used by the pointer: a result is chosen where it is pressed', async () => {
    const api = routedApi(routes(() => json(200, page([asha, ravi]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByRole('combobox', { name: 'Patient' }), 'verma');
    await user.click(await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' }));

    expect(await screen.findByText('Asha Verma (MRN MRN-1001)')).toBeInTheDocument();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/bills')).toHaveLength(0);
  });

  it('searches at once on Enter when no result is marked, and never submits the form', async () => {
    const api = routedApi(routes(() => json(200, page([asha]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByRole('combobox', { name: 'Patient' }), 'asha{Enter}');

    expect(api.sent('GET', patients)).toHaveLength(1);
    expect(await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' })).toBeInTheDocument();
    await rest(searchPause + 100);
    expect(api.sent('GET', patients)).toHaveLength(1);
    expect(api.sent('POST', '/api/v1/bills')).toHaveLength(0);
  });

  it('says when nothing matches and when the search was refused, and closes when the box is left', async () => {
    const api = routedApi(
      routes((request) =>
        new URL(request.url).searchParams.get('Search') === 'zz'
          ? json(200, page([]))
          : json(403, problem(403, 'forbidden', 'You may not read patients.')),
      ),
    );
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'zz');
    await waitFor(() => expect(announcementOf(box)).toHaveTextContent('No active patient matches.'));
    expect(box).toHaveAttribute('aria-expanded', 'false');

    await user.type(box, 'z');
    await waitFor(() => expect(announcementOf(box)).not.toHaveTextContent('No active patient matches.'));
    await waitFor(() => expect(announcementOf(box)).not.toHaveTextContent('Searching…'));
    expect(announcementOf(box)).not.toBeEmptyDOMElement();

    await user.tab();
    expect(announcementOf(box)).toBeEmptyDOMElement();
  });

  it('drops the answer of a search a later key made stale', async () => {
    const waiting = new Map<string, { request: Request; answer: (response: Response) => void }>();
    const api = routedApi(
      routes(
        (request) =>
          new Promise<Response>((resolve) => {
            waiting.set(new URL(request.url).searchParams.get('Search') ?? '', { request, answer: resolve });
          }),
      ),
    );
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 've');
    await waitFor(() => expect(waiting.has('ve')).toBe(true));
    await user.type(box, 'rma');
    await waitFor(() => expect(waiting.has('verma')).toBe(true));
    expect(waiting.get('ve')?.request.signal.aborted).toBe(true);

    waiting.get('verma')?.answer(json(200, page([asha])));
    expect(await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' })).toBeInTheDocument();
    waiting.get('ve')?.answer(json(200, page([ravi])));
    await rest(50);
    expect(screen.getAllByRole('option')).toHaveLength(1);
    expect(screen.queryByRole('option', { name: 'Ravi Verma (MRN MRN-2002)' })).not.toBeInTheDocument();
  });
});
