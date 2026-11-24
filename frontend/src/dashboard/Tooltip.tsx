import { useId, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import styles from './Tooltip.module.css';

/** One line of a tooltip: what it is, its figure as the server gave it, and the colour of its series if it has one. */
export interface TipRow {
  label: string;
  value: ReactNode;
  /** The series' colour class of `Tooltip.module.css` (`series1` to `series6`), for a swatch beside the label. */
  series?: 1 | 2 | 3 | 4 | 5 | 6;
  strong?: boolean;
}

/**
 * The content of every tooltip of the dashboard (ADR 0050): a title, its figures exact, in Indian grouping and tabular
 * figures, and a note. The figures are the server's as they are; nothing is computed here.
 */
export function TipBox({ title, rows, note }: { title: ReactNode; rows: readonly TipRow[]; note?: ReactNode }) {
  return (
    <div className={styles.box}>
      <p className={styles.title}>{title}</p>
      <dl className={styles.rows}>
        {rows.map((row) => (
          <div key={row.label} className={row.strong === true ? styles.strong : undefined}>
            <dt>
              {row.series !== undefined && (
                <span className={`${styles.swatch} ${styles[`series${String(row.series)}`]}`} aria-hidden="true" />
              )}
              {row.label}
            </dt>
            <dd>{row.value}</dd>
          </div>
        ))}
      </dl>
      {note !== undefined && <p className={styles.note}>{note}</p>}
    </div>
  );
}

/**
 * A figure with a tooltip of its own (ADR 0050): the figure is a button, so the tooltip opens on hover, on keyboard focus
 * and on a tap, and closes on Escape, on blur, when the pointer leaves and on a second tap. The tooltip describes the
 * button for assistive technology. It opens under the figure, aligned to its start or its end, so that it never covers
 * the pointer and stays inside the screen.
 */
export function Tip({
  children,
  tip,
  align = 'start',
  className,
  label,
}: {
  children: ReactNode;
  tip: ReactNode;
  align?: 'start' | 'end';
  className?: string;
  /** The button's name, when its content does not give one (a bar). */
  label?: string;
}) {
  const id = useId();
  const [open, setOpen] = useState(false);

  const onPointerEnter = (event: PointerEvent) => {
    if (event.pointerType === 'mouse') setOpen(true);
  };
  const onPointerLeave = (event: PointerEvent) => {
    if (event.pointerType === 'mouse') setOpen(false);
  };
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape' && open) {
      event.stopPropagation();
      setOpen(false);
    }
  };

  return (
    <span
      className={`${styles.anchor} ${className ?? ''}`}
      onPointerEnter={onPointerEnter}
      onPointerLeave={onPointerLeave}
    >
      <button
        type="button"
        className={styles.trigger}
        aria-label={label}
        aria-describedby={open ? id : undefined}
        onPointerUp={(event) => {
          // A tap opens the tooltip and a second tap closes it; a mouse opens it on hover instead.
          if (event.pointerType !== 'mouse') setOpen((was) => !was);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
        onKeyDown={onKeyDown}
      >
        {children}
      </button>
      {open && (
        <span role="tooltip" id={id} className={`${styles.floating} ${align === 'end' ? styles.end : ''}`}>
          {tip}
        </span>
      )}
    </span>
  );
}
