import { configure, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { bill, billLine, patient, payer, payment, printSettings, problem, refund } from '../test/records';
import { imageDataUrl } from './api';

/**
 * The logo and the signature on the printed documents (Prompt 10.4c, ADR 0059), with the API faked: the hospital's logo
 * stands at the head of the three sheets in place of the product's mark, the signature with the signatory's name and
 * designation at their foot; the images are read with the session's token and shown from their own bytes; Print waits
 * for them and is not held back by one that fails; a duplicate carries the signature as the original does; and a
 * letterhead that names no image prints as it did.
 */
configure({ asyncUtilTimeout: 3000 });

const cashier = sessionFor(['Cashier', 'BillingClerk']);
const png = Uint8Array.from(
  atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='),
  (character) => character.charCodeAt(0),
);
const image = (type: string) => () => new Response(png, { status: 200, headers: { 'Content-Type': type } });
const plain = {
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
  logoSha256: null as string | null,
  signatureSha256: null as string | null,
};
const withImages = { ...plain, logoSha256: 'a'.repeat(64), signatureSha256: 'b'.repeat(64) };
const posted = bill({
  status: 'Posted',
  number: 'BIL/26-27/000123',
  fiscalYear: '2026-27',
  lines: [billLine({ sacCode: '999311' })],
});
const asha = patient();
const ashasPayer = payer();
const records: Record<string, Handler> = {
  'GET /api/v1/settings/print': () => json(200, printSettings()),
  [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
  [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
  [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
};
const printed = (copyNumber: number) => ({
  copyNumber,
  original: copyNumber === 1,
  printedAtUtc: '2026-11-26T06:30:00+00:00',
});

let print: ReturnType<typeof vi.fn>;
beforeEach(() => {
  print = vi.fn();
  vi.stubGlobal('print', print);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the bytes of an image', () => {
  it('become a data URL of their own type, and nothing but the three formats is ever shown', () => {
    expect(imageDataUrl('image/png', png.buffer)).toMatch(/^data:image\/png;base64,iVBORw0KGgo/);
    expect(imageDataUrl('IMAGE/WebP; charset=binary', png.buffer)).toMatch(/^data:image\/webp;base64,/);
    expect(() => imageDataUrl('image/svg+xml', png.buffer)).toThrow();
    expect(() => imageDataUrl('text/html', png.buffer)).toThrow();
    expect(() => imageDataUrl(null, png.buffer)).toThrow();
  });

  it('are read whole for an image as large as a logo may be', () => {
    const large = new Uint8Array(256 * 1024).fill(65);

    const url = imageDataUrl('image/jpeg', large.buffer);

    expect(atob(url.slice('data:image/jpeg;base64,'.length))).toHaveLength(256 * 1024);
  });
});

describe('a sheet of a hospital with a logo and a signature', () => {
  const api = () =>
    routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, withImages),
      'GET /api/v1/settings/letterhead/logo': image('image/png'),
      'GET /api/v1/settings/letterhead/signature': image('image/webp'),
      [`POST /api/v1/bills/${posted.id}/prints`]: () => json(201, printed(1)),
    });

  it('carries the logo at its head and the signature, the name and the designation at its foot', async () => {
    const network = api();
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    const sheet = await screen.findByRole('article', { name: 'Tax Invoice' });
    const signature = await within(sheet).findByRole('img', { name: 'Signature of Dr A. Example' });
    expect(signature).toHaveAttribute('src', expect.stringMatching(/^data:image\/webp;base64,/));

    // The logo says nothing a screen reader needs beside the hospital's name; it is an image all the same.
    const logo = sheet.querySelector('header img');
    expect(logo).toHaveAttribute('src', expect.stringMatching(/^data:image\/png;base64,iVBORw0KGgo/));
    expect(logo).toHaveAttribute('alt', '');

    const foot = within(sheet).getByRole('contentinfo');
    expect(within(foot).getByText('Authorised signatory')).toBeInTheDocument();
    expect(within(foot).getByText('Dr A. Example')).toBeInTheDocument();
    expect(within(foot).getByText('Medical Superintendent')).toBeInTheDocument();
    expect(within(foot).getByText('For City Hospital Private Limited')).toBeInTheDocument();

    // Each image was asked of the API once, with the token every request of the app carries.
    for (const path of ['/api/v1/settings/letterhead/logo', '/api/v1/settings/letterhead/signature']) {
      const [request, ...more] = network.sent('GET', path);
      expect(more).toHaveLength(0);
      expect(request?.headers.get('Authorization')).toMatch(/^Bearer /);
    }
  });

  it('keeps a place of a fixed size for each image before it arrives', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const late = () => held.then(() => image('image/png')());
    const network = routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, withImages),
      'GET /api/v1/settings/letterhead/logo': late,
      'GET /api/v1/settings/letterhead/signature': late,
    });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    const sheet = await screen.findByRole('article', { name: 'Tax Invoice' });
    const boxes = () => [
      sheet.querySelector('header > div'),
      within(sheet).getByRole('contentinfo').querySelector('div'),
    ];
    const before = boxes().map((box) => box?.className);
    expect(sheet.querySelectorAll('img')).toHaveLength(0);
    expect(before.every((name) => typeof name === 'string' && name !== '')).toBe(true);

    // Print is not offered while the images are on their way.
    expect(screen.getByRole('button', { name: 'Preparing…' })).toBeDisabled();

    release();
    await waitFor(() => expect(sheet.querySelectorAll('img')).toHaveLength(2));
    expect(boxes().map((box) => box?.className)).toEqual(before);
    expect(screen.getByRole('button', { name: 'Print' })).toBeEnabled();
  });

  it('prints only once the images are here, and a duplicate carries the signature as the original does', async () => {
    const answers = [printed(1), printed(2)];
    const network = routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, withImages),
      'GET /api/v1/settings/letterhead/logo': image('image/png'),
      'GET /api/v1/settings/letterhead/signature': image('image/png'),
      [`POST /api/v1/bills/${posted.id}/prints`]: () => json(201, answers.shift()),
    });
    const { user } = renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    await user.click(await screen.findByRole('button', { name: 'Print' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    expect(screen.getByText('Original')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Signature of Dr A. Example' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Print again' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(2));
    expect(screen.getByText('Duplicate (copy 2)')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Signature of Dr A. Example' })).toBeInTheDocument();
  });

  it('is not held back by an image that cannot be read: the sheet prints without it', async () => {
    const network = routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, withImages),
      'GET /api/v1/settings/letterhead/logo': () =>
        json(404, problem(404, 'letterhead_image_not_found', 'The letterhead has no such image.')),
      'GET /api/v1/settings/letterhead/signature': () =>
        new Response('<svg/>', { status: 200, headers: { 'Content-Type': 'image/svg+xml' } }),
      [`POST /api/v1/bills/${posted.id}/prints`]: () => json(201, printed(1)),
    });
    const { user } = renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    await user.click(await screen.findByRole('button', { name: 'Print' }));

    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    const sheet = screen.getByRole('article', { name: 'Tax Invoice' });
    expect(sheet.querySelectorAll('img')).toHaveLength(0);
    expect(within(sheet).getByText('Dr A. Example')).toBeInTheDocument();
  });

  it('is the same on a receipt and on a refund voucher', async () => {
    const paid = payment();
    const repaid = refund({ status: 'Posted', number: 'REF/26-27/000004', fiscalYear: '2026-27' });
    const images: Record<string, Handler> = {
      'GET /api/v1/settings/print': () => json(200, printSettings()),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      'GET /api/v1/settings/letterhead': () => json(200, withImages),
      'GET /api/v1/settings/letterhead/logo': image('image/jpeg'),
      'GET /api/v1/settings/letterhead/signature': image('image/png'),
    };

    const receipt = renderApp(`/payments/${paid.id}/print`, {
      session: cashier,
      fetch: routedApi({
        ...images,
        [`GET /api/v1/payments/${paid.id}/allocations`]: () => json(200, { payment: paid, allocations: [] }),
      }).fetch,
    });
    let sheet = await screen.findByRole('article');
    expect(await within(sheet).findByRole('img', { name: 'Signature of Dr A. Example' })).toBeInTheDocument();
    expect(sheet.querySelector('header img')).toHaveAttribute(
      'src',
      expect.stringMatching(/^data:image\/jpeg;base64,/),
    );
    expect(within(sheet).getByText('Medical Superintendent')).toBeInTheDocument();
    receipt.unmount();

    renderApp(`/refunds/${repaid.id}/print`, {
      session: cashier,
      fetch: routedApi({ ...images, [`GET /api/v1/refunds/${repaid.id}`]: () => json(200, repaid) }).fetch,
    });
    sheet = await screen.findByRole('article');
    expect(await within(sheet).findByRole('img', { name: 'Signature of Dr A. Example' })).toBeInTheDocument();
    expect(sheet.querySelector('header img')).not.toBeNull();
  });
});

describe('a sheet of a hospital with no image', () => {
  it('asks for none, shows the mark and the line it showed before, and prints at once', async () => {
    const network = routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, { ...plain, signatoryDesignation: null }),
      [`POST /api/v1/bills/${posted.id}/prints`]: () => json(201, printed(1)),
    });
    const { user } = renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    const sheet = await screen.findByRole('article', { name: 'Tax Invoice' });
    expect(sheet.querySelectorAll('img')).toHaveLength(0);
    expect(sheet.querySelector('header svg')).not.toBeNull();
    const foot = within(sheet).getByRole('contentinfo');
    expect(foot).toHaveTextContent(/^Dr A\. ExampleFor City Hospital Private Limited$/);

    await user.click(screen.getByRole('button', { name: 'Print' }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    expect(network.requests.some((request) => new URL(request.url).pathname.includes('/letterhead/'))).toBe(false);
  });

  it('says Authorised signatory where no signatory is named, as before', async () => {
    const network = routedApi({
      ...records,
      'GET /api/v1/settings/letterhead': () => json(200, { ...plain, signatory: null, signatoryDesignation: null }),
    });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: network.fetch });

    const sheet = await screen.findByRole('article', { name: 'Tax Invoice' });
    expect(within(sheet).getByRole('contentinfo')).toHaveTextContent(
      /^Authorised signatoryFor City Hospital Private Limited$/,
    );
  });
});
