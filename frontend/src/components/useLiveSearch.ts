import { useEffect, useRef, useState, type ChangeEvent, type KeyboardEvent } from 'react';

/** How long the typing rests before a search is sent, in milliseconds (ADR 0039, amended 2026-11-25). */
export const searchPause = 300;

/** The fewest characters a typed search has before it is sent; an empty box is sent, and shows everything again. */
export const searchMinimum = 2;

/** Whether a typed text is sent without Enter: empty, or long enough to be worth a request. */
export function isSearchable(text: string): boolean {
  return text.length === 0 || text.length >= searchMinimum;
}

/**
 * The search box of a list, answered while the user types (ADR 0039, amended 2026-11-25). `sent` is the search the
 * list shows now (the address holds it); `send` is called with the trimmed text once the typing has rested for
 * {@link searchPause}, from {@link searchMinimum} characters or when the box is emptied, and at once on Enter. A text
 * that is already the one sent is not sent again.
 */
export function useLiveSearch(sent: string, send: (text: string) => void) {
  const [text, setText] = useState(sent);
  const [seen, setSeen] = useState(sent);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const latest = useRef({ sent, send });

  // When the address changes the search (Back, a link), the box shows it; the box's own search, which the address
  // holds trimmed, leaves what is being typed alone.
  if (sent !== seen) {
    setSeen(sent);
    if (sent !== text.trim()) {
      setText(sent);
    }
  }

  useEffect(() => {
    latest.current = { sent, send };
  });

  useEffect(() => () => clearTimeout(timer.current), []);

  const sendNow = (wanted: string) => {
    clearTimeout(timer.current);
    if (wanted !== latest.current.sent) {
      latest.current.send(wanted);
    }
  };

  return {
    value: text,
    onChange: (event: ChangeEvent<HTMLInputElement>) => {
      const next = event.target.value;
      const wanted = next.trim();
      setText(next);
      clearTimeout(timer.current);
      if (isSearchable(wanted)) {
        timer.current = setTimeout(() => sendNow(wanted), searchPause);
      }
    },
    /** Enter sends what the box holds at once, and never submits a form around it. */
    onKeyDown: (event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        sendNow(text.trim());
      }
    },
  };
}
