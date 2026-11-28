import { keepPreviousData, useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import { pageSize } from '../components/listParams';
import type { Tone } from '../components/tones';

/**
 * R2 Reconciliation (Prompt 13.1c, ADR 0065; `docs/api.md` section 8.22): the hospital's settlement channels, the dry
 * run and the import of a bank's statement, and the lists of imports and lines.
 */
export type SettlementChannel = components['schemas']['SettlementChannelDto'];
export type ChannelKind = SettlementChannel['kind'];
export type ColumnMapping = components['schemas']['ColumnMappingDto'];
export type AmountLayout = ColumnMapping['amountLayout'];
export type DecimalStyle = ColumnMapping['decimalStyle'];
export type StatementDryRun = components['schemas']['StatementDryRunDto'];
export type StatementImport = components['schemas']['StatementImportDto'];
export type StatementLine = components['schemas']['StatementLineDto'];
export type LineStatus = StatementLine['status'];
export type Direction = StatementLine['direction'];

/** The date formats a mapping may name, as the API lists them (`ColumnMapping.DateFormats`). */
export const dateFormats = [
  'dd/MM/yyyy',
  'dd-MM-yyyy',
  'dd.MM.yyyy',
  'dd/MM/yy',
  'dd-MM-yy',
  'dd-MMM-yyyy',
  'dd-MMM-yy',
  'dd MMM yyyy',
  'yyyy-MM-dd',
] as const;

/** The most characters a statement may hold: the API's `StatementReader.MaxFileLength`, about 1 MB (BR-1003). */
export const maxStatementLength = 900_000;

export const lineStatuses: readonly LineStatus[] = ['Unmatched', 'Matched', 'Excluded'];
export const directions: readonly Direction[] = ['In', 'Out'];

export const lineStatusLabels: Record<LineStatus, { label: string; tone: Tone }> = {
  Unmatched: { label: 'Unmatched', tone: 'warning' },
  Matched: { label: 'Matched', tone: 'success' },
  Excluded: { label: 'Excluded', tone: 'muted' },
};

export const directionLabels: Record<Direction, string> = { In: 'Money in', Out: 'Money out' };

export const channelKindLabels: Record<ChannelKind, string> = { Bank: 'Bank account', Gateway: 'Payment gateway' };

/** A mapping to start from: a bank that writes a debit and a credit column under a header on its first line. */
export const emptyMapping: ColumnMapping = {
  headerRow: 1,
  dateColumn: '',
  dateFormat: 'dd/MM/yyyy',
  valueDateColumn: null,
  descriptionColumn: '',
  referenceColumn: null,
  amountLayout: 'TwoColumns',
  amountColumn: null,
  debitColumn: '',
  creditColumn: '',
  balanceColumn: null,
  decimalStyle: 'Point',
};

export const reconciliationKeys = {
  channels: ['settlement-channels'] as const,
  channel: (id: string) => ['settlement-channels', id] as const,
  imports: (channelId: string, page: number) => ['statement-imports', channelId, page] as const,
  lines: (params: LineListParams) => ['statement-lines', params] as const,
};

/** After an import: the imports and the lines. */
export function statementsChanged(queryClient: QueryClient): void {
  for (const key of [['statement-imports'], ['statement-lines']]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

/** Every channel of the hospital, by name: a hospital has a handful, so the first page of 100 holds them all. */
export function useChannels() {
  const client = useApiClient();
  return useQuery({
    queryKey: reconciliationKeys.channels,
    queryFn: async ({ signal }) =>
      (await answer(client.GET('/api/v1/settlement-channels', { signal, params: { query: { PageSize: 100 } } }))).items,
  });
}

export function useChannel(id: string) {
  const client = useApiClient();
  return useQuery({
    queryKey: reconciliationKeys.channel(id),
    staleTime: 0,
    queryFn: ({ signal }) =>
      answer(client.GET('/api/v1/settlement-channels/{id}', { signal, params: { path: { id } } })),
  });
}

export interface ChannelBody {
  name: string;
  accountLabel: string | null;
  dateWindowDays: number;
  bankChannelId: string | null;
  mapping: ColumnMapping | null;
}

export function createChannel(client: ApiClient, kind: ChannelKind, body: ChannelBody) {
  return answer(
    client.POST('/api/v1/settlement-channels', {
      body: { kind, ...body, gateway: kind === 'Gateway' ? 'Razorpay' : null },
    }),
  );
}

export function updateChannel(client: ApiClient, id: string, version: string, body: ChannelBody) {
  return answer(
    client.PATCH('/api/v1/settlement-channels/{id}', { params: { path: { id } }, headers: ifMatch(version), body }),
  );
}

export function setChannelActive(client: ApiClient, channel: SettlementChannel, active: boolean) {
  const path = active ? '/api/v1/settlement-channels/{id}/reactivate' : '/api/v1/settlement-channels/{id}/deactivate';
  return answer(client.POST(path, { params: { path: { id: channel.id } }, headers: ifMatch(channel.version) }));
}

export interface StatementBody {
  channelId: string;
  fileName: string;
  text: string;
  fileHash: string | null;
}

/** The dry run of a statement: it stores nothing, so it takes no Idempotency-Key. */
export function dryRunStatement(client: ApiClient, body: StatementBody): Promise<StatementDryRun> {
  return answer(client.POST('/api/v1/statement-imports/dry-run', { body }));
}

/** The import of the file the dry run read, under its key (BR-803). */
export function importStatement(client: ApiClient, body: StatementBody, key: string): Promise<StatementImport> {
  return answer(client.POST('/api/v1/statement-imports', { body, headers: { 'Idempotency-Key': key } }));
}

/** The imports of one channel, or of all, the latest first. */
export function useImports(channelId: string, page: number) {
  const client = useApiClient();
  return useQuery({
    queryKey: reconciliationKeys.imports(channelId, page),
    placeholderData: keepPreviousData,
    queryFn: ({ signal }) =>
      answer(
        client.GET('/api/v1/statement-imports', {
          signal,
          params: { query: { ...(channelId === '' ? {} : { ChannelId: channelId }), Page: page, PageSize: 10 } },
        }),
      ),
  });
}

export interface LineListParams {
  channelId: string;
  importId: string;
  status: LineStatus | undefined;
  direction: Direction | undefined;
  from: string;
  to: string;
  search: string;
  sort: string;
  page: number;
}

export function useLines(params: LineListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: reconciliationKeys.lines(params),
    placeholderData: keepPreviousData,
    staleTime: 0,
    queryFn: ({ signal }) =>
      answer(
        client.GET('/api/v1/statement-lines', {
          signal,
          params: {
            query: {
              ...(params.channelId === '' ? {} : { ChannelId: params.channelId }),
              ...(params.importId === '' ? {} : { ImportId: params.importId }),
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.direction === undefined ? {} : { Direction: params.direction }),
              ...(params.from === '' ? {} : { From: params.from }),
              ...(params.to === '' ? {} : { To: params.to }),
              ...(params.search === '' ? {} : { Search: params.search }),
              ...(params.sort === '' ? {} : { Sort: params.sort }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

/**
 * The header names of a sample file, read in the browser from the header row the mapping names: what the mapping's
 * columns are chosen from. Nothing is sent. A field in double quotes may hold a comma.
 */
export function headersOf(text: string, headerRow: number): string[] {
  // A byte order mark (U+FEFF) at the start is not part of the first header name.
  const unmarked = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
  const lines = unmarked.split(/\r?\n/);
  const line = lines[headerRow - 1];
  if (line === undefined) {
    return [];
  }

  const names: string[] = [];
  let field = '';
  let quoted = false;
  for (let index = 0; index < line.length; index++) {
    const character = line[index];
    if (quoted) {
      if (character === '"' && line[index + 1] === '"') {
        field += '"';
        index++;
      } else if (character === '"') {
        quoted = false;
      } else {
        field += character;
      }
    } else if (character === '"' && field === '') {
      quoted = true;
    } else if (character === ',') {
      names.push(field.trim());
      field = '';
    } else {
      field += character;
    }
  }

  names.push(field.trim());
  return names.filter((name) => name !== '');
}
