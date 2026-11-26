import { act, configure, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { page, patient, payer } from '../test/records';
import { noRecordChosen } from './RecordPicker';
import { searchPause } from './useLiveSearch';

/**
 * The lens of a search box, and leaving one (Prompt 10.4d; ADR 0039 and ADR 0040, amended 2026-11-26): every search box
 * carries a lens that is drawn and not read, the spinner in its place while a search runs, and a way to clear it; a
 * list's box sends its search at once when it is left; and a record picker is filled by typing a few letters and
 * pressing Tab, which takes the first result with it, or a single match alone where money would follow a wrong one.
 * The timers are real: a test waits for what a user would see.
 */
configure({ asyncUtilTimeout: 3000 });

const clerk = sessionFor(['BillingClerk']);
const asha = patient();
const ravi = patient({ id: 'a1000000-0000-4000-8000-000000000002', mrn: 'MRN-2002', fullName: 'Ravi Verma' });
const ashasPayer = payer();
const ravisPayer = payer({ id: 'b2000000-0000-4000-8000-000000000003', name: 'Ravi Verma', patientId: ravi.id });
const patients = '/api/v1/patients';

function rest(ms: number): Promise<void> {
  return act(() => new Promise<void>((resolve) => setTimeout(resolve, ms)));
}

const searchOf = (request: Request | undefined) => new URL(request?.url ?? 'http://none').searchParams.get('Search');

/** The lens or the spinner of a search box: the one drawing at its start. */
function leadOf(box: HTMLElement): HTMLElement {
  const lead = box.parentElement?.querySelector('[data-testid^="search-"]');
  expect(lead, 'the lens of the box').not.toBeNull();
  return lead as HTMLElement;
}

describe('the lens of a search box', () => {
  const report = {
    period: null,
    items: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
    totals: { payers: 0, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 },
  };
  const lists = [
    { path: '/patients', label: 'MRN or name', roles: ['BillingClerk'], route: 'GET /api/v1/patients' },
    { path: '/payers', label: 'Name', roles: ['BillingClerk'], route: 'GET /api/v1/payers' },
    { path: '/service-items', label: 'Code or name', roles: ['BillingClerk'], route: 'GET /api/v1/service-items' },
    { path: '/administration', label: 'E-mail address', roles: ['TenantAdmin'], route: 'GET /api/v1/users' },
    {
      path: '/reports/outstanding',
      label: 'Payer name',
      roles: ['Viewer'],
      route: 'GET /api/v1/reports/outstanding-by-payer',
    },
  ] as const;

  it.each(lists)('stands in the search of $path, drawn and not read, and is no button', async (list) => {
    const api = routedApi({ [list.route]: () => json(200, list.path === '/reports/outstanding' ? report : page([])) });
    renderApp(list.path, { session: sessionFor(list.roles), fetch: api.fetch });

    const box = await screen.findByLabelText(list.label);
    const lens = leadOf(box);
    await waitFor(() => expect(lens).toHaveAttribute('data-testid', 'search-lens'));
    expect(lens).toHaveAttribute('aria-hidden', 'true');
    expect(lens.querySelector('svg')).not.toBeNull();
    expect(lens.querySelector('svg')?.innerHTML).toContain('currentColor');
    expect(lens.tagName).toBe('SPAN');
    // Nothing of it is in the accessibility tree, and no Search or Find button came back with it.
    expect(within(box.parentElement as HTMLElement).queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^(Search|Find)$/ })).not.toBeInTheDocument();
    // An empty box offers nothing to clear.
    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument();
  });

  it('stands in every record picker too', async () => {
    const api = routedApi({ 'GET /api/v1/service-items': () => json(200, page([])) });
    renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    for (const name of ['Patient', 'Payer', 'Add a line from the catalogue']) {
      const lens = leadOf(await screen.findByRole('combobox', { name }));
      expect(lens).toHaveAttribute('data-testid', 'search-lens');
      expect(lens).toHaveAttribute('aria-hidden', 'true');
    }
    expect(screen.queryByRole('button', { name: /^(Search|Find)$/ })).not.toBeInTheDocument();
  });

  it('gives its place to the spinner while a search is in flight, and takes it back', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    let calls = 0;
    const api = routedApi({
      [`GET ${patients}`]: async () => {
        calls += 1;
        if (calls > 1) {
          await held;
        }
        return json(200, page([asha]));
      },
    });
    const { user } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');
    const place = leadOf(box);
    await waitFor(() => expect(place).toHaveAttribute('data-testid', 'search-lens'));
    const classOfPlace = place.className;

    await user.type(box, 'ver{Enter}');
    await waitFor(() => expect(leadOf(box)).toHaveAttribute('data-testid', 'search-spinner'));
    // The very same place, of the same class and so the same size: nothing shifts.
    expect(leadOf(box)).toBe(place);
    expect(place.className).toBe(classOfPlace);
    expect(place).toHaveAttribute('aria-hidden', 'true');

    release();
    await waitFor(() => expect(leadOf(box)).toHaveAttribute('data-testid', 'search-lens'));
  });
});

describe('the Clear button of a search box', () => {
  it('appears when the box holds text, and empties the box, the address and the search, keeping the focus', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user, router } = renderApp('/patients?search=asha', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');
    expect(box).toHaveValue('asha');

    await user.click(screen.getByRole('button', { name: 'Clear search' }));

    expect(box).toHaveValue('');
    expect(box).toHaveFocus();
    await waitFor(() => expect(router.state.location.search).toBe(''));
    await waitFor(() => expect(api.sent('GET', patients).map(searchOf)).toEqual(['asha', null]));
    expect(screen.queryByRole('button', { name: 'Clear search' })).not.toBeInTheDocument();
  });

  it('empties a record picker and closes its results', async () => {
    const api = routedApi({
      [`GET ${patients}`]: () => json(200, page([asha, ravi])),
      'GET /api/v1/service-items': () => json(200, page([])),
    });
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });
    await user.type(box, 'verma');
    await screen.findByRole('listbox');

    await user.click(within(box.parentElement as HTMLElement).getByRole('button', { name: 'Clear search' }));

    expect(box).toHaveValue('');
    expect(box).toHaveFocus();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(box).toHaveAttribute('aria-invalid', 'false');
  });
});

describe('leaving the search box of a list', () => {
  it('sends the search at once, before the pause, when the text changed and is long enough', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user, router } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });

    await user.type(screen.getByLabelText('MRN or name'), 'ver');
    expect(api.sent('GET', patients)).toHaveLength(1);
    await user.tab();

    // At once: the request is on its way well inside the pause.
    await waitFor(() => expect(api.sent('GET', patients)).toHaveLength(2), { timeout: searchPause / 2 });
    expect(searchOf(api.sent('GET', patients)[1])).toBe('ver');
    expect(router.state.location.search).toBe('?search=ver');

    // The pause that was running sends nothing more.
    await rest(searchPause + 100);
    expect(api.sent('GET', patients)).toHaveLength(2);
  });

  it.each([
    ['Shift+Tab', '{Shift>}{Tab}{/Shift}'],
    ['a click elsewhere', null],
  ])('does the same on %s', async (_, keys) => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user } = renderApp('/patients', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });

    await user.type(screen.getByLabelText('MRN or name'), 'ver');
    if (keys === null) {
      await user.click(screen.getByRole('heading', { level: 1 }));
    } else {
      await user.keyboard(keys);
    }

    await waitFor(() => expect(api.sent('GET', patients)).toHaveLength(2), { timeout: searchPause / 2 });
    expect(searchOf(api.sent('GET', patients)[1])).toBe('ver');
  });

  it('sends nothing when the text is the search already shown, or too short to be one', async () => {
    const api = routedApi({ [`GET ${patients}`]: () => json(200, page([asha])) });
    const { user } = renderApp('/patients?search=asha', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });
    const box = screen.getByLabelText('MRN or name');

    // Unchanged: focused and left.
    await user.click(box);
    await user.tab();
    // Changed and back to what it was, with a space the search never held.
    await user.click(box);
    await user.type(box, ' ');
    await user.tab();
    await rest(searchPause + 100);
    expect(api.sent('GET', patients).map(searchOf)).toEqual(['asha']);

    // One character is not a search: leaving the box does not send it.
    await user.clear(box);
    await user.type(box, 'v');
    await user.tab();
    await rest(searchPause + 100);
    expect(api.sent('GET', patients).map(searchOf)).toEqual(['asha']);
  });
});

describe('a record picker, left by the Tab key', () => {
  const routes = (find: Handler): Record<string, Handler> => ({
    [`GET ${patients}`]: find,
    'GET /api/v1/payers': () => json(200, page([ashasPayer])),
    'GET /api/v1/service-items': () => json(200, page([])),
  });

  it('with one result takes it, closes the list and is in the next field, in one press', async () => {
    const api = routedApi(routes(() => json(200, page([asha]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'ash');
    await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' });
    await user.tab();

    // The record is shown by its name and its number, and said aloud: the user has moved on.
    expect(await screen.findByText('Asha Verma (MRN MRN-1001)', { selector: 'span' })).toBeInTheDocument();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Patient' })).not.toBeInTheDocument();
    expect(screen.getByText('Chosen: Asha Verma (MRN MRN-1001)')).toHaveAttribute('role', 'status');

    // The next field is the payer, which the bill fills with the patient's own: the focus is there, not lost.
    await screen.findByText("Asha Verma (Self-pay, a patient's own payer)");
    await waitFor(() => expect(document.activeElement).not.toBe(document.body));
    const payerField = screen.getByText("Asha Verma (Self-pay, a patient's own payer)").parentElement as HTMLElement;
    expect(payerField).toContainElement(document.activeElement as HTMLElement);
  });

  it('with several results takes the first, or the one the arrows marked', async () => {
    const api = routedApi(routes(() => json(200, page([asha, ravi]))));
    const first = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    await first.user.type(await screen.findByRole('combobox', { name: 'Patient' }), 'verma');
    await screen.findByRole('listbox');
    await first.user.tab();
    expect(await screen.findByText('Chosen: Asha Verma (MRN MRN-1001)')).toBeInTheDocument();
    first.unmount();

    const second = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    await second.user.type(await screen.findByRole('combobox', { name: 'Patient' }), 'verma');
    await screen.findByRole('listbox');
    await second.user.keyboard('{ArrowDown}');
    await second.user.tab();
    expect(await screen.findByText('Chosen: Ravi Verma (MRN MRN-2002)')).toBeInTheDocument();
  });

  it('with no result keeps its text, is marked "No record chosen", and the focus has moved on all the same', async () => {
    const api = routedApi(routes(() => json(200, page([]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'zzz');
    await screen.findByText('No active patient matches.');
    await user.tab();

    expect(box).toHaveValue('zzz');
    expect(box).toHaveAttribute('aria-invalid', 'true');
    expect(box).toHaveAccessibleDescription(noRecordChosen);
    expect(screen.getByRole('combobox', { name: 'Payer' })).toHaveFocus();

    // Typing again takes the mark away.
    await user.click(box);
    await user.type(box, 'z');
    expect(box).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText(noRecordChosen)).not.toBeInTheDocument();
  });

  it('before the answer has come sends the search at once and takes the first result when it arrives', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const api = routedApi(
      routes(async () => {
        await held;
        return json(200, page([asha, ravi]));
      }),
    );
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    // Typed and left inside the pause: nothing had been sent yet.
    await user.type(box, 'ver');
    expect(api.sent('GET', patients)).toHaveLength(0);
    await user.tab();

    await waitFor(() => expect(api.sent('GET', patients)).toHaveLength(1), { timeout: searchPause / 2 });
    expect(searchOf(api.sent('GET', patients)[0])).toBe('ver');
    expect(screen.getByRole('combobox', { name: 'Payer' })).toHaveFocus();
    expect(screen.queryByText(/^Chosen:/)).not.toBeInTheDocument();

    release();
    expect(await screen.findByText('Chosen: Asha Verma (MRN MRN-1001)')).toBeInTheDocument();
    expect(api.sent('GET', patients)).toHaveLength(1);
  });

  it('chooses nothing from a late answer when the user came back and typed again', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const api = routedApi(
      routes(async (request) => {
        if (searchOf(request) === 'ver') {
          await held;
        }
        return json(200, page([asha, ravi]));
      }),
    );
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'ver');
    await user.tab();
    await user.click(box);
    await user.type(box, 'm');
    release();

    await screen.findByRole('listbox');
    await rest(200);
    expect(screen.queryByText(/^Chosen:/)).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Patient' })).toHaveValue('verm');
  });

  it('by Shift+Tab does the same, backwards', async () => {
    const api = routedApi(routes(() => json(200, page([asha]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'ash');
    await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' });
    await user.tab({ shift: true });

    expect(await screen.findByText('Chosen: Asha Verma (MRN MRN-1001)')).toBeInTheDocument();
    expect(document.activeElement).not.toBe(document.body);
  });

  it('left by the pointer closes the list and chooses nothing', async () => {
    const api = routedApi(routes(() => json(200, page([asha, ravi]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Patient' });

    await user.type(box, 'verma');
    await screen.findByRole('listbox');
    await user.click(screen.getByRole('heading', { level: 1 }));

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(box).toHaveValue('verma');
    expect(box).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByText(/^Chosen:/)).not.toBeInTheDocument();
  });

  it('by Enter chooses and stays: the focus is on the way to change the record', async () => {
    const api = routedApi(routes(() => json(200, page([asha]))));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByRole('combobox', { name: 'Patient' }), 'ash');
    await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' });
    await user.keyboard('{Enter}');

    const chosen = await screen.findByText('Asha Verma (MRN MRN-1001)', { selector: 'span' });
    expect(within(chosen.parentElement as HTMLElement).getByRole('button', { name: 'Change' })).toHaveFocus();
    expect(api.sent('POST', '/api/v1/bills')).toHaveLength(0);
  });
});

describe('the payer of a payment, where a wrong first result would move money', () => {
  const cashier = sessionFor(['Cashier']);
  const payers = '/api/v1/payers';

  it('is chosen by Tab when one payer alone matches', async () => {
    const api = routedApi({ [`GET ${payers}`]: () => json(200, page([ashasPayer])) });
    const { user } = renderApp('/payments/new', { session: cashier, fetch: api.fetch });

    await user.type(await screen.findByRole('combobox', { name: 'Payer' }), 'ash');
    await screen.findByRole('option', { name: /^Asha Verma/ });
    await user.tab();

    expect(await screen.findByText("Chosen: Asha Verma (Self-pay, a patient's own payer)")).toBeInTheDocument();
    expect(screen.getByLabelText(/^Payment date/)).toHaveFocus();
  });

  it('is not chosen by Tab when several match: the field says so, and the focus still moves on', async () => {
    const api = routedApi({ [`GET ${payers}`]: () => json(200, page([ashasPayer, ravisPayer])) });
    const { user } = renderApp('/payments/new', { session: cashier, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Payer' });

    await user.type(box, 'verma');
    await screen.findByRole('listbox');
    await user.tab();

    expect(screen.queryByText(/^Chosen:/)).not.toBeInTheDocument();
    expect(box).toHaveValue('verma');
    expect(box).toHaveAttribute('aria-invalid', 'true');
    expect(box).toHaveAccessibleDescription(`${noRecordChosen}: 2 match. Choose one from the list.`);
    expect(screen.getByLabelText(/^Payment date/)).toHaveFocus();
    expect(api.sent('POST', '/api/v1/payments')).toHaveLength(0);

    // Enter still chooses the marked one, deliberately.
    await user.click(box);
    await screen.findByRole('listbox');
    await user.keyboard('{ArrowDown}{Enter}');
    expect(await screen.findByText("Chosen: Ravi Verma (Self-pay, a patient's own payer)")).toBeInTheDocument();
  });

  it('holds to the rule for a late answer too', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const api = routedApi({
      [`GET ${payers}`]: async () => {
        await held;
        return json(200, page([ashasPayer, ravisPayer]));
      },
    });
    const { user } = renderApp('/payments/new', { session: cashier, fetch: api.fetch });
    const box = await screen.findByRole('combobox', { name: 'Payer' });

    await user.type(box, 'ver');
    await user.tab();
    release();

    await waitFor(() => expect(box).toHaveAttribute('aria-invalid', 'true'));
    expect(box).toHaveAccessibleDescription(`${noRecordChosen}: 2 match. Choose one from the list.`);
    expect(screen.queryByText(/^Chosen:/)).not.toBeInTheDocument();
  });
});
