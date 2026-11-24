import { ProblemAlert } from './ProblemAlert';
import { problemOf } from '../api/call';
import styles from './Loading.module.css';

/**
 * What a screen shows while it reads, or when the read failed: the API's problem, in the one error component. While it
 * reads (ADR 0048), a skeleton of lines stands where the content will be, busy for assistive technology, and the words
 * "Loading …" are said once, not shown.
 */
export function LoadState({ error, what }: { error: unknown; what: string }) {
  if (error !== null && error !== undefined) {
    return <ProblemAlert problem={problemOf(error)} />;
  }

  return (
    <div className={styles.skeleton} aria-busy="true">
      <p role="status" aria-live="polite" className={styles.said}>
        Loading {what}…
      </p>
      <span className={styles.line} aria-hidden="true" />
      <span className={styles.line} aria-hidden="true" />
      <span className={styles.line} aria-hidden="true" />
    </div>
  );
}
