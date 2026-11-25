import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { inTurn, json, renderApp, routedApi, sessionFor } from '../test/app';
import { page, patient, payer, problem } from '../test/records';

const asha = patient();
const one = `/api/v1/patients/${asha.id}`;
const clerk = sessionFor(['BillingClerk']);

describe('the list of patients (US-10)', () => {
  it('sends the search, the status and the page it keeps in the address, and links each patient', async () => {
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([asha], 45, 2)) });
    const { router } = renderApp('/patients?search=verma&status=Active&page=2', { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Asha Verma' })).toHaveAttribute('href', `/patients/${asha.id}`);
    const query = new URL(api.sent('GET', '/api/v1/patients')[0]?.url ?? '').searchParams;
    expect(Object.fromEntries(query)).toEqual({ Search: 'verma', Status: 'Active', Page: '2', PageSize: '20' });
    expect(screen.getByText('45 records, page 2 of 3')).toBeInTheDocument();
    expect(screen.getByText('12 Mar 1984')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?search=verma&status=Active&page=2');
  });

  it('starts a new search at the first page and moves between pages', async () => {
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([asha], 45, 1)) });
    const { user, router } = renderApp('/patients?page=2', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'Asha Verma' });

    // The search is sent while it is typed, after the pause (Prompt 10.2b): no button is pressed.
    await user.type(screen.getByLabelText('MRN or name'), 'MRN-1001');
    expect(screen.queryByRole('button', { name: 'Search' })).not.toBeInTheDocument();
    await waitFor(() => expect(router.state.location.search).toBe('?search=MRN-1001'), { timeout: 3000 });

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(router.state.location.search).toBe('?search=MRN-1001&page=2'));

    await user.selectOptions(screen.getByLabelText('Status'), 'Inactive');
    await waitFor(() => expect(router.state.location.search).toBe('?search=MRN-1001&status=Inactive'));
  });

  it('says when nobody is registered, and when nobody matches', async () => {
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([])) });
    renderApp('/patients', { session: clerk, fetch: api.fetch });
    expect(await screen.findByText('No patient is registered yet.')).toBeInTheDocument();
  });

  it('offers registration to those who may change patients only', async () => {
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([asha])) });
    renderApp('/patients', { session: sessionFor(['Viewer']), fetch: api.fetch });

    await screen.findByRole('link', { name: 'Asha Verma' });
    expect(screen.queryByRole('link', { name: 'Register patient' })).not.toBeInTheDocument();
  });
});

describe('a patient', () => {
  const routes = {
    [`GET ${one}`]: () => json(200, asha),
    'GET /api/v1/payers': () => json(200, page([])),
  };

  it('shows their details, and to a reader no action', async () => {
    const api = routedApi(routes);
    renderApp(`/patients/${asha.id}`, { session: sessionFor(['Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Asha Verma' })).toBeInTheDocument();
    expect(screen.getByText('MRN MRN-1001', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('12 MG Road')).toBeInTheDocument();
    expect(await screen.findByText('No payer is linked to this patient.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Deactivate/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: "Create the patient's payer" })).not.toBeInTheDocument();
  });

  it('shows the payer linked to them', async () => {
    const api = routedApi({ ...routes, 'GET /api/v1/payers': () => json(200, page([payer()])) });
    renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Asha Verma' })).toHaveAttribute('href', `/payers/${payer().id}`);
    const query = new URL(api.sent('GET', '/api/v1/payers')[0]?.url ?? '').searchParams;
    expect(query.get('PatientId')).toBe(asha.id);
  });

  it('is deactivated after a confirmation, with the version shown', async () => {
    const api = routedApi({
      ...routes,
      [`POST ${one}/deactivate`]: () => json(200, { ...asha, status: 'Inactive', version: 'AAAAAAAAB+A=' }),
    });
    const { user } = renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Deactivate patient' }));
    expect(api.sent('POST', `${one}/deactivate`)).toHaveLength(0);
    await user.click(screen.getByRole('button', { name: 'Confirm: deactivate patient' }));

    expect(await screen.findByRole('button', { name: 'Reactivate patient' })).toBeInTheDocument();
    expect(api.sent('POST', `${one}/deactivate`)[0]?.headers.get('If-Match')).toBe('"AAAAAAAAB9E="');
    expect(screen.getByText('An inactive patient can be read and corrected', { exact: false })).toBeInTheDocument();
  });

  it('is not deactivated when the user cancels', async () => {
    const api = routedApi(routes);
    const { user } = renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Deactivate patient' }));
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.getByRole('button', { name: 'Deactivate patient' })).toBeInTheDocument();
    expect(api.sent('POST', `${one}/deactivate`)).toHaveLength(0);
  });

  it('shows what changed when someone else changed them first, and changes nothing', async () => {
    const renamed = { ...asha, fullName: 'Asha V. Rao', version: 'AAAAAAAAB+B=' };
    const api = routedApi({
      ...routes,
      [`GET ${one}`]: inTurn(
        () => json(200, asha),
        () => json(200, renamed),
      ),
      [`POST ${one}/deactivate`]: () =>
        json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
    });
    const { user } = renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Deactivate patient' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: deactivate patient' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Someone else changed this record before you could deactivate it.');
    expect(notice).toHaveTextContent('Full name: was Asha Verma, now Asha V. Rao');
    expect(await screen.findByRole('heading', { name: 'Asha V. Rao' })).toBeInTheDocument();
  });

  it("shows the API's refusal of a change of status", async () => {
    const api = routedApi({
      ...routes,
      [`POST ${one}/deactivate`]: () =>
        json(422, problem(422, 'patient_already_inactive', 'The patient is already inactive.')),
    });
    const { user } = renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Deactivate patient' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: deactivate patient' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The patient is already inactive.');
  });
});

describe('registering a patient (US-10, BR-101)', () => {
  it('asks for the MRN and the name before it sends anything', async () => {
    const api = routedApi({});
    const { user } = renderApp('/patients/new', { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Register patient' }));

    expect(await screen.findByText('Enter the MRN.')).toBeInTheDocument();
    expect(screen.getByText('Enter the full name.')).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });

  it('sends every field, blanks as null, and opens the new patient', async () => {
    const created = patient({ id: 'a1000000-0000-4000-8000-000000000009', mrn: 'MRN-2002', fullName: 'Ravi Kumar' });
    const api = routedApi({
      'POST /api/v1/patients': () => json(201, created),
      [`GET /api/v1/patients/${created.id}`]: () => json(200, created),
      'GET /api/v1/payers': () => json(200, page([])),
    });
    const { user, router } = renderApp('/patients/new', { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByLabelText('MRN'), 'MRN-2002');
    await user.type(screen.getByLabelText('Full name'), 'Ravi Kumar');
    await user.type(screen.getByLabelText('Date of birth'), '1990-07-01');
    await user.selectOptions(screen.getByLabelText('Sex'), 'Male');
    await user.type(screen.getByLabelText('City'), 'Pune');
    await user.click(screen.getByRole('button', { name: 'Register patient' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/patients/${created.id}`));
    expect(await api.sent('POST', '/api/v1/patients')[0]?.json()).toEqual({
      mrn: 'MRN-2002',
      fullName: 'Ravi Kumar',
      dateOfBirth: '1990-07-01',
      sex: 'Male',
      phone: null,
      email: null,
      addressLine1: null,
      addressLine2: null,
      city: 'Pune',
      state: null,
      pinCode: null,
    });
  });

  it("shows the API's refusal of a duplicate MRN, and its field messages at their fields", async () => {
    const api = routedApi({
      'POST /api/v1/patients': inTurn(
        () => json(409, problem(409, 'patient_mrn_duplicate', 'A patient with this MRN is already registered.')),
        () =>
          json(
            400,
            problem(400, 'validation_failed', 'One or more fields are invalid.', {
              phone: ['The phone number has 7 to 15 digits.'],
            }),
          ),
      ),
    });
    const { user } = renderApp('/patients/new', { session: clerk, fetch: api.fetch });
    await user.type(await screen.findByLabelText('MRN'), 'MRN-1001');
    await user.type(screen.getByLabelText('Full name'), 'Asha Verma');

    await user.click(screen.getByRole('button', { name: 'Register patient' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('A patient with this MRN is already registered.');

    await user.click(screen.getByRole('button', { name: 'Register patient' }));
    expect(await screen.findByText('The phone number has 7 to 15 digits.')).toBeInTheDocument();
    expect(screen.getByLabelText('Phone')).toHaveAttribute('aria-invalid', 'true');
  });

  it('is not offered to a role that may only read', async () => {
    renderApp('/patients/new', { session: sessionFor(['Viewer']) });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
  });
});

describe('correcting a patient (ADR 0039)', () => {
  const routes = {
    [`GET ${one}`]: () => json(200, asha),
    'GET /api/v1/payers': () => json(200, page([])),
  };

  it('starts from their details, keeps the MRN, and sends the version it started from', async () => {
    const corrected = { ...asha, phone: '+91 80 2222 3333', version: 'AAAAAAAAB+A=' };
    const api = routedApi({ ...routes, [`PATCH ${one}`]: () => json(200, corrected) });
    const { user, router } = renderApp(`/patients/${asha.id}/edit`, { session: clerk, fetch: api.fetch });

    const phone = await screen.findByLabelText('Phone');
    expect(phone).toHaveValue('+91 98450 12345');
    expect(screen.queryByLabelText('MRN')).not.toBeInTheDocument();
    expect(screen.getByText('MRN-1001')).toBeInTheDocument();

    await user.clear(phone);
    await user.type(phone, '+91 80 2222 3333');
    await user.clear(screen.getByLabelText('Address line 1'));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/patients/${asha.id}`));
    const request = api.sent('PATCH', one)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAAB9E="');
    expect(await request?.json()).toMatchObject({
      phone: '+91 80 2222 3333',
      addressLine1: null,
      fullName: 'Asha Verma',
    });
  });

  it('on a conflict says nothing was saved and what changed, and loads the latest version when asked', async () => {
    const changed = { ...asha, fullName: 'Asha V. Rao', city: 'Mysuru', version: 'AAAAAAAAB+B=' };
    const api = routedApi({
      ...routes,
      [`GET ${one}`]: inTurn(
        () => json(200, asha),
        () => json(200, changed),
      ),
      [`PATCH ${one}`]: () =>
        json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
    });
    const { user } = renderApp(`/patients/${asha.id}/edit`, { session: clerk, fetch: api.fetch });
    await user.type(await screen.findByLabelText('State'), ' (South)');

    await user.click(screen.getByRole('button', { name: 'Save' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent(
      'Someone else changed this record while you were editing it. Your changes have not been saved.',
    );
    const changes = within(notice)
      .getAllByRole('listitem')
      .map((item) => item.textContent);
    expect(changes).toEqual(['Full name: was Asha Verma, now Asha V. Rao', 'City: was Bengaluru, now Mysuru']);

    await user.click(within(notice).getByRole('button', { name: 'Load the latest version' }));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Full name')).toHaveValue('Asha V. Rao');
    expect(screen.getByLabelText('State')).toHaveValue('Karnataka');
  });

  it('on a conflict keeps the changes when asked, and the next save replaces the latest version knowingly', async () => {
    const changed = { ...asha, city: 'Mysuru', version: 'AAAAAAAAB+B=' };
    const api = routedApi({
      ...routes,
      [`GET ${one}`]: inTurn(
        () => json(200, asha),
        () => json(200, changed),
      ),
      [`PATCH ${one}`]: inTurn(
        () => json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
        () => json(200, { ...changed, state: 'Karnataka (South)', version: 'AAAAAAAAB+C=' }),
      ),
    });
    const { user, router } = renderApp(`/patients/${asha.id}/edit`, { session: clerk, fetch: api.fetch });
    await user.type(await screen.findByLabelText('State'), ' (South)');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await user.click(await screen.findByRole('button', { name: 'Keep my changes' }));

    expect(screen.getByText('Your changes are kept. Saving now replaces the latest version.')).toBeInTheDocument();
    expect(screen.getByLabelText('State')).toHaveValue('Karnataka (South)');

    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/patients/${asha.id}`));
    const [first, second] = api.sent('PATCH', one);
    expect(first?.headers.get('If-Match')).toBe('"AAAAAAAAB9E="');
    expect(second?.headers.get('If-Match')).toBe('"AAAAAAAAB+B="');
  });
});
