import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState, type ReactNode } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import { ConfirmAction } from '../components/ConfirmAction';
import { changesBetween, type Change } from '../components/conflict';
import { ConflictNotice } from '../components/ConflictNotice';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { billKeys, billsChanged, comparedBillFields, getBill, postBill, voidBill, type Bill } from './api';

type Action = { kind: 'post' } | { kind: 'void'; reason: string };

const verbs: Record<Action['kind'], string> = { post: 'post', void: 'void' };

/**
 * Post and void on a draft's page (US-21, ADR 0040 points 2 and 6): each a financial request with its own
 * `Idempotency-Key`, sent again unchanged when the user retries after no answer, a fault or "still running"; posting asks
 * once more and answers posted or sent for approval; voiding needs a reason; a conflict reads the bill again and says
 * what changed. The page places `actions` among its actions and `notices` where it shows messages.
 */
export function useBillActions(bill: Bill): { actions: ReactNode; notices: ReactNode } {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const send = useIdempotentRequest();
  const reasonId = useId();
  const [voiding, setVoiding] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<string | null>(null);
  const [problem, setProblem] = useState<{ problem: ApiProblem; retry: Action | null } | null>(null);
  const [conflict, setConflict] = useState<{ changes: Change[]; verb: string } | null>(null);

  const showBill = (changed: Bill) => {
    queryClient.setQueryData(billKeys.one(changed.id), changed);
    billsChanged(queryClient);
  };

  const act = useMutation({
    mutationFn: async (action: Action) => {
      if (action.kind === 'post') {
        const identity = `POST /bills/${bill.id}/post ${bill.version}`;
        return send(identity, (key) => postBill(client, bill, key));
      }

      const identity = `POST /bills/${bill.id}/void ${bill.version} ${JSON.stringify(action.reason)}`;
      return { data: await send(identity, (key) => voidBill(client, bill, action.reason, key)), status: 200 };
    },
    onMutate: () => {
      setOutcome(null);
      setProblem(null);
      setConflict(null);
    },
    onSuccess: ({ data, status }, action) => {
      setVoiding(false);
      setOutcome(
        action.kind === 'void'
          ? 'The draft is voided.'
          : status === 202
            ? 'Sent for approval: the bill waits for a Supervisor or an administrator, who posts it (BR-207).'
            : `Posted as ${data.number ?? 'a bill'}.`,
      );
      showBill(data);
    },
    onError: async (error, action) => {
      if (isConflict(error)) {
        try {
          const latest = await getBill(client, bill.id);
          setConflict({ changes: changesBetween(bill, latest, comparedBillFields), verb: verbs[action.kind] });
          showBill(latest);
        } catch (reloadError) {
          setProblem({ problem: problemOf(reloadError), retry: null });
        }

        return;
      }

      setProblem({ problem: problemOf(error), retry: isRetryable(error) ? action : null });
    },
  });

  const confirmVoid = () => {
    const text = reason.trim();
    if (text === '') {
      setReasonError('Give the reason for voiding the draft.');
      return;
    }

    if (text.length > 500) {
      setReasonError('The reason is at most 500 characters.');
      return;
    }

    setReasonError(null);
    act.mutate({ kind: 'void', reason: text });
  };

  const actions =
    bill.status !== 'Draft' ? null : voiding ? null : (
      <>
        <ConfirmAction
          label="Post bill"
          confirmLabel="Confirm: post the bill"
          busy={act.isPending}
          onConfirm={() => act.mutate({ kind: 'post' })}
        />
        <button
          type="button"
          className={buttons.danger}
          disabled={act.isPending}
          aria-busy={act.isPending}
          onClick={() => setVoiding(true)}
        >
          Void draft
        </button>
      </>
    );

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
          title={`Someone else changed this bill before you could ${conflict.verb} it. Nothing has been changed; it is shown as it is now.`}
        />
      )}
      {voiding && bill.status === 'Draft' && (
        <div className={forms.grid}>
          <div className={`${fieldStyles.field} ${forms.wide}`}>
            <label className={fieldStyles.label} htmlFor={reasonId}>
              Why is the draft voided?
            </label>
            <textarea
              id={reasonId}
              className={fieldStyles.input}
              rows={2}
              value={reason}
              aria-invalid={reasonError !== null}
              aria-describedby={reasonError === null ? undefined : `${reasonId}-error`}
              onChange={(event) => setReason(event.target.value)}
            />
            {reasonError !== null && (
              <p id={`${reasonId}-error`} className={fieldStyles.error}>
                {reasonError}
              </p>
            )}
          </div>
          <div className={forms.actions}>
            <button
              type="button"
              className={buttons.danger}
              disabled={act.isPending}
              aria-busy={act.isPending}
              onClick={confirmVoid}
            >
              Confirm: void the draft
            </button>
            <button type="button" className={buttons.secondary} onClick={() => setVoiding(false)}>
              Cancel
            </button>
          </div>
        </div>
      )}
    </>
  );

  return { actions, notices };
}
