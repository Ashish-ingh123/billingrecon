import { useQuery } from '@tanstack/react-query';
import { answer } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { Tone } from '../components/tones';

/**
 * Bringing in a hospital's records and opening balances (Prompt 10.3b, ADR 0053): what the API answers, the limits a
 * file is held to before it is sent, and the requests of the four screens.
 */
export type PartyImport = components['schemas']['PartyImportDto'];
export type OpeningBatchCheck = components['schemas']['OpeningBatchCheckDto'];
export type OpeningBatch = components['schemas']['OpeningBatchDto'];
export type OpeningBatchStatus = components['schemas']['OpeningBatchStatus'];
export type ImportFinding = components['schemas']['ImportFinding'];

/** A CSV file as it was chosen: its name, and its text, which stays in the page's memory and nowhere else. */
export interface ChosenFile {
  name: string;
  text: string;
}

/**
 * The most one request may weigh, in bytes: the proxy in front of the API takes 1 MB (`client_max_body_size`), and a
 * request that is refused there never reaches the API's own message. Held with room to spare.
 */
export const maxRequestBytes = 900_000;

/** The characters the API takes of each file (`ImportPartiesCommand`, `OpeningBatchFiles`). */
export const fileLimits = { patients: 600_000, guarantors: 200_000, opening: 250_000 } as const;

/** What a request body weighs on the wire. */
export function requestBytes(body: unknown): number {
  return new TextEncoder().encode(JSON.stringify(body)).length;
}

/** Why these files cannot be sent as they are, or null when they can. */
export function tooLarge(body: unknown, files: readonly (readonly [ChosenFile | null, number])[]): string | null {
  for (const [file, limit] of files) {
    if (file !== null && file.text.length > limit) {
      return `${file.name} is too large for one import (${file.text.length.toLocaleString('en-IN')} characters; at most ${limit.toLocaleString('en-IN')}). Split it into parts and bring them in one after another.`;
    }
  }

  return requestBytes(body) > maxRequestBytes
    ? 'These files are too large to send together. Split them into parts and bring them in one after another.'
    : null;
}

export const batchStatusLabels: Record<OpeningBatchStatus, string> = {
  PendingApproval: 'Waiting for approval',
  Approved: 'Approved and posted',
  Rejected: 'Rejected',
};

export const batchStatusTones: Record<OpeningBatchStatus, Tone> = {
  PendingApproval: 'warning',
  Approved: 'success',
  Rejected: 'danger',
};

export const importKeys = {
  batches: ['opening-batches'] as const,
  batch: (id: string) => ['opening-batches', id] as const,
};

export interface PartiesBody {
  patientsCsv: string;
  guarantorsCsv: string | null;
  dryRun: boolean;
}

export interface OpeningBody {
  cutOverDate: string;
  billsCsv: string | null;
  advancesCsv: string | null;
  trialBalanceCsv: string | null;
  comment: string | null;
}

export function importParties(client: ApiClient, body: PartiesBody): Promise<PartyImport> {
  return answer(client.POST('/api/v1/imports/parties', { body }));
}

export function checkOpeningBatch(client: ApiClient, body: OpeningBody): Promise<OpeningBatchCheck> {
  return answer(client.POST('/api/v1/opening-batches/dry-run', { body }));
}

export function submitOpeningBatch(client: ApiClient, body: OpeningBody, key: string): Promise<OpeningBatch> {
  return answer(client.POST('/api/v1/opening-batches', { body, headers: { 'Idempotency-Key': key } }));
}

/** The opening batches of the hospital, newest first: the first page, which holds them all for years. */
export function useOpeningBatches(enabled: boolean) {
  const client = useApiClient();

  return useQuery({
    queryKey: importKeys.batches,
    queryFn: ({ signal }) =>
      answer(client.GET('/api/v1/opening-batches', { params: { query: { PageSize: 50 } }, signal })),
    enabled,
  });
}

export function useOpeningBatch(id: string) {
  const client = useApiClient();

  return useQuery({
    queryKey: importKeys.batch(id),
    queryFn: ({ signal }) => answer(client.GET('/api/v1/opening-batches/{id}', { params: { path: { id } }, signal })),
  });
}
