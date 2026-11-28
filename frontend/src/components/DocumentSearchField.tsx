import { useId } from 'react';
import styles from './ListControls.module.css';
import { SearchInput } from './SearchInput';
import { useLiveSearch } from './useLiveSearch';

/** What the one search box of a list of documents looks in; its label says so (Prompt 11.1; ADR 0036, amended). */
export const documentSearchLabel = 'Number, patient, payer or amount';

/**
 * The search box of a list of documents (Prompt 11.1): one box for a part of the number, of the patient's name or MRN,
 * of the payer's name, or an exact amount. It is the box of every search (the lens, the spinner in its place, Clear),
 * sent while it is typed, at once on Enter and when the box is left (ADR 0039 and ADR 0040, amended 2026-11-26).
 * `sent` is the search the address holds; `onSend` puts the next one there.
 */
export function DocumentSearchField({
  sent,
  onSend,
  busy = false,
  label = documentSearchLabel,
}: {
  sent: string;
  onSend: (text: string) => void;
  /** Whether the list is reading: the box shows its spinner in the lens's place. */
  busy?: boolean;
  /** What the box looks in, when a list searches other fields than a document's (the statement lines, Prompt 13.1c). */
  label?: string;
}) {
  const search = useLiveSearch(sent, onSend);
  const id = useId();

  return (
    <div className={styles.field}>
      <label htmlFor={id}>{label}</label>
      <SearchInput id={id} type="search" busy={busy} {...search} />
    </div>
  );
}
