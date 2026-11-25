import { useQuery } from '@tanstack/react-query';
import { answer } from '../api/call';
import type { ApiClient } from '../api/client';
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
