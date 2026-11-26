import { configure, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { printSettings, problem } from '../test/records';

/**
 * The Print tab of Users and settings (Prompt 10.4c, ADR 0059), with the API faked: a form for each kind of document
 * beside a sample of it that follows the form before it is saved; a save with every part and the version read, the
 * version of none for a first save; what a bill may never switch off shown locked with the reason; a measure outside
 * its limits refused at its field before anything is sent; a refusal and a conflict of the API shown as every one is;
 * and the tab is the administrator's alone.
 */
configure({ asyncUtilTimeout: 3000 });

const admin = sessionFor(['TenantAdmin']);
const letterhead = {
  name: 'City Hospital',
  legalName: 'City Hospital Private Limited',
  addressLine1: '12 Example Road',
  addressLine2: null,
  city: 'Pune',
  state: 'Maharashtra',
  pinCode: '411001',
  phone: null,
  email: null,
  gstin: '27AAAAA0000A1Z5',
  signatory: 'Dr A. Example',
  signatoryDesignation: null,
  logoSha256: null,
  signatureSha256: null,
};
const base = {
  'GET /api/v1/settings/print': () => json(200, printSettings()),
  'GET /api/v1/settings/letterhead': () => json(200, letterhead),
};
const defaults = printSettings();

describe('the print settings of the hospital', () => {
  it('shows the bill first, as it prints today, with the two parts a tax invoice must show locked', async () => {
    renderApp('/administration/print', { session: admin, fetch: routedApi(base).fetch });

    const form = await screen.findByRole('form', { name: 'How a bill is printed' });
    expect(screen.getByRole('radio', { name: 'Bill' })).toBeChecked();
    expect(within(form).getByLabelText('Paper size')).toHaveValue('A4');
    expect(within(form).queryByRole('option', { name: 'Roll, 80 mm' })).not.toBeInTheDocument();
    expect(within(form).getByLabelText(/^Top margin/)).toHaveValue('14');
    expect(within(form).getByLabelText(/^Left margin/)).toHaveValue('12');

    for (const name of ['The SAC column', 'The tax as CGST and SGST']) {
      const locked = within(form).getByRole('checkbox', { name: new RegExp(`^${name}`) });
      expect(locked).toBeChecked();
      expect(locked).toBeDisabled();
      expect(locked).toHaveAccessibleDescription('A tax invoice must show this: it cannot be switched off for a bill.');
    }
    // A bill has no cashier to name.
    expect(within(form).queryByRole('checkbox', { name: /cashier/ })).not.toBeInTheDocument();

    // The sample is a tax invoice on the hospital's own letterhead, as a print of today.
    const preview = await screen.findByRole('article', { name: 'Tax Invoice' });
    expect(within(preview).getByText('City Hospital Private Limited')).toBeInTheDocument();
    expect(within(preview).getByText('Preview')).toBeInTheDocument();
    expect(within(preview).getByRole('columnheader', { name: 'SAC' })).toBeInTheDocument();
    expect(within(preview).getByText(/^In words:/)).toBeInTheDocument();
  });

  it('follows the form before it is saved: the paper, the logo, a switch, the footer line and the terms', async () => {
    const api = routedApi(base);
    const { user } = renderApp('/administration/print', { session: admin, fetch: api.fetch });
    const form = await screen.findByRole('form', { name: 'How a bill is printed' });
    const preview = await screen.findByRole('article', { name: 'Tax Invoice' });
    expect(preview.className).not.toMatch(/paperA5/);

    await user.selectOptions(within(form).getByLabelText('Paper size'), 'A5');
    await user.selectOptions(within(form).getByLabelText('Logo'), 'Centre');
    await user.click(within(form).getByRole('checkbox', { name: 'The amount in words' }));
    await user.click(within(form).getByRole('checkbox', { name: 'The patient’s phone and address' }));
    await user.type(within(form).getByLabelText('Footer line'), 'Open 24 hours');
    await user.type(within(form).getByLabelText('Terms'), 'Pay within 15 days.');

    await waitFor(() => expect(preview.className).toMatch(/paperA5/));
    expect(preview.querySelector('header')?.className).toMatch(/logoCentre/);
    expect(within(preview).queryByText(/^In words:/)).not.toBeInTheDocument();
    expect(within(preview).getByText('Phone')).toBeInTheDocument();
    expect(within(preview).getByText('Open 24 hours')).toBeInTheDocument();
    expect(within(preview).getByText('Pay within 15 days.')).toBeInTheDocument();
    expect(screen.getByText(/A sample bill as it will print, on A5/)).toBeInTheDocument();

    // Nothing was sent: the preview is the form's, not the API's.
    expect(api.requests.filter((request) => request.method !== 'GET')).toHaveLength(0);
  });

  it('saves a receipt on a roll with every part, the version of none for the first save, then its own', async () => {
    const saved = {
      ...defaults[1],
      paper: 'Roll80',
      marginLeftMm: 5,
      marginRightMm: 5,
      showCashierName: true,
      version: 'AAAAAAAAD0E=',
    };
    const again = { ...saved, footerLine: 'Thank you', version: 'AAAAAAAAD0F=' };
    const answers = [saved, again];
    const api = routedApi({
      ...base,
      'PUT /api/v1/settings/print/Payment': () => json(200, answers.shift()),
    });
    const { user } = renderApp('/administration/print', { session: admin, fetch: api.fetch });

    await user.click(await screen.findByRole('radio', { name: 'Receipt' }));
    const form = await screen.findByRole('form', { name: 'How a receipt is printed' });
    await user.selectOptions(within(form).getByLabelText('Paper size'), 'Roll80');
    for (const side of [/^Left margin/, /^Right margin/]) {
      await user.clear(within(form).getByLabelText(side));
      await user.type(within(form).getByLabelText(side), '5');
    }
    await user.click(within(form).getByRole('checkbox', { name: 'The cashier’s name' }));
    await user.click(within(form).getByRole('button', { name: 'Save how a receipt is printed' }));

    expect(await screen.findByText('Every receipt printed from now on is laid out this way.')).toBeInTheDocument();
    const [first] = api.sent('PUT', '/api/v1/settings/print/Payment');
    expect(first?.headers.get('If-Match')).toBe('"AAAAAAAAAAA="');
    expect(await first?.json()).toEqual({
      paper: 'Roll80',
      marginTopMm: 14,
      marginRightMm: 5,
      marginBottomMm: 14,
      marginLeftMm: 5,
      logoPosition: 'Left',
      logoSize: 'Medium',
      prePrintedStationery: false,
      stationeryTopMm: 0,
      showPatientContact: false,
      showPayer: true,
      showSacColumn: true,
      showTaxSplit: true,
      showAmountInWords: true,
      showSignatureBlock: true,
      showCashierName: true,
      footerLine: null,
      terms: null,
    });

    // The next save of the same kind sends the version the first one answered.
    const next = await screen.findByRole('form', { name: 'How a receipt is printed' });
    await user.type(within(next).getByLabelText('Footer line'), 'Thank you');
    await user.click(within(next).getByRole('button', { name: 'Save how a receipt is printed' }));
    await waitFor(() => expect(api.sent('PUT', '/api/v1/settings/print/Payment')).toHaveLength(2));
    expect(api.sent('PUT', '/api/v1/settings/print/Payment')[1]?.headers.get('If-Match')).toBe('"AAAAAAAAD0E="');

    // The bill was not touched: its form still holds the version of none.
    await user.click(screen.getByRole('radio', { name: 'Bill' }));
    expect(
      within(await screen.findByRole('form', { name: 'How a bill is printed' })).getByLabelText('Paper size'),
    ).toHaveValue('A4');
  });

  it('refuses a margin and a stationery space outside their limits at the field, and sends nothing', async () => {
    const api = routedApi(base);
    const { user } = renderApp('/administration/print', { session: admin, fetch: api.fetch });
    await user.click(await screen.findByRole('radio', { name: 'Refund voucher' }));
    const form = await screen.findByRole('form', { name: 'How a refund voucher is printed' });
    const preview = await screen.findByRole('article', { name: 'Refund Voucher' });

    const top = within(form).getByLabelText(/^Top margin/);
    await user.clear(top);
    await user.type(top, '40');
    await user.click(within(form).getByRole('checkbox', { name: /^Pre-printed stationery/ }));
    const space = await within(form).findByLabelText(/^Space for the paper's own head/);
    await user.clear(space);
    await user.type(space, '95');
    await user.click(within(form).getByRole('button', { name: 'Save how a refund voucher is printed' }));

    expect(await within(form).findByText('A margin is between 5 and 25 mm.')).toBeInTheDocument();
    expect(within(form).getByText('The space is between 0 and 80 mm.')).toBeInTheDocument();
    expect(top).toHaveAttribute('aria-invalid', 'true');
    expect(api.requests.filter((request) => request.method !== 'GET')).toHaveLength(0);

    // The stationery takes the head's place in the preview at once; the logo's choices leave the form.
    expect(preview.querySelector('header')).toBeNull();
    expect(preview.querySelector('[data-testid="stationery-space"]')).not.toBeNull();
    expect(within(form).queryByLabelText('Logo')).not.toBeInTheDocument();
  });

  it('shows what the API refuses at its field, and a change by someone else as a conflict to decide', async () => {
    const answers = [
      () =>
        json(
          400,
          problem(400, 'validation_failed', 'One or more fields are not valid.', {
            footerLine: ['The footer line has at most 200 characters.'],
          }),
        ),
      () => json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
    ];
    let reads = 0;
    const api = routedApi({
      ...base,
      'GET /api/v1/settings/print': () => {
        reads += 1;
        // After the conflict the page reads again and finds another administrator's A5.
        return json(
          200,
          reads === 1 ? printSettings() : printSettings({ Bill: { paper: 'A5', version: 'AAAAAAAAD0G=' } }),
        );
      },
      'PUT /api/v1/settings/print/Bill': () => (answers.shift() ?? answers[0])?.() ?? json(500, {}),
    });
    const { user } = renderApp('/administration/print', { session: admin, fetch: api.fetch });
    const form = await screen.findByRole('form', { name: 'How a bill is printed' });
    await user.type(within(form).getByLabelText('Footer line'), 'Open');
    await user.click(within(form).getByRole('button', { name: 'Save how a bill is printed' }));

    expect(await within(form).findByText('The footer line has at most 200 characters.')).toBeInTheDocument();
    expect(within(form).getByLabelText('Footer line')).toHaveAttribute('aria-invalid', 'true');

    await user.click(within(form).getByRole('button', { name: 'Save how a bill is printed' }));
    expect(await within(form).findByRole('button', { name: 'Load the latest version' })).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Keep my changes' })).toBeInTheDocument();
    expect(form).toHaveTextContent('Paper');
    expect(form).toHaveTextContent('A5');
  });

  it('is a tab of Users and settings, and the administrator’s alone', async () => {
    const first = renderApp('/administration/settings', {
      session: admin,
      fetch: routedApi({ ...base, 'GET /api/v1/settings': () => json(404, problem(404, 'not_found', 'Not here.')) })
        .fetch,
    });
    expect(await screen.findByRole('link', { name: 'Print' })).toHaveAttribute('href', '/administration/print');
    first.unmount();

    renderApp('/administration/print', { session: sessionFor(['Supervisor']), fetch: routedApi({}).fetch });
    expect(await screen.findByRole('heading', { level: 1 })).not.toHaveTextContent('Users and settings');
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });
});
