import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { page, patient, payer, problem } from '../test/records';

const asha = patient();
const ashasPayer = payer();
const guarantor = payer({
  id: 'b2000000-0000-4000-8000-000000000020',
  name: 'Meera Iyer',
  patientId: null,
  phone: '+91 99000 11111',
});
const clerk = sessionFor(['BillingClerk']);

describe('the list of payers (US-11)', () => {
  it('lists each payer with whom they pay for, and offers a guarantor to those who may change payers', async () => {
    const api = routedApi({ 'GET /api/v1/payers': () => json(200, page([ashasPayer, guarantor])) });
    renderApp('/payers', { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Meera Iyer' })).toHaveAttribute('href', `/payers/${guarantor.id}`);
    expect(screen.getByText('Guarantor')).toBeInTheDocument();
    expect(screen.getByText('A patient (their own payer)')).toBeInTheDocument();
    expect(screen.getAllByText('Self-pay')).toHaveLength(2);
    expect(screen.getByRole('link', { name: 'New guarantor' })).toHaveAttribute('href', '/payers/new');
  });
});

describe('a payer', () => {
  it('links the patient they are the payer of, and shows a reader no action', async () => {
    const api = routedApi({
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
    });
    renderApp(`/payers/${ashasPayer.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Asha Verma (MRN MRN-1001)' })).toHaveAttribute(
      'href',
      `/patients/${asha.id}`,
    );
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Deactivate/ })).not.toBeInTheDocument();
  });

  it('is reactivated with the version shown', async () => {
    const inactive = { ...guarantor, status: 'Inactive' as const };
    const api = routedApi({
      [`GET /api/v1/payers/${guarantor.id}`]: () => json(200, inactive),
      [`POST /api/v1/payers/${guarantor.id}/reactivate`]: () => json(200, { ...guarantor, version: 'AAAAAAAAB+A=' }),
    });
    const { user } = renderApp(`/payers/${guarantor.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByText('An inactive payer receives no new bills', { exact: false })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reactivate payer' }));

    expect(await screen.findByRole('button', { name: 'Deactivate payer' })).toBeInTheDocument();
    expect(api.sent('POST', `/api/v1/payers/${guarantor.id}/reactivate`)[0]?.headers.get('If-Match')).toBe(
      '"AAAAAAAAB9I="',
    );
  });
});

describe('creating a payer (US-11, BR-103, BR-104)', () => {
  it("from the patient's page: a self-pay payer for that patient, named after them", async () => {
    const api = routedApi({
      [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
      'GET /api/v1/payers': () => json(200, page([])),
      'POST /api/v1/payers': () => json(201, ashasPayer),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    });
    const { user, router } = renderApp(`/patients/${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('link', { name: "Create the patient's payer" }));
    expect(await screen.findByRole('heading', { name: 'Payer for Asha Verma' })).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Asha Verma');
    expect(screen.getByText('Asha Verma (MRN MRN-1001)')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create payer' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payers/${ashasPayer.id}`));
    expect(await api.sent('POST', '/api/v1/payers')[0]?.json()).toEqual({
      kind: 'SelfPay',
      patientId: asha.id,
      name: 'Asha Verma',
      phone: null,
      email: null,
    });
  });

  it('from the list: a guarantor, who is no patient', async () => {
    const api = routedApi({
      'POST /api/v1/payers': () => json(201, guarantor),
      [`GET /api/v1/payers/${guarantor.id}`]: () => json(200, guarantor),
    });
    const { user, router } = renderApp('/payers/new', { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'New guarantor' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Name'), 'Meera Iyer');
    await user.type(screen.getByLabelText('Phone'), '+91 99000 11111');
    await user.click(screen.getByRole('button', { name: 'Create payer' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payers/${guarantor.id}`));
    expect(await api.sent('POST', '/api/v1/payers')[0]?.json()).toEqual({
      kind: 'SelfPay',
      patientId: null,
      name: 'Meera Iyer',
      phone: '+91 99000 11111',
      email: null,
    });
  });

  it("shows the API's refusal of a second payer for a patient", async () => {
    const api = routedApi({
      [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
      'POST /api/v1/payers': () =>
        json(409, problem(409, 'patient_already_linked', 'The patient already has a payer.')),
    });
    const { user } = renderApp(`/payers/new?patientId=${asha.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Create payer' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The patient already has a payer.');
  });
});

describe('correcting a payer', () => {
  it('changes the name and contact details only, with the version it started from', async () => {
    const api = routedApi({
      [`GET /api/v1/payers/${guarantor.id}`]: () => json(200, guarantor),
      [`PATCH /api/v1/payers/${guarantor.id}`]: () => json(200, { ...guarantor, email: 'meera@example.org' }),
    });
    const { user, router } = renderApp(`/payers/${guarantor.id}/edit`, { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByLabelText('E-mail address'), 'meera@example.org');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payers/${guarantor.id}`));
    const request = api.sent('PATCH', `/api/v1/payers/${guarantor.id}`)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAAB9I="');
    expect(await request?.json()).toEqual({ name: 'Meera Iyer', phone: '+91 99000 11111', email: 'meera@example.org' });
  });
});
