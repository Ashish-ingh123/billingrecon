import buttons from './buttons.module.css';
import type { Change } from './conflict';
import styles from './ConflictNotice.module.css';

const emptyValue = '(empty)';

/**
 * What a screen says when someone else changed the record first (409 `concurrency_conflict`, ADR 0039 point 7): that
 * nothing was saved, what changed, and, for a form, the user's two choices.
 */
export function ConflictNotice({
  changes,
  title = 'Someone else changed this record while you were editing it. Your changes have not been saved.',
  onLoadLatest,
  onKeepMine,
}: {
  changes: readonly Change[];
  /** What was not done; a form's message unless an action says its own. */
  title?: string;
  onLoadLatest?: () => void;
  onKeepMine?: () => void;
}) {
  return (
    <div role="alert" className={styles.notice}>
      <p className={styles.title}>{title}</p>
      {changes.length === 0 ? (
        <p>Nothing shown here differs; it changed in another way. Check it and try again.</p>
      ) : (
        <>
          <p>What it says now:</p>
          <ul className={styles.changes}>
            {changes.map((change) => (
              <li key={change.label}>
                <strong>{change.label}</strong>: was {change.before || emptyValue}, now {change.now || emptyValue}
              </li>
            ))}
          </ul>
        </>
      )}
      {(onLoadLatest !== undefined || onKeepMine !== undefined) && (
        <div className={styles.actions}>
          {onLoadLatest !== undefined && (
            <button type="button" className={buttons.secondary} onClick={onLoadLatest}>
              Load the latest version
            </button>
          )}
          {onKeepMine !== undefined && (
            <button type="button" className={buttons.secondary} onClick={onKeepMine}>
              Keep my changes
            </button>
          )}
        </div>
      )}
    </div>
  );
}
