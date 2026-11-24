import type { ComponentProps, ReactNode, Ref } from 'react';
import styles from './Field.module.css';

interface FieldFrame {
  id: string;
  label: string;
  /** The message of the field's error: the form's own, or the API's for this field. */
  error?: string | undefined;
  hint?: string;
  /** The field must be filled: a mark beside the label, and aria-required on the control (ADR 0049). The form still checks. */
  requiredMark?: boolean;
}

function Frame({ id, label, error, hint, requiredMark, children }: FieldFrame & { children: ReactNode }) {
  return (
    <div className={styles.field}>
      <span className={styles.labelRow}>
        <label className={styles.label} htmlFor={id}>
          {label}
        </label>
        {requiredMark === true && (
          <span className={styles.required} aria-hidden="true">
            Required
          </span>
        )}
      </span>
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

/**
 * A text input with its label, a hint and its error, wired for assistive technology. A unit may stand inside the field,
 * before its value (₹) or after it (%): drawn, not read, because the label already names it (ADR 0049).
 */
export function TextField({
  id,
  label,
  error,
  hint,
  requiredMark,
  leading,
  trailing,
  ref,
  ...input
}: FieldFrame & ComponentProps<'input'> & { leading?: string; trailing?: string; ref?: Ref<HTMLInputElement> }) {
  const control = (
    <input
      id={id}
      ref={ref}
      className={[
        styles.input,
        leading === undefined ? '' : styles.withLeading,
        trailing === undefined ? '' : styles.withTrailing,
      ].join(' ')}
      aria-invalid={error !== undefined}
      aria-describedby={describedBy(id, error, hint)}
      aria-required={requiredMark === true ? true : undefined}
      {...input}
    />
  );

  return (
    <Frame
      id={id}
      label={label}
      error={error}
      {...(hint === undefined ? {} : { hint })}
      {...(requiredMark === undefined ? {} : { requiredMark })}
    >
      {leading === undefined && trailing === undefined ? (
        control
      ) : (
        <span className={styles.adorned}>
          {leading !== undefined && (
            <span className={styles.leading} aria-hidden="true">
              {leading}
            </span>
          )}
          {control}
          {trailing !== undefined && (
            <span className={styles.trailing} aria-hidden="true">
              {trailing}
            </span>
          )}
        </span>
      )}
    </Frame>
  );
}

/** A select with its label, a hint and its error. */
export function SelectField({
  id,
  label,
  error,
  hint,
  requiredMark,
  ref,
  children,
  ...select
}: FieldFrame & ComponentProps<'select'> & { ref?: Ref<HTMLSelectElement> }) {
  return (
    <Frame
      id={id}
      label={label}
      error={error}
      {...(hint === undefined ? {} : { hint })}
      {...(requiredMark === undefined ? {} : { requiredMark })}
    >
      <select
        id={id}
        ref={ref}
        className={styles.input}
        aria-required={requiredMark === true ? true : undefined}
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
