import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { problemOf } from '../api/call';
import buttons from './buttons.module.css';
import styles from './RecordPicker.module.css';
import { searchMinimum, searchPause } from './useLiveSearch';

/**
 * Chooses one record by searching for it (ADR 0040, amended 2026-11-25): an autocomplete in the combobox pattern of
 * WAI-ARIA. The results open under the box while the user types, after the pause and from the minimum of every search;
 * the arrow keys move through them, Enter chooses the one marked (or searches at once when none is), Escape closes
 * them, and a click or a tap chooses. The choice is shown with a way to change it. Inside a form, Enter never submits
 * the form. The results lie over the page, so nothing below them moves.
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
  /** Reads the records that match; a search that a later key made stale is cancelled through `signal`. */
  find: (text: string, signal: AbortSignal) => Promise<T[]>;
  describe: (record: T) => ReactNode;
  keyOf: (record: T) => string;
  error?: string | undefined;
  hint?: string;
  /** The choice cannot be changed (it never changes once a draft exists). */
  disabled?: boolean;
}) {
  const id = useId();
  const listId = `${id}-results`;
  const [text, setText] = useState('');
  const [searched, setSearched] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const results = useQuery({
    queryKey: ['picker', kind, searched],
    queryFn: ({ signal }) => find(searched ?? '', signal),
    enabled: searched !== null,
    // The results on screen stay while the next ones are read, so the list does not blink at each key.
    placeholderData: keepPreviousData,
  });

  useEffect(() => () => clearTimeout(timer.current), []);

  const records = searched === null ? [] : (results.data ?? []);
  const expanded = open && records.length > 0;
  const optionId = (index: number) => `${id}-option-${String(index)}`;
  const failed = records.length === 0 && results.error !== null;
  const message =
    !open || searched === null
      ? ''
      : records.length > 0
        ? `${String(records.length)} found. Use the arrow keys to choose one.`
        : results.isPending
          ? 'Searching…'
          : failed
            ? problemOf(results.error).title
            : `No active ${kind} matches.`;
  const noteClass =
    message === '' || records.length > 0 ? styles.announcement : failed ? styles.popupError : styles.popupNote;

  const search = (wanted: string) => {
    clearTimeout(timer.current);
    setSearched(wanted);
    setOpen(true);
    setActive(-1);
  };

  const type = (next: string) => {
    const wanted = next.trim();
    setText(next);
    clearTimeout(timer.current);
    if (wanted.length < searchMinimum) {
      setSearched(null);
      setOpen(false);
      setActive(-1);
    } else if (wanted !== searched) {
      timer.current = setTimeout(() => search(wanted), searchPause);
    } else {
      setOpen(true);
    }
  };

  const choose = (record: T) => {
    clearTimeout(timer.current);
    setText('');
    setSearched(null);
    setOpen(false);
    setActive(-1);
    onSelect(record);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    switch (event.key) {
      case 'Enter': {
        // Never the form's submission: the marked result is chosen, or the search is sent without waiting.
        event.preventDefault();
        const marked = expanded ? records[active] : undefined;
        if (marked !== undefined) {
          choose(marked);
        } else if (text.trim() !== '') {
          search(text.trim());
        }
        break;
      }
      case 'ArrowDown':
      case 'ArrowUp': {
        if (records.length === 0) {
          break;
        }
        event.preventDefault();
        const step = event.key === 'ArrowDown' ? 1 : -1;
        setOpen(true);
        setActive(
          !expanded || active < 0
            ? step === 1
              ? 0
              : records.length - 1
            : (active + step + records.length) % records.length,
        );
        break;
      }
      case 'Escape':
        if (open) {
          // The results close; the text stays, and the dialog or form around the box does not see the key.
          event.preventDefault();
          event.stopPropagation();
          setOpen(false);
          setActive(-1);
        }
        break;
      default:
        break;
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
        <div className={styles.search}>
          <input
            id={id}
            className={styles.input}
            type="text"
            role="combobox"
            autoComplete="off"
            value={text}
            aria-autocomplete="list"
            aria-expanded={expanded}
            aria-controls={listId}
            aria-activedescendant={expanded && active >= 0 ? optionId(active) : undefined}
            aria-invalid={error !== undefined}
            aria-describedby={error === undefined ? undefined : `${id}-error`}
            onChange={(event) => type(event.target.value)}
            onKeyDown={onKeyDown}
            onFocus={() => setOpen(searched !== null)}
            onBlur={() => {
              setOpen(false);
              setActive(-1);
            }}
          />
          {expanded && (
            <div className={styles.popup}>
              <ul id={listId} className={styles.results} role="listbox" aria-label={`${kind} found`}>
                {records.map((record, index) => (
                  // The option is chosen by the pointer here, at the press, and by the keyboard from the box, which keeps
                  // the focus (the combobox pattern): an option is never a stop of the Tab key.
                  <li
                    key={keyOf(record)}
                    id={optionId(index)}
                    className={styles.result}
                    role="option"
                    aria-selected={index === active}
                    onMouseDown={(event) => {
                      // The box keeps the focus, so the results do not close under the pointer before the choice.
                      event.preventDefault();
                      if (event.button === 0) {
                        choose(record);
                      }
                    }}
                    onMouseEnter={() => setActive(index)}
                  >
                    {describe(record)}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {/* What a screen reader hears when an answer arrives: the count of the results, which are read one by one as
              the arrows move, or, shown under the box as well, that the search runs, failed or found nothing. */}
          <p className={noteClass} role="status">
            {message}
          </p>
        </div>
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
