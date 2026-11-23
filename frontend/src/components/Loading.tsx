import { ProblemAlert } from './ProblemAlert';
import { problemOf } from '../api/call';

/** What a screen shows while it reads, or when the read failed: the API's problem, in the one error component. */
export function LoadState({ error, what }: { error: unknown; what: string }) {
  if (error !== null && error !== undefined) {
    return <ProblemAlert problem={problemOf(error)} />;
  }

  return (
    <p role="status" aria-live="polite">
      Loading {what}…
    </p>
  );
}
