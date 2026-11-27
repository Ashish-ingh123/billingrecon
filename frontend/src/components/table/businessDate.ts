import { answer } from '../../api/call';
import type { ApiClient } from '../../api/client';

/** The shortcuts of a date filter (Prompt 12.2; ADR 0063). */
export type DateShortcut = 'today' | 'week' | 'month' | 'last30';

export const shortcutLabels: Record<DateShortcut, string> = {
  today: 'Today',
  week: 'This week',
  month: 'This month',
  last30: 'Last 30 days',
};

/** Today in India by the server's clock: the day the shortcuts count from, never the browser's (BR-003). */
export async function readBusinessDate(client: ApiClient): Promise<string> {
  return (await answer(client.GET('/api/v1/system/business-date'))).today;
}

const day = (iso: string) => new Date(`${iso}T00:00:00Z`);
const iso = (date: Date) => date.toISOString().slice(0, 10);
const plus = (date: Date, days: number) => new Date(date.getTime() + days * 86_400_000);

/**
 * The period of a shortcut, both ends included, counted from `today` (`yyyy-MM-dd`): today alone; the week from its
 * Monday; the month from its first; the 30 days that end today. The arithmetic is of dates alone, in UTC, so the
 * browser's own time zone and clock play no part.
 */
export function periodOf(shortcut: DateShortcut, today: string): { from: string; to: string } {
  const date = day(today);
  switch (shortcut) {
    case 'today':
      return { from: today, to: today };
    case 'week':
      return { from: iso(plus(date, -((date.getUTCDay() + 6) % 7))), to: today };
    case 'month':
      return { from: `${today.slice(0, 7)}-01`, to: today };
    case 'last30':
      return { from: iso(plus(date, -29)), to: today };
  }
}
