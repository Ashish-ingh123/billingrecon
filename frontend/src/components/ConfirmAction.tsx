import { useState } from 'react';
import buttons from './buttons.module.css';

/**
 * An action that asks once more, in place (ADR 0039 point 4): the button becomes "Confirm …" and "Cancel". `danger` marks
 * an action that takes something away.
 */
export function ConfirmAction({
  label,
  confirmLabel,
  onConfirm,
  busy = false,
  danger = false,
}: {
  label: string;
  confirmLabel: string;
  onConfirm: () => void;
  busy?: boolean;
  danger?: boolean;
}) {
  const [asking, setAsking] = useState(false);
  const style = danger ? buttons.danger : buttons.secondary;

  if (!asking) {
    return (
      <button type="button" className={style} onClick={() => setAsking(true)} disabled={busy}>
        {label}
      </button>
    );
  }

  return (
    <>
      <button
        type="button"
        className={style}
        disabled={busy}
        onClick={() => {
          setAsking(false);
          onConfirm();
        }}
      >
        {confirmLabel}
      </button>
      <button type="button" className={buttons.secondary} onClick={() => setAsking(false)}>
        Cancel
      </button>
    </>
  );
}
