import { useQuery } from '@tanstack/react-query';
import { answer, ApiError } from '../api/call';
import type { ApiClient } from '../api/client';
import { problemFrom, unreachableProblem } from '../api/problem';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';

/** What the printed pages read and record (Prompt 10.4b, ADR 0054). */
export type Letterhead = components['schemas']['LetterheadDto'];
export type PrintRecord = components['schemas']['DocumentPrintDto'];
export type PrintedKind = 'bills' | 'payments' | 'refunds';

/** The hospital's letterhead, which anyone who is signed in may read. */
export function useLetterhead() {
  const client = useApiClient();

  return useQuery({
    queryKey: ['letterhead'],
    queryFn: ({ signal }) => answer(client.GET('/api/v1/settings/letterhead', { signal })),
  });
}

/** Records that the document is being printed, and answers which print it is: the first is the original. */
export function recordPrint(client: ApiClient, kind: PrintedKind, id: string, key: string): Promise<PrintRecord> {
  const options = { params: { path: { id } }, headers: { 'Idempotency-Key': key } };
  switch (kind) {
    case 'bills':
      return answer(client.POST('/api/v1/bills/{id}/prints', options));
    case 'payments':
      return answer(client.POST('/api/v1/payments/{id}/prints', options));
    case 'refunds':
      return answer(client.POST('/api/v1/refunds/{id}/prints', options));
  }
}

/** The lines of the hospital's address that are set, in the order they are printed. */
export function addressLines(letterhead: Letterhead): string[] {
  const place = [letterhead.city, letterhead.state].filter((part) => part !== null && part !== '').join(', ');
  const placeAndPin = [place, letterhead.pinCode ?? ''].filter((part) => part !== '').join(' ');
  const reach = [
    letterhead.phone === null ? '' : `Phone ${letterhead.phone}`,
    letterhead.email === null ? '' : letterhead.email,
  ]
    .filter((part) => part !== '')
    .join(' · ');

  return [letterhead.addressLine1 ?? '', letterhead.addressLine2 ?? '', placeAndPin, reach].filter(
    (line) => line !== '',
  );
}

/** The two images of a letterhead (Prompt 10.4c, ADR 0059). */
export type LetterheadImageKind = 'logo' | 'signature';

const imagePaths = {
  logo: '/api/v1/settings/letterhead/logo',
  signature: '/api/v1/settings/letterhead/signature',
} as const;

/** The three formats the API stores; nothing else is ever shown as an image. */
const shownTypes = ['image/png', 'image/jpeg', 'image/webp'];

/**
 * The bytes of an image as a `data:` URL, which the page's content security policy admits for an image. A type that
 * is not one of the three the API stores is refused: nothing else is ever given to an `<img>`.
 */
export function imageDataUrl(contentType: string | null, bytes: ArrayBufferLike): string {
  const type = (contentType ?? '').split(';')[0]?.trim().toLowerCase() ?? '';
  if (!shownTypes.includes(type)) {
    throw new Error('The answer is not a PNG, JPEG or WebP image.');
  }

  // In pieces: a call with every byte as an argument would overflow the stack for a large image.
  const view = new Uint8Array(bytes);
  let binary = '';
  for (let start = 0; start < view.length; start += 0x8000) {
    binary += String.fromCharCode(...view.subarray(start, start + 0x8000));
  }

  return `data:${type};base64,${btoa(binary)}`;
}

/**
 * Reads the hospital's logo or signature with the session's token, as every request of the app is sent, and answers it
 * as a `data:` URL: an `<img>` that named the API's path could not send the token.
 */
export async function readLetterheadImage(
  client: ApiClient,
  kind: LetterheadImageKind,
  signal?: AbortSignal,
): Promise<string> {
  let result: { data?: unknown; error?: unknown; response: Response };
  try {
    result = await client.GET(imagePaths[kind], {
      parseAs: 'arrayBuffer',
      ...(signal === undefined ? {} : { signal }),
    });
  } catch {
    throw new ApiError(unreachableProblem());
  }

  if (!result.response.ok || result.data === undefined) {
    throw new ApiError(problemFrom(result.response, result.error));
  }

  return imageDataUrl(result.response.headers.get('Content-Type'), result.data as ArrayBuffer);
}

/**
 * The logo or the signature the letterhead names by its hash, as a `data:` URL; idle while the letterhead names none.
 * The hash is part of the key, so a replaced image is read anew and an unchanged one never twice.
 */
export function useLetterheadImage(kind: LetterheadImageKind, sha256: string | null) {
  const client = useApiClient();

  return useQuery({
    queryKey: ['letterhead-image', kind, sha256],
    queryFn: ({ signal }) => readLetterheadImage(client, kind, signal),
    enabled: sha256 !== null,
    staleTime: Infinity,
  });
}

/** Whether the image the letterhead names is still being read: a sheet is not printed until it is here or has failed. */
export function imagePending(sha256: string | null, query: { isPending: boolean }): boolean {
  return sha256 !== null && query.isPending;
}
