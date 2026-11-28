import { useEffect, useId, useRef, useState, type ChangeEvent } from 'react';
import listStyles from '../ListControls.module.css';
import { searchPause } from '../useLiveSearch';

/**
 * A filter by a whole word of the record (a service item's category, compared whole by the API): sent after the pause of
 * typing, as a search is, and at once when the box is left (Prompt 12.2, ADR 0063). An empty box filters nothing.
 */
export function CategoryFilter({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const id = useId();
  const [text, setText] = useState(value);
  const [seen, setSeen] = useState(value);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const latest = useRef({ value, onChange });

  // The address changed it (Back, Clear filters): the box shows it.
  if (value !== seen) {
    setSeen(value);
    if (value !== text.trim()) {
      setText(value);
    }
  }

  useEffect(() => {
    latest.current = { value, onChange };
  });

  useEffect(() => () => clearTimeout(timer.current), []);

  const send = (next: string) => {
    clearTimeout(timer.current);
    if (next !== latest.current.value) {
      latest.current.onChange(next);
    }
  };

  const changed = (event: ChangeEvent<HTMLInputElement>) => {
    const next = event.target.value;
    setText(next);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => send(next.trim()), searchPause);
  };

  return (
    <div className={listStyles.field}>
      <label htmlFor={id}>Category</label>
      <input
        id={id}
        className={listStyles.input}
        autoComplete="off"
        value={text}
        onChange={changed}
        onBlur={() => send(text.trim())}
      />
    </div>
  );
}
