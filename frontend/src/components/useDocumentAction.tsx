import { useMutation } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from './buttons.module.css';
import { changesBetween, type Change, type ComparedField } from './conflict';
import { ConflictNotice } from './ConflictNotice';
import forms from './forms.module.css';
import { ProblemAlert } from './ProblemAlert';

/** What an action sends, and how its outcome reads. */
export interface DocumentAction<D> {
  /** The request as sent (method, path, version, body): the same identity is the same operation (ADR 0040 point 2). */
  identity: string;
  /** What the action does, for a conflict's message: "post", "request the reversal of". */
  verb: string;
  send: (key: string) => Promise<{ data: D; status: number }>;
  /** What to say once it is done. */
  done: (data: D, status: number) => string;
}

/**
 * A financial action on a document (ADR 0040 points 2 and 6, ADR 0041 point 5): sent with the key of its operation;
 * after no answer, a fault or "still running" the user may send it again under the same key; a conflict reads the
 * document again (`reload`, compared as `compared`) and says what changed; any other refusal is shown. `onDone` gets the
 * answer, `onReloaded` the document as it is after a conflict. `notices` is placed where the page shows messages.
 */
export function useDocumentAction<D, R>({
  current,
  compared,
  reload,
  onDone,
  onReloaded,
}: {
  current: R;
  compared: readonly ComparedField<R>[];
  reload: () => Promise<R>;
  onDone: (data: D) => void;
  onReloaded: (record: R) => void;
}): { run: (action: DocumentAction<D>) => void; pending: boolean; notices: ReactNode } {
  const send = useIdempotentRequest();
  const [outcome, setOutcome] = useState<string | null>(null);
  const [problem, setProblem] = useState<{ problem: ApiProblem; retry: DocumentAction<D> | null } | null>(null);
  const [conflict, setConflict] = useState<{ changes: Change[]; verb: string } | null>(null);

  const act = useMutation({
    mutationFn: (action: DocumentAction<D>) => send(action.identity, action.send),
    onMutate: () => {
      setOutcome(null);
      setProblem(null);
      setConflict(null);
    },
    onSuccess: ({ data, status }, action) => {
      setOutcome(action.done(data, status));
      onDone(data);
    },
    onError: async (error, action) => {
      if (!isConflict(error)) {
        setProblem({ problem: problemOf(error), retry: isRetryable(error) ? action : null });
        return;
      }

      try {
        const latest = await reload();
        setConflict({ changes: changesBetween(current, latest, compared), verb: action.verb });
        onReloaded(latest);
      } catch (reloadError) {
        setProblem({ problem: problemOf(reloadError), retry: null });
      }
    },
  });

  const notices = (
    <>
      {outcome !== null && (
        <p className={forms.note} role="status">
          {outcome}
        </p>
      )}
      {problem !== null && (
        <>
          <ProblemAlert problem={problem.problem} />
          {problem.retry !== null && (
            <p className={forms.note}>
              Nothing is done twice: sending it again finishes the same request.{' '}
              <button
                type="button"
                className={buttons.link}
                onClick={() => problem.retry !== null && act.mutate(problem.retry)}
              >
                Send it again
              </button>
            </p>
          )}
        </>
      )}
      {conflict !== null && (
        <ConflictNotice
          changes={conflict.changes}
          title={`Someone else changed this before you could ${conflict.verb} it. Nothing has been changed; it is shown as it is now.`}
        />
      )}
    </>
  );

  return { run: (action) => act.mutate(action), pending: act.isPending, notices };
}
