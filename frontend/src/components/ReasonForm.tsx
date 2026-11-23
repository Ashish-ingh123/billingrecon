import { useId, useState } from 'react';
import buttons from './buttons.module.css';
import fieldStyles from './Field.module.css';
import forms from './forms.module.css';

/**
 * An action that needs a reason, asked for in place (ADR 0039 point 4): a reversal request, a void. The reason is
 * required and at most 500 characters, as the API has it.
 */
export function ReasonForm({
  label,
  confirmLabel,
  onConfirm,
  onCancel,
  busy = false,
}: {
  label: string;
  confirmLabel: string;
  onConfirm: (reason: string) => void;
  onCancel: () => void;
  busy?: boolean;
}) {
  const id = useId();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const confirm = () => {
    const text = reason.trim();
    if (text === '') {
      setError('Give the reason.');
      return;
    }

    if (text.length > 500) {
      setError('The reason is at most 500 characters.');
      return;
    }

    setError(null);
    onConfirm(text);
  };

  return (
    <div className={forms.grid}>
      <div className={`${fieldStyles.field} ${forms.wide}`}>
        <label className={fieldStyles.label} htmlFor={id}>
          {label}
        </label>
        <textarea
          id={id}
          className={fieldStyles.input}
          rows={2}
          value={reason}
          aria-invalid={error !== null}
          aria-describedby={error === null ? undefined : `${id}-error`}
          onChange={(event) => setReason(event.target.value)}
        />
        {error !== null && (
          <p id={`${id}-error`} className={fieldStyles.error}>
            {error}
          </p>
        )}
      </div>
      <div className={forms.actions}>
        <button type="button" className={buttons.danger} disabled={busy} onClick={confirm}>
          {confirmLabel}
        </button>
        <button type="button" className={buttons.secondary} onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}
