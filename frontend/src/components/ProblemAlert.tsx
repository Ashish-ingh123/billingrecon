import type { ApiProblem } from '../api/problem';
import styles from './ProblemAlert.module.css';

function waitText(seconds: number): string {
  if (seconds < 60) {
    return `Try again in ${seconds} ${seconds === 1 ? 'second' : 'seconds'}.`;
  }

  const minutes = Math.ceil(seconds / 60);
  return `Try again in ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}.`;
}

/**
 * The one way a screen shows what the API refused or failed to do (ADR 0038): the API's message, the messages of fields
 * the screen has not placed on its own fields, how long to wait after a 429, and the reference to quote to support.
 * `placedFields` are the fields whose messages the screen shows beside them.
 */
export function ProblemAlert({
  problem,
  placedFields = [],
}: {
  problem: ApiProblem;
  placedFields?: readonly string[];
}) {
  const others = Object.entries(problem.fieldErrors).filter(([field]) => !placedFields.includes(field));

  return (
    <div role="alert" className={styles.alert} data-error-code={problem.errorCode ?? undefined}>
      <p className={styles.title}>{problem.detail ?? problem.title}</p>
      {others.length > 0 && (
        <ul className={styles.fields}>
          {others.flatMap(([field, messages]) =>
            messages.map((message) => <li key={`${field}:${message}`}>{message}</li>),
          )}
        </ul>
      )}
      {problem.retryAfterSeconds !== null && <p className={styles.note}>{waitText(problem.retryAfterSeconds)}</p>}
      {problem.correlationId !== null && (
        <p className={styles.reference}>
          Reference for support: <code>{problem.correlationId}</code>
        </p>
      )}
    </div>
  );
}
