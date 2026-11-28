import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState, type ChangeEvent, type ReactNode } from 'react';
import { useApiClient } from '../../api/ApiClientContext';
import buttons from '../buttons.module.css';
import { DocumentSearchField } from '../DocumentSearchField';
import listStyles from '../ListControls.module.css';
import { searchPause } from '../useLiveSearch';
import { periodOf, readBusinessDate, shortcutLabels, type DateShortcut } from './businessDate';
import { filtersInUse, type ListState } from './listState';
import styles from './Table.module.css';

/** A filter of a list's bar (Prompt 12.2; ADR 0063); each answers as it is changed. */
export type FilterSpec<K extends string> =
  | { kind: 'search'; key: K }
  | { kind: 'select'; key: K; label: string; options: readonly { value: string; label: string }[] }
  | { kind: 'dates'; from: K; to: K; label: string }
  | { kind: 'amounts'; from: K; to: K; label: string };

/** An amount as the API reads it: rupees from 0 with at most two decimals. */
const isAmount = (text: string) => /^\d{1,13}(\.\d{1,2})?$/.test(text);

/**
 * The filter bar of a list: the search box of 11.1, selects, a period of business dates with its shortcuts and amounts
 * from and to. A select or a date answers at once; an amount, like the search, after the pause of typing. The count of
 * the filters in use is said in words, and "Clear filters" empties them. The rows on screen stay until the new ones
 * arrive (`useListQuery`).
 */
export function FilterBar<K extends string>({
  state,
  filters,
  busy = false,
  children,
}: {
  state: ListState<K>;
  filters: readonly FilterSpec<K>[];
  busy?: boolean;
  /** Filters of the list's own, after the shared ones (a user picker). */
  children?: ReactNode;
}) {
  return (
    <div className={styles.filters} role="search" aria-label="Filters">
      {filters.map((filter) => {
        switch (filter.kind) {
          case 'search':
            return (
              <DocumentSearchField
                key={filter.key}
                sent={state.values[filter.key]}
                onSend={(text) => state.change({ [filter.key]: text } as Partial<Record<K, string>>)}
                busy={busy}
              />
            );
          case 'select':
            return <SelectFilter key={filter.key} state={state} filter={filter} />;
          case 'dates':
            return <DateRange key={filter.from} state={state} filter={filter} />;
          case 'amounts':
            return <AmountRange key={filter.from} state={state} filter={filter} />;
        }
      })}
      {children}
      <p className={styles.inUse}>
        <span role="status">{filtersInUse(state.inUse)}</span>
        {state.inUse > 0 && (
          <button type="button" className={buttons.link} onClick={state.clear}>
            Clear filters
          </button>
        )}
      </p>
    </div>
  );
}

function SelectFilter<K extends string>({
  state,
  filter,
}: {
  state: ListState<K>;
  filter: Extract<FilterSpec<K>, { kind: 'select' }>;
}) {
  const id = useId();
  return (
    <div className={listStyles.field}>
      <label htmlFor={id}>{filter.label}</label>
      <select
        id={id}
        className={listStyles.input}
        value={state.values[filter.key]}
        onChange={(event) => state.change({ [filter.key]: event.target.value } as Partial<Record<K, string>>)}
      >
        <option value="">All</option>
        {filter.options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

/** What a range reads and writes of a list: its values and their change (a report's filters too). */
export type RangeState<K extends string> = Pick<ListState<K>, 'values' | 'change'>;

/**
 * A period of business dates in India, both ends included, with four shortcuts. A shortcut asks the server for today
 * (`GET /api/v1/system/business-date`) and counts from it: the browser's clock plays no part, whatever it says.
 */
export function DateRange<K extends string>({
  state,
  filter,
}: {
  state: RangeState<K>;
  filter: Extract<FilterSpec<K>, { kind: 'dates' }>;
}) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const fromId = useId();
  const toId = useId();
  const [failed, setFailed] = useState(false);

  const apply = async (shortcut: DateShortcut) => {
    try {
      const today = await queryClient.fetchQuery({
        queryKey: ['business-date'],
        queryFn: () => readBusinessDate(client),
        staleTime: 60_000,
      });
      setFailed(false);
      const { from, to } = periodOf(shortcut, today);
      state.change({ [filter.from]: from, [filter.to]: to } as Partial<Record<K, string>>);
    } catch {
      setFailed(true);
    }
  };

  return (
    <fieldset className={styles.range}>
      <legend>{filter.label}</legend>
      <div className={styles.rangeFields}>
        <label htmlFor={fromId}>From</label>
        <input
          id={fromId}
          className={listStyles.input}
          type="date"
          value={state.values[filter.from]}
          onChange={(event) => state.change({ [filter.from]: event.target.value } as Partial<Record<K, string>>)}
        />
        <label htmlFor={toId}>to</label>
        <input
          id={toId}
          className={listStyles.input}
          type="date"
          value={state.values[filter.to]}
          onChange={(event) => state.change({ [filter.to]: event.target.value } as Partial<Record<K, string>>)}
        />
      </div>
      <div className={styles.shortcuts} role="group" aria-label={`${filter.label}: shortcuts`}>
        {(Object.keys(shortcutLabels) as DateShortcut[]).map((shortcut) => (
          <button key={shortcut} type="button" className={buttons.link} onClick={() => void apply(shortcut)}>
            {shortcutLabels[shortcut]}
          </button>
        ))}
      </div>
      {failed && (
        <p className={styles.fieldError} role="alert">
          The server&rsquo;s date could not be read. Choose the dates instead.
        </p>
      )}
    </fieldset>
  );
}

/** Amounts from and to, in rupees, each sent after the pause of typing; an amount that is not one is said and not sent. */
export function AmountRange<K extends string>({
  state,
  filter,
}: {
  state: RangeState<K>;
  filter: Extract<FilterSpec<K>, { kind: 'amounts' }>;
}) {
  return (
    <fieldset className={styles.range}>
      <legend>{filter.label}</legend>
      <div className={styles.rangeFields}>
        <AmountField
          label="At least"
          sent={state.values[filter.from]}
          send={(text) => state.change({ [filter.from]: text } as Partial<Record<K, string>>)}
        />
        <AmountField
          label="At most"
          sent={state.values[filter.to]}
          send={(text) => state.change({ [filter.to]: text } as Partial<Record<K, string>>)}
        />
      </div>
    </fieldset>
  );
}

function AmountField({ label, sent, send }: { label: string; sent: string; send: (text: string) => void }) {
  const id = useId();
  const [text, setText] = useState(sent);
  const [seen, setSeen] = useState(sent);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const latest = useRef({ sent, send });
  const wanted = text.trim().replaceAll(',', '');
  const invalid = wanted !== '' && !isAmount(wanted);

  // The address changed the amount (Back, Clear filters): the field shows it.
  if (sent !== seen) {
    setSeen(sent);
    if (sent !== wanted) {
      setText(sent);
    }
  }

  useEffect(() => {
    latest.current = { sent, send };
  });

  useEffect(() => () => clearTimeout(timer.current), []);

  const onChange = (event: ChangeEvent<HTMLInputElement>) => {
    const next = event.target.value;
    setText(next);
    clearTimeout(timer.current);
    const value = next.trim().replaceAll(',', '');
    if (value === '' || isAmount(value)) {
      timer.current = setTimeout(() => {
        if (value !== latest.current.sent) {
          latest.current.send(value);
        }
      }, searchPause);
    }
  };

  return (
    <>
      <label htmlFor={id}>{label}</label>
      <span className={styles.amount}>
        <input
          id={id}
          className={listStyles.input}
          inputMode="decimal"
          autoComplete="off"
          value={text}
          onChange={onChange}
          aria-invalid={invalid}
          aria-describedby={invalid ? `${id}-error` : undefined}
        />
      </span>
      <span aria-hidden="true">₹</span>
      {invalid && (
        <span id={`${id}-error`} className={styles.fieldError}>
          An amount, like 1500 or 1500.50
        </span>
      )}
    </>
  );
}
