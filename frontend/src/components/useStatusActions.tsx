import { useMutation } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import buttons from './buttons.module.css';
import { ConfirmAction } from './ConfirmAction';
import { changesBetween, type Change, type ComparedField } from './conflict';
import { ConflictNotice } from './ConflictNotice';
import { ProblemAlert } from './ProblemAlert';

/**
 * Deactivate and reactivate on a record's page (ADR 0039 points 2, 4 and 7): deactivating asks once more; each sends the
 * version of the record as shown; a conflict reads the record again, says what changed, and changes nothing. The page
 * places `actions` among its actions and `notices` where it shows messages.
 */
export function useStatusActions<T extends { status: 'Active' | 'Inactive' }>({
  record,
  what,
  deactivate,
  reactivate,
  compared,
  reload,
  onChanged,
}: {
  record: T;
  /** The record's kind, as the buttons name it: "patient". */
  what: string;
  deactivate: () => Promise<T>;
  reactivate: () => Promise<T>;
  compared: readonly ComparedField<T>[];
  reload: () => Promise<T>;
  onChanged: (record: T) => void;
}): { actions: ReactNode; notices: ReactNode } {
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [conflict, setConflict] = useState<{ changes: Change[]; action: string } | null>(null);

  const change = useMutation({
    mutationFn: (action: 'deactivate' | 'reactivate') => (action === 'deactivate' ? deactivate() : reactivate()),
    onMutate: () => {
      setProblem(null);
      setConflict(null);
    },
    onSuccess: onChanged,
    onError: async (error, action) => {
      if (!isConflict(error)) {
        setProblem(problemOf(error));
        return;
      }

      try {
        const latest = await reload();
        setConflict({ changes: changesBetween(record, latest, compared), action });
        onChanged(latest);
      } catch (reloadError) {
        setProblem(problemOf(reloadError));
      }
    },
  });

  const actions =
    record.status === 'Active' ? (
      <ConfirmAction
        label={`Deactivate ${what}`}
        confirmLabel={`Confirm: deactivate ${what}`}
        danger
        busy={change.isPending}
        onConfirm={() => change.mutate('deactivate')}
      />
    ) : (
      <button
        type="button"
        className={buttons.secondary}
        disabled={change.isPending}
        onClick={() => change.mutate('reactivate')}
      >
        Reactivate {what}
      </button>
    );

  const notices = (
    <>
      {problem !== null && <ProblemAlert problem={problem} />}
      {conflict !== null && (
        <ConflictNotice
          changes={conflict.changes}
          title={`Someone else changed this record before you could ${conflict.action} it. Nothing has been changed; it is shown as it is now.`}
        />
      )}
    </>
  );

  return { actions, notices };
}
