import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '../../brand/Icon';
import buttons from '../buttons.module.css';
import styles from './Table.module.css';

/**
 * The "Columns" menu of a table (Prompt 12.2; ADR 0063): a button that opens a panel where each column can be shown or
 * hidden with its checkbox and moved up or down with two buttons, all by keyboard, and "Reset columns" brings back the
 * table's own. A column that is never hidden has its checkbox ticked and disabled, with the reason. Escape closes the
 * panel and gives the focus back to the button.
 */
export function ColumnsMenu({
  tableLabel,
  columns,
  hidden,
  onToggle,
  onMove,
  onReset,
}: {
  tableLabel: string;
  columns: readonly { id: string; header: string; fixed: boolean }[];
  hidden: ReadonlySet<string>;
  onToggle: (id: string) => void;
  onMove: (id: string, by: -1 | 1) => void;
  onReset: () => void;
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  const close = () => {
    setOpen(false);
    button.current?.focus();
  };

  // Open: the focus goes into the panel, and Escape anywhere closes it and gives the focus back to the button.
  useEffect(() => {
    if (!open) {
      return undefined;
    }

    // The first checkbox that can be used; a button only when every column is fixed.
    (
      panel.current?.querySelector<HTMLElement>('input:not(:disabled)') ??
      panel.current?.querySelector<HTMLElement>('button:not(:disabled)')
    )?.focus();
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        setOpen(false);
        button.current?.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open]);

  return (
    <div className={styles.columns}>
      <button
        ref={button}
        type="button"
        className={buttons.secondary}
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((now) => !now)}
      >
        <Icon name="columns" />
        Columns
      </button>
      {open && (
        <div ref={panel} id={panelId} className={styles.panel} role="group" aria-label={`Columns of ${tableLabel}`}>
          <ul className={styles.columnList}>
            {columns.map((column, index) => (
              <li key={column.id}>
                <label className={styles.columnName}>
                  <input
                    type="checkbox"
                    checked={column.fixed || !hidden.has(column.id)}
                    disabled={column.fixed}
                    aria-describedby={column.fixed ? `${panelId}-fixed` : undefined}
                    onChange={() => onToggle(column.id)}
                  />
                  {column.header}
                </label>
                <span className={styles.move}>
                  <button
                    type="button"
                    className={buttons.link}
                    aria-label={`Move ${column.header} up`}
                    disabled={index === 0}
                    onClick={() => onMove(column.id, -1)}
                  >
                    Up
                  </button>
                  <button
                    type="button"
                    className={buttons.link}
                    aria-label={`Move ${column.header} down`}
                    disabled={index === columns.length - 1}
                    onClick={() => onMove(column.id, 1)}
                  >
                    Down
                  </button>
                </span>
              </li>
            ))}
          </ul>
          <p id={`${panelId}-fixed`} className={styles.note}>
            The number and the status are always shown: a row is known by them.
          </p>
          <div className={styles.panelActions}>
            <button type="button" className={buttons.secondary} onClick={onReset}>
              Reset columns
            </button>
            <button type="button" className={buttons.link} onClick={close}>
              Close
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
