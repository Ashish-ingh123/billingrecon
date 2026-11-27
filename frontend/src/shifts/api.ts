import { keepPreviousData, useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { StatusLook } from '../components/documentStatuses';
import { documentStatuses } from '../components/documentStatuses';
import { pageSize } from '../components/listParams';
import { formatMoney } from '../format';
import type { Paise } from '../bills/lineMath';
import { asRupees } from '../money';
import { signedPaise } from '../reports/csv';

/** A shift of the cash counter as the API answers it (Prompt 11.2, ADR 0060; `docs/api.md` section 8.20). */
export type CashierShift = components['schemas']['CashierShiftDto'];
export type ShiftStatus = CashierShift['status'];
export type DayEnd = components['schemas']['DayEndDto'];
export type DayEndShift = components['schemas']['DayEndShiftDto'];

export const shiftStatuses: readonly ShiftStatus[] = ['Open', 'PendingApproval', 'Closed'];
export const shiftStatusLabels: Record<ShiftStatus, StatusLook> = documentStatuses.shift;

/** The notes and coins a drawer is counted in, in rupees, the largest first: the API's own list (BR-313). */
export const denominations = [2000, 500, 200, 100, 50, 20, 10, 5, 2, 1] as const;

/** The most pieces of one denomination a count holds, as the API holds it. */
export const maxPieces = 99_999;

export interface ShiftListParams {
  status: ShiftStatus | undefined;
  date: string;
  page: number;
}

export const shiftKeys = {
  all: ['shifts'] as const,
  mine: ['shifts', 'mine'] as const,
  list: (params: ShiftListParams) => ['shifts', 'list', params] as const,
  one: (id: string) => ['shifts', 'one', id] as const,
};

/** After a shift is opened or closed, or cash joined one: every read of a shift, and the inbox a closing may wait in. */
export function shiftsChanged(queryClient: QueryClient): void {
  for (const key of [['shifts'], ['approvals'], ['reports', 'day-end']]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

/**
 * The shift the signed-in user has now, if any: the one that is open, or the one that waits for approval (a user has
 * one at a time, BR-310). Read fresh on every visit: cash that joined it elsewhere changes its expected cash.
 */
export function useMyShift(enabled = true) {
  const client = useApiClient();
  return useQuery({
    queryKey: shiftKeys.mine,
    enabled,
    staleTime: 0,
    queryFn: async ({ signal }) => {
      const page = await answer(
        client.GET('/api/v1/cashier-shifts', {
          signal,
          params: { query: { Mine: true, Unclosed: true, Page: 1, PageSize: 1 } },
        }),
      );
      return page.items[0] ?? null;
    },
  });
}

export function useShifts(params: ShiftListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: shiftKeys.list(params),
    placeholderData: keepPreviousData,
    staleTime: 0,
    queryFn: ({ signal }) =>
      answer(
        client.GET('/api/v1/cashier-shifts', {
          signal,
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.date === '' ? {} : { Date: params.date }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

export function readShift(client: ApiClient, id: string, signal?: AbortSignal) {
  return answer(
    client.GET('/api/v1/cashier-shifts/{id}', {
      ...(signal === undefined ? {} : { signal }),
      params: { path: { id } },
    }),
  );
}

export function useShift(id: string) {
  const client = useApiClient();
  return useQuery({
    queryKey: shiftKeys.one(id),
    staleTime: 0,
    queryFn: ({ signal }) => readShift(client, id, signal),
  });
}

/** Opens a shift for the signed-in user. No key: a second opening is refused by the rule of one shift at a time. */
export function openShift(client: ApiClient, openingFloat: number) {
  return answer(client.POST('/api/v1/cashier-shifts', { body: { openingFloat } }));
}

export interface CountLine {
  denomination: number;
  pieces: number;
}

/**
 * Closes a shift with its count. The answer says which it was: closed at once (200), or handed to a second person
 * because the count differs (202, BR-314).
 */
export async function closeShift(
  client: ApiClient,
  shift: CashierShift,
  count: CountLine[],
  looseChange: number,
  reason: string,
): Promise<{ shift: CashierShift; waits: boolean }> {
  const { data, status } = await answerWithStatus(
    client.POST('/api/v1/cashier-shifts/{id}/close', {
      params: { path: { id: shift.id } },
      body: { count, looseChange, reason: reason.trim() === '' ? null : reason.trim() },
      headers: ifMatch(shift.version),
    }),
  );
  return { shift: data, waits: status === 202 };
}

/** The pieces typed for each denomination, as text: an empty box is none. */
export type CountText = Record<number, string>;

export const emptyCount = (): CountText => Object.fromEntries(denominations.map((value) => [value, '']));

/** The pieces of a box: a whole number from 0 to the most a count holds, or null when the box holds something else. */
export function piecesOf(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === '') {
    return 0;
  }

  if (!/^\d{1,5}$/.test(trimmed)) {
    return null;
  }

  return Number(trimmed);
}

/** What the boxes come to in paise, exactly; null while one of them is not a number of pieces. */
export function countedPaise(count: CountText, looseChange: Paise): Paise | null {
  let total = looseChange;
  for (const denomination of denominations) {
    const pieces = piecesOf(count[denomination] ?? '');
    if (pieces === null) {
      return null;
    }

    total += BigInt(denomination) * 100n * BigInt(pieces);
  }

  return total;
}

/** The lines the API is sent: the denominations that have pieces. */
export function countLines(count: CountText): CountLine[] {
  return denominations
    .map((denomination) => ({ denomination, pieces: piecesOf(count[denomination] ?? '') ?? 0 }))
    .filter((line) => line.pieces > 0);
}

/** A difference in words: a shortage or an excess, never a bare sign (ADR 0049). */
export function differenceWords(difference: Paise): string {
  if (difference === 0n) {
    return 'None: the count is the expected cash';
  }

  const amount = formatMoney(asRupees(difference < 0n ? -difference : difference));
  return difference < 0n ? `Short by ${amount}` : `Over by ${amount}`;
}

/** The same for a difference the API answered. */
export const differenceOfServer = (difference: number | string) => differenceWords(signedPaise(difference));
