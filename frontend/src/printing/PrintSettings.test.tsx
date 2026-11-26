import { cleanup, configure, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { patient, payer, printSettings } from '../test/records';
import { sampleBill, sampleCashier, samplePatient, sampleReceipt, sampleRefund } from './samples';
import { defaultLayout, layoutOf, layoutRules, papersOf, settingsOf, type PrintLayout } from './settings';

/**
 * The print settings on the sheets (Prompt 10.4c, ADR 0059), with the API faked: the default is the sheet as it was;
 * each of the three sheets follows its own settings for paper, logo, stationery, every switch, the footer line and the
 * terms; a bill that carries tax keeps its SAC column and its tax split whatever is set; the cashier's name and the
 * patient's contact are read only when asked for, and Print waits for them; the settings change no figure and not the
 * mark of the original or a duplicate; and the preview of the settings is the sheet that prints.
 */
configure({ asyncUtilTimeout: 3000 });

const cashier = sessionFor(['Cashier', 'BillingClerk']);
const admin = sessionFor(['TenantAdmin']);
const png = Uint8Array.from(
  atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='),
  (character) => character.charCodeAt(0),
);
const image = () => new Response(png, { status: 200, headers: { 'Content-Type': 'image/png' } });
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
  signatoryDesignation: 'Medical Superintendent',
  logoSha256: 'a'.repeat(64) as string | null,
  signatureSha256: 'b'.repeat(64) as string | null,
};
const asha = patient({ ...samplePatient, id: sampleBill.patientId });
const ashasPayer = payer({ id: sampleBill.payerId, name: samplePatient.fullName, patientId: sampleBill.patientId });
const printed = (copyNumber: number) => ({
  copyNumber,
  original: copyNumber === 1,
  printedAtUtc: '2026-11-26T06:30:00+00:00',
});

const documents = [
  { type: 'Bill', path: `/bills/${sampleBill.id}/print`, title: 'Tax Invoice', amount: '₹2,249.48' },
  { type: 'Payment', path: `/payments/${sampleReceipt.payment.id}/print`, title: 'Receipt', amount: '₹2,500.00' },
  { type: 'Refund', path: `/refunds/${sampleRefund.id}/print`, title: 'Refund Voucher', amount: '₹250.52' },
] as const;
type DocumentType = (typeof documents)[number]['type'];

/** The API as a hospital with these settings and this letterhead answers it, for the three sample documents. */
function api(settings: Partial<Record<DocumentType, object>> = {}, head: object = letterhead) {
  const routes: Record<string, Handler> = {
    'GET /api/v1/settings/print': () => json(200, printSettings(settings)),
    'GET /api/v1/settings/letterhead': () => json(200, head),
    'GET /api/v1/settings/letterhead/logo': image,
    'GET /api/v1/settings/letterhead/signature': image,
    [`GET /api/v1/bills/${sampleBill.id}`]: () => json(200, sampleBill),
    [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    [`GET /api/v1/payments/${sampleReceipt.payment.id}/allocations`]: () => json(200, sampleReceipt),
    [`GET /api/v1/payments/${sampleReceipt.payment.id}/recorder`]: () => json(200, { name: sampleCashier }),
    [`GET /api/v1/refunds/${sampleRefund.id}`]: () => json(200, sampleRefund),
    [`GET /api/v1/refunds/${sampleRefund.id}/recorder`]: () => json(200, { name: sampleCashier }),
    [`POST /api/v1/bills/${sampleBill.id}/prints`]: () => json(201, printed(1)),
    [`POST /api/v1/payments/${sampleReceipt.payment.id}/prints`]: () => json(201, printed(2)),
    [`POST /api/v1/refunds/${sampleRefund.id}/prints`]: () => json(201, printed(1)),
  };
  return routedApi(routes);
}

/** The sheet of a document as it is shown once everything it reads is here. */
async function sheetOf(document: (typeof documents)[number], network: ReturnType<typeof api>) {
  const view = renderApp(document.path, { session: cashier, fetch: network.fetch });
  const sheet = await screen.findByRole('article', { name: document.title });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Print' })).toBeEnabled());
  await waitFor(() => expect(sheet).not.toHaveTextContent('…'));
  return { sheet, ...view };
}

let print: ReturnType<typeof vi.fn>;
beforeEach(() => {
  print = vi.fn();
  vi.stubGlobal('print', print);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the rules a layout adds to the page', () => {
  it('give the printer the paper and the margins, and the preview its padding, from numbers alone', () => {
    expect(layoutRules(defaultLayout, 'print')).toBe(
      '@page { size: A4; margin: 14mm 12mm 14mm 12mm; }\n.print-stationery-space { height: 0mm; }',
    );
    const roll: PrintLayout = {
      ...defaultLayout,
      paper: 'Roll80',
      marginTopMm: 5,
      marginRightMm: 6,
      marginBottomMm: 7,
      marginLeftMm: 8,
      prePrintedStationery: true,
      stationeryTopMm: 40,
    };
    expect(layoutRules(roll, 'print')).toBe(
      '@page { size: 80mm 297mm; margin: 5mm 6mm 7mm 8mm; }\n.print-stationery-space { height: 40mm; }',
    );
    expect(layoutRules({ ...roll, paper: 'A5' }, 'print')).toContain('size: A5;');
    // The preview is a picture on a screen: it names no paper for the printer.
    expect(layoutRules(roll, 'preview')).toBe(
      '.print-sheet-preview { padding: 5mm 6mm 7mm 8mm; }\n.print-stationery-space { height: 40mm; }',
    );
  });

  it('never write a measure outside its limits, or anything that is not a whole number', () => {
    const wild = { ...defaultLayout, marginTopMm: 900, marginLeftMm: -3, marginRightMm: 12.6, stationeryTopMm: 500 };
    expect(layoutRules(wild, 'print')).toBe(
      '@page { size: A4; margin: 25mm 13mm 14mm 5mm; }\n.print-stationery-space { height: 80mm; }',
    );
    expect(layoutRules({ ...defaultLayout, marginTopMm: Number.NaN }, 'print')).not.toContain('NaN');
  });

  it('a roll is offered for a receipt and a refund voucher and never for a bill', () => {
    expect(papersOf('Bill')).toEqual(['A4', 'A5']);
    expect(papersOf('Payment')).toEqual(['A4', 'A5', 'Roll80']);
    expect(papersOf('Refund')).toEqual(['A4', 'A5', 'Roll80']);
  });

  it('the defaults of the page are the defaults the API answers a hospital that saved nothing', () => {
    for (const answer of printSettings()) {
      expect(layoutOf(answer as never)).toEqual(defaultLayout);
    }
    expect(settingsOf([], 'Bill')).toEqual({ documentType: 'Bill', layout: defaultLayout, version: 'AAAAAAAAAAA=' });
  });
});

describe.each(documents)('the sheet of a $type', (document) => {
  it('with no settings saved is the sheet as it was: A4, the logo at the left, every part it showed', async () => {
    const { sheet } = await sheetOf(document, api());

    expect(sheet.className).not.toMatch(/paperA5|paperRoll80/);
    expect(sheet.querySelector('header')?.className).toMatch(/logoLeft/);
    expect(sheet.querySelector('header > div')?.className).toMatch(/logoMedium/);
    expect(within(sheet).getByText(/^In words:/)).toBeInTheDocument();
    expect(within(sheet).getByRole('contentinfo')).toHaveTextContent('Dr A. Example');
    expect(sheet).toHaveTextContent(samplePatient.fullName);
    expect(sheet).not.toHaveTextContent(samplePatient.phone);
    expect(sheet).not.toHaveTextContent('Cashier');
    expect(sheet).not.toHaveTextContent('Terms');
    expect(sheet.querySelector('[data-testid="stationery-space"]')).toBeNull();
  });

  it.each(['A4', 'A5', 'Roll80'] as const)('stands as wide as its paper: %s', async (paper) => {
    if (paper === 'Roll80' && document.type === 'Bill') {
      // A bill has no roll: the API refuses it and the form does not offer it.
      return;
    }

    const { sheet } = await sheetOf(document, api({ [document.type]: { paper } }));

    expect(sheet.className).toMatch(new RegExp(`paper${paper}`));
    expect(screen.getByText(/Use the browser's dialog to print on/)).toHaveTextContent(
      paper === 'Roll80' ? 'the 80 mm roll' : paper,
    );
  });

  it.each([
    ['Left', 'Small'],
    ['Centre', 'Medium'],
    ['Right', 'Large'],
  ] as const)('places the logo at the %s, %s', async (logoPosition, logoSize) => {
    const { sheet } = await sheetOf(document, api({ [document.type]: { logoPosition, logoSize } }));

    expect(sheet.querySelector('header')?.className).toMatch(new RegExp(`logo${logoPosition}`));
    expect(sheet.querySelector('header > div')?.className).toMatch(new RegExp(`logo${logoSize}`));
    expect(sheet.querySelector('header img')).not.toBeNull();
  });

  it('on pre-printed stationery prints no head of its own and asks for no logo', async () => {
    const network = api({ [document.type]: { prePrintedStationery: true, stationeryTopMm: 45 } });
    const { sheet } = await sheetOf(document, network);

    expect(sheet.querySelector('header')).toBeNull();
    expect(sheet.querySelector('[data-testid="stationery-space"]')).not.toBeNull();
    expect(sheet).not.toHaveTextContent('City Hospital Private Limited12 Example Road');
    expect(network.sent('GET', '/api/v1/settings/letterhead/logo')).toHaveLength(0);
    // The signature is still its own: the foot is not the paper's.
    expect(within(sheet).getByRole('img', { name: 'Signature of Dr A. Example' })).toBeInTheDocument();
  });

  it('leaves out each part that is switched off, and nothing else', async () => {
    const network = api({
      [document.type]: { showPayer: false, showAmountInWords: false, showSignatureBlock: false },
    });
    const { sheet } = await sheetOf(document, network);

    expect(sheet).not.toHaveTextContent(/Billed to|Received from|Paid to/);
    expect(within(sheet).queryByText(/^In words:/)).not.toBeInTheDocument();
    expect(within(sheet).queryByRole('contentinfo')).not.toBeInTheDocument();
    expect(network.sent('GET', '/api/v1/settings/letterhead/signature')).toHaveLength(0);
    // The figure, the number and the mark of the print are not the settings' to hide.
    expect(sheet).toHaveTextContent(document.amount);
    expect(sheet).toHaveTextContent('Unrecorded copy');
  });

  it('shows the patient’s phone and address, a footer line and the terms when they are set', async () => {
    const { sheet } = await sheetOf(
      document,
      api({
        [document.type]: {
          showPatientContact: true,
          footerLine: 'Open 24 hours',
          terms: 'Refunds within 7 days.\nKeep this sheet.',
        },
      }),
    );

    expect(within(sheet).getByText('Phone').nextElementSibling).toHaveTextContent(samplePatient.phone);
    expect(within(sheet).getByText('Address').nextElementSibling).toHaveTextContent(
      '4 Example Lane, Pune, Maharashtra 411001',
    );
    expect(within(sheet).getByText('Open 24 hours')).toBeInTheDocument();
    expect(within(sheet).getByRole('heading', { level: 3, name: 'Terms' }).nextElementSibling?.textContent).toBe(
      'Refunds within 7 days.\nKeep this sheet.',
    );
  });

  it('prints the same figure, and the same mark of its print, whatever the settings', async () => {
    const plain = await sheetOf(document, api());
    await plain.user.click(screen.getByRole('button', { name: 'Print' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    const markBefore = plain.sheet.querySelector('[aria-live]')?.textContent;
    const figures = (sheet: HTMLElement) =>
      Array.from(sheet.querySelectorAll('dd, td')).map((cell) => cell.textContent);
    const before = figures(plain.sheet);
    cleanup();

    const changed = await sheetOf(
      document,
      api({
        [document.type]: {
          paper: 'A5',
          marginTopMm: 25,
          logoPosition: 'Centre',
          logoSize: 'Large',
          footerLine: 'Open 24 hours',
          terms: 'Terms apply.',
        },
      }),
    );
    await changed.user.click(screen.getByRole('button', { name: 'Print' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(2));

    expect(figures(changed.sheet)).toEqual(before);
    expect(changed.sheet.querySelector('[aria-live]')?.textContent).toBe(markBefore);
    expect(changed.sheet.querySelector('[style]')).toBeNull();
    expect(changed.sheet.getAttribute('style')).toBeNull();
  });
});

describe('what a bill must show', () => {
  it('stays on a bill that carries tax, whatever its settings say', async () => {
    // A row the API would refuse, as if it had been written some other way.
    const { sheet } = await sheetOf(documents[0], api({ Bill: { showSacColumn: false, showTaxSplit: false } }));

    expect(within(sheet).getByRole('columnheader', { name: 'SAC' })).toBeInTheDocument();
    expect(within(sheet).getByRole('columnheader', { name: 'CGST' })).toBeInTheDocument();
    expect(within(sheet).getByRole('columnheader', { name: 'SGST' })).toBeInTheDocument();
    expect(within(sheet).getByText('999311')).toBeInTheDocument();
    expect(within(sheet).getByText('CGST', { selector: 'dt' }).nextElementSibling).toHaveTextContent('₹24.99');
  });
});

describe('the cashier’s name', () => {
  it.each(documents.slice(1))('is read and printed on a $type only when its settings ask', async (document) => {
    const without = api();
    const plain = await sheetOf(document, without);
    expect(plain.sheet).not.toHaveTextContent(sampleCashier);
    expect(without.requests.some((request) => request.url.endsWith('/recorder'))).toBe(false);
    cleanup();

    const withName = api({ [document.type]: { showCashierName: true } });
    const { sheet } = await sheetOf(document, withName);
    expect(within(sheet).getByText('Cashier').nextElementSibling).toHaveTextContent(sampleCashier);
    expect(withName.requests.filter((request) => request.url.endsWith('/recorder'))).toHaveLength(1);
  });

  it('holds Print back until it is here, and is left out where no user recorded the document', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const network = api({ Payment: { showCashierName: true } });
    const routes = network.fetch;
    const fetch = async (request: Request) => {
      if (request.url.endsWith('/recorder')) {
        await held;
        return json(200, { name: null });
      }
      return routes(request);
    };
    renderApp(documents[1].path, { session: cashier, fetch });

    const sheet = await screen.findByRole('article', { name: 'Receipt' });
    await waitFor(() => expect(sheet).toHaveTextContent(samplePatient.fullName));
    expect(screen.getByRole('button', { name: 'Preparing…' })).toBeDisabled();

    release();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Print' })).toBeEnabled());
    expect(sheet).not.toHaveTextContent('Cashier');
  });
});

describe('the preview of the settings', () => {
  const layouts: Record<DocumentType, object> = {
    Bill: { paper: 'A5', logoPosition: 'Right', logoSize: 'Small', showPatientContact: true, footerLine: 'Thank you' },
    Payment: { paper: 'Roll80', logoPosition: 'Centre', showCashierName: true, showPatientContact: true, terms: 'T.' },
    Refund: { prePrintedStationery: true, stationeryTopMm: 30, showCashierName: true, showAmountInWords: false },
  };
  const choices: Record<DocumentType, string> = { Bill: 'Bill', Payment: 'Receipt', Refund: 'Refund voucher' };

  it.each(documents)('of a $type is the sheet that prints, part for part', async (document) => {
    const { sheet } = await sheetOf(document, api(layouts));
    // What differs is what must: the mark of the print, which a preview has none of.
    const printedSheet = sheet.innerHTML.replace('Unrecorded copy', 'Preview');
    const printedClasses = sheet.className;
    cleanup();

    const { user } = renderApp('/administration/print', { session: admin, fetch: api(layouts).fetch });
    await user.click(await screen.findByRole('radio', { name: choices[document.type] }));
    const preview = await screen.findByRole('article', { name: document.title });
    await waitFor(() => expect(preview.querySelectorAll('img')).toHaveLength(sheet.querySelectorAll('img').length));

    expect(preview.innerHTML).toBe(printedSheet);
    // The same paper and nothing else of the layout on the frame; the preview adds only that it is one.
    expect(preview.className.replace(/\s*\S*preview\S*/g, '')).toBe(printedClasses);
    expect(preview.className).toContain('print-sheet-preview');
  });
});
