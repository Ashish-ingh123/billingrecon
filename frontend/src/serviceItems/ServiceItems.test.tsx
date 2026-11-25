import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { page, problem, serviceItem } from '../test/records';

const cbc = serviceItem();
const room = serviceItem({
  id: 'c3000000-0000-4000-8000-000000000030',
  code: 'ROOM/ICU',
  name: 'ICU bed, per day',
  category: 'Rooms',
  defaultUnitPrice: 1250000.5,
  taxRatePercent: 12.5,
});
const clerk = sessionFor(['BillingClerk']);

describe('the service catalogue (BR-201)', () => {
  it('lists each item with its price in Indian grouping and its tax rate', async () => {
    const api = routedApi({ 'GET /api/v1/service-items': () => json(200, page([cbc, room])) });
    renderApp('/service-items', { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'ROOM/ICU' })).toHaveAttribute('href', `/service-items/${room.id}`);
    expect(screen.getByText('12,50,000.50')).toBeInTheDocument();
    expect(screen.getByText('12.5%')).toBeInTheDocument();
    expect(screen.getByText('450.00')).toBeInTheDocument();
    expect(screen.getByText('0%')).toBeInTheDocument();
  });
});

describe('creating a service item', () => {
  async function fill(user: ReturnType<typeof renderApp>['user'], price: string, taxRate: string) {
    await user.type(await screen.findByLabelText('Code'), 'LAB/LFT');
    await user.type(screen.getByLabelText('Name'), 'Liver function test');
    if (price !== '') {
      await user.type(screen.getByLabelText('Default unit price (₹)'), price);
    }

    if (taxRate !== '') {
      await user.type(screen.getByLabelText('Tax rate (%)'), taxRate);
    }

    await user.click(screen.getByRole('button', { name: 'Create service item' }));
  }

  it('asks for both numbers: a forgotten one is never free or untaxed', async () => {
    const api = routedApi({});
    const { user } = renderApp('/service-items/new', { session: clerk, fetch: api.fetch });

    await fill(user, '', '');

    expect(await screen.findByText('Enter the default unit price.')).toBeInTheDocument();
    expect(screen.getByText('Enter the tax rate.')).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });

  it.each([
    ['12.34567', '0', 'Write the default unit price as a number of 0 or more, with at most 4 decimals.'],
    ['-5', '0', 'Write the default unit price as a number of 0 or more, with at most 4 decimals.'],
    ['1,500', '0', 'Write the default unit price as a number of 0 or more, with at most 4 decimals.'],
    ['1234567890123.4567', '0', 'The default unit price has more than 15 digits.'],
    ['100', '18.255', 'Write the tax rate as a number of 0 or more, with at most 2 decimals.'],
    ['100', '100.01', 'The tax rate is at most 100.'],
  ])('refuses a price of %s with a tax rate of %s', async (price, taxRate, message) => {
    const api = routedApi({});
    const { user } = renderApp('/service-items/new', { session: clerk, fetch: api.fetch });

    await fill(user, price, taxRate);

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });

  it('sends the numbers as JSON numbers, exactly as typed, and opens the new item', async () => {
    const created = serviceItem({ id: 'c3000000-0000-4000-8000-000000000040', code: 'LAB/LFT' });
    const api = routedApi({
      'POST /api/v1/service-items': () => json(201, created),
      [`GET /api/v1/service-items/${created.id}`]: () => json(200, created),
    });
    const { user, router } = renderApp('/service-items/new', { session: clerk, fetch: api.fetch });

    await fill(user, '1500.1234', '18');

    await waitFor(() => expect(router.state.location.pathname).toBe(`/service-items/${created.id}`));
    const body = await api.sent('POST', '/api/v1/service-items')[0]?.text();
    expect(body).toBe(
      '{"code":"LAB/LFT","name":"Liver function test","category":null,"defaultUnitPrice":1500.1234,"taxRatePercent":18,"sacCode":null}',
    );
  });

  it("shows the API's refusal of a code that is taken", async () => {
    const api = routedApi({
      'POST /api/v1/service-items': () =>
        json(409, problem(409, 'service_item_code_duplicate', 'A service item with this code exists already.')),
    });
    const { user } = renderApp('/service-items/new', { session: clerk, fetch: api.fetch });

    await fill(user, '100', '0');

    expect(await screen.findByRole('alert')).toHaveTextContent('A service item with this code exists already.');
  });
});

describe('correcting a service item', () => {
  it('starts from its numbers, keeps the code, and sends the version it started from', async () => {
    const api = routedApi({
      [`GET /api/v1/service-items/${room.id}`]: () => json(200, room),
      [`PATCH /api/v1/service-items/${room.id}`]: () => json(200, { ...room, taxRatePercent: 18 }),
    });
    const { user, router } = renderApp(`/service-items/${room.id}/edit`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByLabelText('Default unit price (₹)')).toHaveValue('1250000.5');
    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument();
    expect(screen.getByText('ROOM/ICU')).toBeInTheDocument();
    const rate = screen.getByLabelText('Tax rate (%)');
    await user.clear(rate);
    await user.type(rate, '18');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/service-items/${room.id}`));
    const request = api.sent('PATCH', `/api/v1/service-items/${room.id}`)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAAB9M="');
    expect(await request?.json()).toEqual({
      name: 'ICU bed, per day',
      category: 'Rooms',
      defaultUnitPrice: 1250000.5,
      taxRatePercent: 18,
      // Since Prompt 10.4 (ADR 0054) the SAC code is one of the editable fields: none is sent as null.
      sacCode: null,
    });
  });
});
