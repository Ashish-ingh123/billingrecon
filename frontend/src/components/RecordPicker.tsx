import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { problemOf } from '../api/call';
import buttons from './buttons.module.css';
import styles from './RecordPicker.module.css';
import { SearchInput } from './SearchInput';
import { searchMinimum, searchPause } from './useLiveSearch';

/** What the box says when it was left with text and no record: the field is marked, and the form still goes on. */
export const noRecordChosen = 'No record chosen';

/**
 * Chooses one record by searching for it (ADR 0040, amended 2026-11-25): an autocomplete in the combobox pattern of
 * WAI-ARIA. The results open under the box while the user types, after the pause and from the minimum of every search;
 * the arrow keys move through them, Enter chooses the one marked, Escape closes them, and a click or a tap chooses.
 * The choice is shown with a way to change it. Inside a form, Enter never submits the form. The results lie over the
 * page, so nothing below them moves.
 *
 * Since 2026-11-26 a form is filled by typing a few letters and pressing Tab. The first result is the marked one while
 * typing. Tab (and Shift+Tab) chooses it and moves on in the same press; if the answer has not arrived, the search is
 * sent at once and its first result chosen when it arrives, unless the user came back and typed again. With no result
 * the box keeps its text and is marked "No record chosen", and the focus has moved on all the same. Leaving by the
 * pointer closes the results and chooses nothing. Where a wrong first result would move money (`chooseOnTab` "single":
 * the payer of a payment), Tab chooses only when one record alone matches.
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
  chooseOnTab = 'first',
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
  /** What Tab chooses: the first result, or a record only when it alone matches. */
  chooseOnTab?: 'first' | 'single';
}) {
  const id = useId();
  const listId = `${id}-results`;
  const queryClient = useQueryClient();
  const [text, setText] = useState('');
  const [searched, setSearched] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [unchosen, setUnchosen] = useState<string | null>(null);
  // How the record shown was chosen: by the keyboard it is announced, and after Enter the focus stays on the field.
  const [chosenBy, setChosenBy] = useState<'tab' | 'enter' | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  // Whether the box is being left by the Tab key, and the search whose answer a Tab is still waiting for.
  const tabbing = useRef(false);
  const awaited = useRef<string | null>(null);
  // Whether the box holds the focus: if a record arrives from outside while it does (a bill's payer, set with its
  // patient), the box is gone, and the focus goes to the way to change the record rather than to nowhere.
  const focused = useRef(false);
  const keyOfSearch = (wanted: string) => ['picker', kind, wanted] as const;
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
  const marked = Math.min(active, records.length - 1);
  const optionId = (index: number) => `${id}-option-${String(index)}`;
  const failed = records.length === 0 && results.error !== null;
  const message =
    !open || searched === null
      ? ''
      : records.length > 0
        ? `${String(records.length)} found. Use the arrow keys to choose one. Tab or Enter takes the first.`
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
    setActive(0);
  };

  const type = (next: string) => {
    const wanted = next.trim();
    setText(next);
    setUnchosen(null);
    // Whoever types again takes back a choice that a Tab left to a late answer.
    awaited.current = null;
    clearTimeout(timer.current);
    if (wanted.length < searchMinimum) {
      setSearched(null);
      setOpen(false);
      setActive(0);
    } else if (wanted !== searched) {
      timer.current = setTimeout(() => search(wanted), searchPause);
    } else {
      setOpen(true);
    }
  };

  const choose = (record: T, by: 'tab' | 'enter' | null) => {
    clearTimeout(timer.current);
    awaited.current = null;
    setText('');
    setSearched(null);
    setOpen(false);
    setActive(0);
    setUnchosen(null);
    setChosenBy(by);
    onSelect(record);
  };

  /** What a Tab does with the answer of the search the box held: a record, or the mark that none was chosen. */
  const settle = (found: readonly T[], at: number) => {
    const wanted = chooseOnTab === 'single' ? (found.length === 1 ? found[0] : undefined) : (found[at] ?? found[0]);
    if (wanted !== undefined) {
      choose(wanted, 'tab');
    } else if (found.length > 1) {
      setUnchosen(`${noRecordChosen}: ${String(found.length)} match. Choose one from the list.`);
    } else {
      setUnchosen(noRecordChosen);
    }
  };

  const leaveByTab = () => {
    const wanted = text.trim();
    if (wanted === '') {
      return;
    }

    if (wanted.length < searchMinimum) {
      setUnchosen(noRecordChosen);
      return;
    }

    const answered = wanted === searched && results.data !== undefined && !results.isPlaceholderData;
    if (answered) {
      settle(results.data, marked);
      return;
    }

    // The answer is not here: the search is sent at once, and its first result is chosen when it arrives, unless
    // the user comes back and types again.
    clearTimeout(timer.current);
    setSearched(wanted);
    awaited.current = wanted;
    void queryClient.fetchQuery({ queryKey: keyOfSearch(wanted), queryFn: ({ signal }) => find(wanted, signal) }).then(
      (found) => {
        if (awaited.current === wanted) {
          awaited.current = null;
          settle(found, 0);
        }
      },
      () => {
        if (awaited.current === wanted) {
          awaited.current = null;
          setUnchosen(noRecordChosen);
        }
      },
    );
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    tabbing.current = event.key === 'Tab';
    switch (event.key) {
      case 'Enter': {
        // Never the form's submission: the marked result is chosen, or the search is sent without waiting.
        event.preventDefault();
        const record = expanded ? records[marked] : undefined;
        if (record !== undefined) {
          choose(record, 'enter');
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
        // Opening the results again marks the first; from there the arrows go round.
        setActive(expanded ? (marked + step + records.length) % records.length : step === 1 ? 0 : records.length - 1);
        break;
      }
      case 'Escape':
        if (open) {
          // The results close; the text stays, and the dialog or form around the box does not see the key.
          event.preventDefault();
          event.stopPropagation();
          setOpen(false);
        }
        break;
      default:
        break;
    }
  };

  const invalid = error !== undefined || unchosen !== null;
  const describedBy =
    [error === undefined ? '' : `${id}-error`, unchosen === null ? '' : `${id}-unchosen`].filter(Boolean).join(' ') ||
    undefined;

  return (
    <div className={styles.picker}>
      <label className={styles.label} htmlFor={id}>
        {label}
      </label>
      {selected !== null ? (
        <div className={styles.chosen}>
          <span id={id}>{describe(selected)}</span>
          {!disabled && (
            <button
              type="button"
              className={buttons.link}
              // After Enter the focus stays with the field: on the way to change what was just chosen.
              ref={(button) => {
                if (button !== null && (chosenBy === 'enter' || focused.current)) {
                  focused.current = false;
                  button.focus();
                }
              }}
              onClick={() => {
                setChosenBy(null);
                onSelect(null);
              }}
            >
              Change
            </button>
          )}
        </div>
      ) : (
        <div className={styles.search}>
          <SearchInput
            id={id}
            type="text"
            role="combobox"
            inputClassName={styles.tall}
            value={text}
            busy={searched !== null && results.isFetching}
            onClear={() => type('')}
            aria-autocomplete="list"
            aria-expanded={expanded}
            aria-controls={listId}
            aria-activedescendant={expanded ? optionId(marked) : undefined}
            aria-invalid={invalid}
            aria-describedby={describedBy}
            onChange={(event) => type(event.target.value)}
            onKeyDown={onKeyDown}
            onKeyUp={() => {
              tabbing.current = false;
            }}
            onFocus={() => {
              focused.current = true;
              setOpen(searched !== null);
            }}
            onBlur={() => {
              focused.current = false;
              const byTab = tabbing.current;
              tabbing.current = false;
              setOpen(false);
              // By the pointer the results close and nothing is chosen; by Tab the first result goes with the user.
              if (byTab) {
                leaveByTab();
              }
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
                    aria-selected={index === marked}
                    onMouseDown={(event) => {
                      // The box keeps the focus, so the results do not close under the pointer before the choice.
                      event.preventDefault();
                      if (event.button === 0) {
                        choose(record, null);
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
      {/* A record chosen by the keyboard is said aloud: after Tab the user is already in the next field. */}
      <p className={styles.announcement} role="status">
        {selected !== null && chosenBy !== null && <>Chosen: {describe(selected)}</>}
      </p>
      {hint !== undefined && <p className={styles.note}>{hint}</p>}
      {unchosen !== null && selected === null && (
        <p id={`${id}-unchosen`} className={styles.error}>
          {unchosen}
        </p>
      )}
      {error !== undefined && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  );
}
