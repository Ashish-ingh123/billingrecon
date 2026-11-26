import type { ComponentProps, Ref } from 'react';
import { Icon } from '../brand/Icon';
import styles from './SearchInput.module.css';

/**
 * The box of every search (ADR 0039 and ADR 0040, amended 2026-11-26): a lens at its start, which says that the box
 * searches and is neither a button nor read out; the small spinner in the lens's place, in the same box, while a search
 * is in flight; and a "Clear search" button at its end while the box holds text. There is no Search or Find button: a
 * search is sent while it is typed.
 */
export function SearchInput({
  busy = false,
  onClear,
  inputClassName,
  ref,
  ...input
}: Omit<ComponentProps<'input'>, 'className'> & {
  /** Whether a search this box sent is still on its way. */
  busy?: boolean;
  /** Empties the box and what it searched for; the button is shown while the box holds text. */
  onClear: () => void;
  inputClassName?: string | undefined;
  ref?: Ref<HTMLInputElement>;
}) {
  const holdsText = typeof input.value === 'string' && input.value !== '';

  return (
    <span className={styles.box}>
      <span className={styles.lead} aria-hidden="true" data-testid={busy ? 'search-spinner' : 'search-lens'}>
        <Icon name={busy ? 'spinner' : 'search'} className={busy ? styles.spinner : undefined} />
      </span>
      <input
        ref={ref}
        autoComplete="off"
        {...input}
        className={[styles.input, inputClassName ?? ''].join(' ').trim()}
      />
      {holdsText && input.disabled !== true && (
        <button
          type="button"
          className={styles.clear}
          aria-label="Clear search"
          // Not a stop of the Tab key: Tab from a search box goes to the next field in one press (ADR 0040, amended
          // 2026-11-26). The keyboard clears a box by deleting its text; this is the pointer's and the touch's way.
          tabIndex={-1}
          // The box keeps the focus: the press of the pointer does not take it, and the click gives it back.
          onMouseDown={(event) => event.preventDefault()}
          onClick={(event) => {
            onClear();
            event.currentTarget.parentElement?.querySelector('input')?.focus();
          }}
        >
          <Icon name="close" />
        </button>
      )}
    </span>
  );
}
