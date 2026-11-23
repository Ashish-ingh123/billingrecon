import { useQuery } from '@tanstack/react-query';
import { useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { problemOf } from '../api/call';
import buttons from './buttons.module.css';
import styles from './RecordPicker.module.css';

/**
 * Chooses one record by searching for it (ADR 0040): the user types, presses Find or Enter, and picks one of the
 * results; the choice is shown with a way to change it. Inside a form, Enter searches and never submits the form.
 */
export function RecordPicker<T>({
  kind,
  label,
  selected,
  onSelect,
  find,
  describe,
  keyOf,
  error,
  hint,
  disabled = false,
}: {
  /** What is chosen, for the query cache and the messages: "patient". */
  kind: string;
  label: string;
  selected: T | null;
  onSelect: (record: T | null) => void;
  find: (text: string) => Promise<T[]>;
  describe: (record: T) => ReactNode;
  keyOf: (record: T) => string;
  error?: string | undefined;
  hint?: string;
  /** The choice cannot be changed (it never changes once a draft exists). */
  disabled?: boolean;
}) {
  const id = useId();
  const [text, setText] = useState('');
  const [searched, setSearched] = useState<string | null>(null);
  const results = useQuery({
    queryKey: ['picker', kind, searched],
    queryFn: () => find(searched ?? ''),
    enabled: searched !== null,
  });

  const search = () => setSearched(text.trim());
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      search();
    }
  };

  return (
    <div className={styles.picker}>
      <label className={styles.label} htmlFor={id}>
        {label}
      </label>
      {selected !== null ? (
        <div className={styles.chosen}>
          <span id={id}>{describe(selected)}</span>
          {!disabled && (
            <button type="button" className={buttons.link} onClick={() => onSelect(null)}>
              Change
            </button>
          )}
        </div>
      ) : (
        <>
          <div className={styles.search}>
            <input
              id={id}
              className={styles.input}
              type="search"
              value={text}
              aria-invalid={error !== undefined}
              aria-describedby={error === undefined ? undefined : `${id}-error`}
              onChange={(event) => setText(event.target.value)}
              onKeyDown={onKeyDown}
            />
            <button type="button" className={buttons.secondary} onClick={search}>
              Find
            </button>
          </div>
          {searched !== null && (
            <div aria-live="polite">
              {results.isPending ? (
                <p className={styles.note}>Searching…</p>
              ) : results.error !== null ? (
                <p className={styles.error}>{problemOf(results.error).title}</p>
              ) : results.data.length === 0 ? (
                <p className={styles.note}>No active {kind} matches.</p>
              ) : (
                <ul className={styles.results} aria-label={`${kind} found`}>
                  {results.data.map((record) => (
                    <li key={keyOf(record)}>
                      <button type="button" className={styles.result} onClick={() => onSelect(record)}>
                        {describe(record)}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </>
      )}
      {hint !== undefined && <p className={styles.note}>{hint}</p>}
      {error !== undefined && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  );
}
