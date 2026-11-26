import { configure, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { problem } from '../test/records';
import { letterheadImageRefusal, type TenantSettings } from './api';

/**
 * The logo and the signature on the Settings screen (Prompt 10.4c, ADR 0059), with the API faked: an image is sent as
 * the file itself with its type and the version of the settings, shown as it will print, replaced and removed; a file
 * of the wrong type or too large is refused before a request carries it; a refusal of the API is shown as every
 * refusal is; and the forms beside it go on with the version the save answered.
 */
configure({ asyncUtilTimeout: 3000 });

const admin = sessionFor(['TenantAdmin']);
const png = Uint8Array.from(
  atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='),
  (character) => character.charCodeAt(0),
);
const image = () => new Response(png, { status: 200, headers: { 'Content-Type': 'image/png' } });
const settings: TenantSettings = {
  postingApprovalThreshold: 100000,
  maxBackdateDays: 7,
  defaultDueDays: 15,
  prefixes: { bill: 'BIL', creditNote: 'CRN', payment: 'PAY', adjustment: 'ADJ', refund: 'REF' },
  letterhead: {
    name: 'City Hospital',
    legalName: null,
    addressLine1: null,
    addressLine2: null,
    city: null,
    state: null,
    pinCode: null,
    phone: null,
    email: null,
    gstin: null,
    signatory: null,
    signatoryDesignation: null,
    logoSha256: null,
    signatureSha256: null,
  },
  version: 'AAAAAAAAC1E=',
};
const withLogo: TenantSettings = {
  ...settings,
  letterhead: { ...settings.letterhead, logoSha256: 'a'.repeat(64) },
  version: 'AAAAAAAAC1F=',
};
const file = (name: string, type: string, bytes: Uint8Array<ArrayBuffer> = png) => new File([bytes], name, { type });
const card = async (title: string) => {
  const heading = await screen.findByRole('heading', { level: 3, name: title });
  return within(heading.parentElement as HTMLElement);
};

describe('what may be chosen', () => {
  it('is a PNG, JPEG or WebP image within the size of its kind', () => {
    expect(letterheadImageRefusal('logo', { type: 'image/png', size: 256 * 1024 })).toBeNull();
    expect(letterheadImageRefusal('logo', { type: 'image/webp', size: 1 })).toBeNull();
    expect(letterheadImageRefusal('signature', { type: 'image/jpeg', size: 128 * 1024 })).toBeNull();
    expect(letterheadImageRefusal('logo', { type: 'image/png', size: 256 * 1024 + 1 })).toMatch(/at most 256 KB/);
    expect(letterheadImageRefusal('signature', { type: 'image/png', size: 128 * 1024 + 1 })).toMatch(/at most 128 KB/);
    expect(letterheadImageRefusal('logo', { type: 'image/svg+xml', size: 10 })).toMatch(/An SVG is not accepted/);
    expect(letterheadImageRefusal('logo', { type: 'image/gif', size: 10 })).toBe('Choose a PNG, JPEG or WebP image.');
    expect(letterheadImageRefusal('logo', { type: '', size: 10 })).toBe('Choose a PNG, JPEG or WebP image.');
  });
});

describe('the logo and the signature of the letterhead', () => {
  it('sends a chosen logo as the file itself, with its type and the version, and shows it as it prints', async () => {
    let now = settings;
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, now),
      'PUT /api/v1/settings/letterhead/logo': () => {
        now = withLogo;
        return json(200, withLogo);
      },
      'GET /api/v1/settings/letterhead/logo': image,
      'PATCH /api/v1/settings': () => json(200, { ...withLogo, version: 'AAAAAAAAC1G=' }),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });

    const logo = await card('Logo');
    expect(logo.getByText('No logo yet')).toBeInTheDocument();
    expect(logo.queryByRole('button', { name: 'Remove the logo' })).not.toBeInTheDocument();

    await user.upload(logo.getByLabelText('Choose a logo'), file('logo.png', 'image/png'));

    expect(await logo.findByText('Documents printed from now on carry this logo.')).toBeInTheDocument();
    const sent = api.sent('PUT', '/api/v1/settings/letterhead/logo')[0];
    expect(sent?.headers.get('If-Match')).toBe(`"${settings.version}"`);
    expect(sent?.headers.get('Content-Type')).toBe('image/png');
    expect(new Uint8Array(await (sent as Request).arrayBuffer())).toEqual(png);

    // The preview is the stored image, read back from the API.
    const preview = await logo.findByRole('img', { name: 'The logo as it is printed' });
    expect(preview).toHaveAttribute('src', expect.stringMatching(/^data:image\/png;base64,iVBORw0KGgo/));
    expect(logo.getByLabelText('Replace the logo')).toBeInTheDocument();
    expect(logo.getByRole('button', { name: 'Remove the logo' })).toBeInTheDocument();

    // The settings above share the row: their next save sends the version the upload answered.
    await user.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(api.sent('PATCH', '/api/v1/settings')).toHaveLength(1));
    expect(api.sent('PATCH', '/api/v1/settings')[0]?.headers.get('If-Match')).toBe(`"${withLogo.version}"`);
  });

  it('removes the logo with the version, and replaces a signature by choosing another file', async () => {
    const both: TenantSettings = {
      ...withLogo,
      letterhead: { ...withLogo.letterhead, signatureSha256: 'b'.repeat(64) },
    };
    const removed: TenantSettings = {
      ...both,
      letterhead: { ...both.letterhead, logoSha256: null },
      version: 'AAAAAAAAC1H=',
    };
    const replaced: TenantSettings = {
      ...removed,
      letterhead: { ...removed.letterhead, signatureSha256: 'c'.repeat(64) },
      version: 'AAAAAAAAC1I=',
    };
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, both),
      'GET /api/v1/settings/letterhead/logo': image,
      'GET /api/v1/settings/letterhead/signature': image,
      'POST /api/v1/settings/letterhead/logo/remove': () => json(200, removed),
      'PUT /api/v1/settings/letterhead/signature': () => json(200, replaced),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });

    const logo = await card('Logo');
    await logo.findByRole('img', { name: 'The logo as it is printed' });
    await user.click(logo.getByRole('button', { name: 'Remove the logo' }));

    expect(await logo.findByText('Documents printed from now on carry no logo.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/settings/letterhead/logo/remove')[0]?.headers.get('If-Match')).toBe(
      `"${both.version}"`,
    );
    expect(logo.getByText('No logo yet')).toBeInTheDocument();
    expect(logo.queryByRole('img')).not.toBeInTheDocument();

    const signature = await card('Signature');
    await user.upload(signature.getByLabelText('Replace the signature'), file('signature.webp', 'image/webp'));

    expect(await signature.findByText('Documents printed from now on carry this signature.')).toBeInTheDocument();
    const sent = api.sent('PUT', '/api/v1/settings/letterhead/signature')[0];
    expect(sent?.headers.get('Content-Type')).toBe('image/webp');
    expect(sent?.headers.get('If-Match')).toBe(`"${removed.version}"`);
    // The new hash is a new image: it is read again.
    await waitFor(() => expect(api.sent('GET', '/api/v1/settings/letterhead/signature')).toHaveLength(2));
  });

  it('refuses an SVG, another type and a file that is too large before any request, at the field', async () => {
    const api = routedApi({ 'GET /api/v1/settings': () => json(200, settings) });
    renderApp('/administration/settings', { session: admin, fetch: api.fetch });
    const logo = await card('Logo');
    const input = logo.getByLabelText('Choose a logo');
    // The filter of the browser's file dialog is not the check: a file of any type can still be given to the field.
    const choose = (field: HTMLElement, chosen: File) => fireEvent.change(field, { target: { files: [chosen] } });

    choose(input, file('logo.svg', 'image/svg+xml'));
    expect(await logo.findByRole('alert')).toHaveTextContent(
      'An SVG is not accepted. Choose a PNG, JPEG or WebP image.',
    );
    expect(input).toHaveAttribute('aria-invalid', 'true');

    choose(input, file('logo.pdf', 'application/pdf'));
    expect(await logo.findByRole('alert')).toHaveTextContent('Choose a PNG, JPEG or WebP image.');

    choose(input, file('logo.png', 'image/png', new Uint8Array(256 * 1024 + 1)));
    expect(await logo.findByRole('alert')).toHaveTextContent('The file is too large: at most 256 KB.');

    const signature = await card('Signature');
    choose(
      signature.getByLabelText('Choose a signature'),
      file('signature.png', 'image/png', new Uint8Array(128 * 1024 + 1)),
    );
    expect(await signature.findByRole('alert')).toHaveTextContent('The file is too large: at most 128 KB.');

    expect(api.requests.filter((request) => request.method !== 'GET')).toHaveLength(0);
  });

  it('shows what the API refuses, and a version that is no longer the latest, as every refusal is shown', async () => {
    const answers = [
      () =>
        json(
          400,
          problem(400, 'validation_failed', 'One or more fields are not valid.', {
            content: ['The file is not a PNG, JPEG or WebP image. An SVG is not accepted.'],
          }),
        ),
      () => json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
    ];
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, settings),
      'PUT /api/v1/settings/letterhead/logo': () => (answers.shift() ?? answers[0])?.() ?? json(500, {}),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });
    const logo = await card('Logo');

    // A file that is called a PNG and is not one: the browser cannot tell, the API can.
    await user.upload(
      logo.getByLabelText('Choose a logo'),
      file('logo.png', 'image/png', new Uint8Array([60, 115, 118, 103])),
    );
    expect(await logo.findByRole('alert')).toHaveTextContent('The file is not a PNG, JPEG or WebP image.');
    expect(logo.getByText('No logo yet')).toBeInTheDocument();

    await user.upload(logo.getByLabelText('Choose a logo'), file('logo.png', 'image/png'));
    await waitFor(() => expect(logo.getByRole('alert')).toHaveTextContent('The record was changed by someone else.'));
    expect(logo.queryByText(/Documents printed from now on/)).not.toBeInTheDocument();
  });

  it('has a field for the signatory and one for the designation, saved with the letterhead', async () => {
    const saved: TenantSettings = {
      ...settings,
      letterhead: {
        ...settings.letterhead,
        signatory: 'Dr A. Example',
        signatoryDesignation: 'Medical Superintendent',
      },
      version: 'AAAAAAAAC1F=',
    };
    const api = routedApi({
      'GET /api/v1/settings': () => json(200, settings),
      'PUT /api/v1/settings/letterhead': () => json(200, saved),
    });
    const { user } = renderApp('/administration/settings', { session: admin, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^Signatory(?!')/), 'Dr A. Example');
    await user.type(screen.getByLabelText(/^Signatory's designation/), ' Medical Superintendent ');
    await user.click(screen.getByRole('button', { name: 'Save letterhead' }));

    await waitFor(() => expect(api.sent('PUT', '/api/v1/settings/letterhead')).toHaveLength(1));
    expect(await api.sent('PUT', '/api/v1/settings/letterhead')[0]?.json()).toMatchObject({
      signatory: 'Dr A. Example',
      signatoryDesignation: 'Medical Superintendent',
    });
  });
});
