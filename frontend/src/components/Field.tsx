import type { ComponentProps, ReactNode, Ref } from 'react';
import styles from './Field.module.css';

interface FieldFrame {
  id: string;
  label: string;
  /** The message of the field's error: the form's own, or the API's for this field. */
  error?: string | undefined;
  hint?: string;
}

function Frame({ id, label, error, hint, children }: FieldFrame & { children: ReactNode }) {
  return (
    <div className={styles.field}>
      <label className={styles.label} htmlFor={id}>
        {label}
      </label>
      {children}
      {hint !== undefined && (
        <p id={`${id}-hint`} className={styles.hint}>
          {hint}
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

function describedBy(id: string, error: string | undefined, hint: string | undefined): string | undefined {
  const ids = [hint === undefined ? null : `${id}-hint`, error === undefined ? null : `${id}-error`].filter(Boolean);
  return ids.length === 0 ? undefined : ids.join(' ');
}

/** A text input with its label, a hint and its error, wired for assistive technology. */
export function TextField({
  id,
  label,
  error,
  hint,
  ref,
  ...input
}: FieldFrame & ComponentProps<'input'> & { ref?: Ref<HTMLInputElement> }) {
  return (
    <Frame id={id} label={label} error={error} {...(hint === undefined ? {} : { hint })}>
      <input
        id={id}
        ref={ref}
        className={styles.input}
        aria-invalid={error !== undefined}
        aria-describedby={describedBy(id, error, hint)}
        {...input}
      />
    </Frame>
  );
}

/** A select with its label, a hint and its error. */
export function SelectField({
  id,
  label,
  error,
  hint,
  ref,
  children,
  ...select
}: FieldFrame & ComponentProps<'select'> & { ref?: Ref<HTMLSelectElement> }) {
  return (
    <Frame id={id} label={label} error={error} {...(hint === undefined ? {} : { hint })}>
      <select
        id={id}
        ref={ref}
        className={styles.input}
        aria-invalid={error !== undefined}
        aria-describedby={describedBy(id, error, hint)}
        {...select}
      >
        {children}
      </select>
    </Frame>
  );
}

/** A value that cannot be changed here, shown where its field would be. */
export function ReadOnlyField({ label, value, hint }: { label: string; value: ReactNode; hint?: string }) {
  return (
    <div className={styles.field}>
      <span className={styles.label}>{label}</span>
      <span className={styles.readOnly}>{value}</span>
      {hint !== undefined && <p className={styles.hint}>{hint}</p>}
    </div>
  );
}
