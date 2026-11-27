/**
 * Which columns of a table a user shows, and in which order (Prompt 12.2; ADR 0063): kept in this browser, for one
 * hospital, one user and one table, never sent anywhere. A browser that keeps nothing (a private window, storage
 * switched off) shows every column in its own order, and the choice lasts while the page is open.
 */
export interface ColumnPrefs {
  hidden: string[];
  order: string[];
}

export const noPrefs: ColumnPrefs = { hidden: [], order: [] };

/** The key of one table's choice for one user of one hospital. */
export function prefsKey(hospital: string, userId: string, tableId: string): string {
  return `billingrecon.columns.v1.${hospital}.${userId}.${tableId}`;
}

const isNames = (value: unknown): value is string[] =>
  Array.isArray(value) && value.every((item) => typeof item === 'string');

export function readPrefs(key: string): ColumnPrefs {
  try {
    const raw = window.localStorage.getItem(key);
    if (raw === null) {
      return noPrefs;
    }

    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed === 'object' && parsed !== null) {
      const { hidden, order } = parsed as Record<string, unknown>;
      return { hidden: isNames(hidden) ? hidden : [], order: isNames(order) ? order : [] };
    }
  } catch {
    // Nothing kept, or nothing readable: the table's own columns.
  }

  return noPrefs;
}

export function writePrefs(key: string, prefs: ColumnPrefs): void {
  try {
    if (prefs.hidden.length === 0 && prefs.order.length === 0) {
      window.localStorage.removeItem(key);
    } else {
      window.localStorage.setItem(key, JSON.stringify(prefs));
    }
  } catch {
    // A browser that keeps nothing: the choice lasts while the page is open.
  }
}

/**
 * The order the columns are shown in: the kept order for the columns it names, then any column it does not know (a
 * column added since) in its own place at the end. A name that is no longer a column is dropped.
 */
export function orderOf(ids: readonly string[], kept: readonly string[]): string[] {
  const known = kept.filter((id) => ids.includes(id));
  return [...known, ...ids.filter((id) => !known.includes(id))];
}
