import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { useNavigate } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useIdempotentRequest } from '../api/useIdempotentRequest';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { Money } from '../components/Money';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import {
  checkOpeningBatch,
  fileLimits,
  importKeys,
  submitOpeningBatch,
  tooLarge,
  type ChosenFile,
  type OpeningBody,
} from './api';
import { FileField } from './FileField';
import { Findings } from './Findings';
import styles from './Imports.module.css';

const count = (value: number | string) => Number(value).toLocaleString('en-IN');

/**
 * Step two of bringing in a hospital's records (Prompt 10.3b, ADR 0053): what its payers owed and held at the close of
 * the day before the cut-over. The files are checked against the hospital's patients and against the trial balance; a
 * batch with no rejected row is submitted, and posts nothing until a second person approves it in Approvals.
 */
export function OpeningBalancesPage() {
  const client = useApiClient();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const sendOnce = useIdempotentRequest();
  const dateId = useId();
  const commentId = useId();
  const [cutOverDate, setCutOverDate] = useState('');
  const [bills, setBills] = useState<ChosenFile | null>(null);
  const [advances, setAdvances] = useState<ChosenFile | null>(null);
  const [trialBalance, setTrialBalance] = useState<ChosenFile | null>(null);
  const [comment, setComment] = useState('');
  const [refusal, setRefusal] = useState<string | null>(null);

  const check = useMutation({ mutationFn: (body: OpeningBody) => checkOpeningBatch(client, body) });
  const submit = useMutation({
    mutationFn: (body: OpeningBody) => sendOnce(JSON.stringify(body), (key) => submitOpeningBatch(client, body, key)),
    onSuccess: async (batch) => {
      await queryClient.invalidateQueries({ queryKey: importKeys.batches });
      await navigate(`/administration/import/batches/${batch.id}`);
    },
  });

  const changed = () => {
    setRefusal(null);
    check.reset();
    submit.reset();
  };
  const choose = (set: (file: ChosenFile | null) => void) => (file: ChosenFile | null) => {
    set(file);
    changed();
  };

  const body = (): OpeningBody => ({
    cutOverDate,
    billsCsv: bills?.text ?? null,
    advancesCsv: advances?.text ?? null,
    trialBalanceCsv: trialBalance?.text ?? null,
    comment: comment.trim() === '' ? null : comment.trim(),
  });

  const sendable = (): OpeningBody | null => {
    const next = body();
    const large = tooLarge(next, [
      [bills, fileLimits.opening],
      [advances, fileLimits.opening],
      [trialBalance, fileLimits.opening],
    ]);
    setRefusal(large);
    return large === null ? next : null;
  };

  const ready = cutOverDate !== '' && trialBalance !== null && (bills !== null || advances !== null);
  const found = check.data;
  const accepted = found?.accepted === true;
  const pending = check.isPending || submit.isPending;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Import opening balances"
        back={{ to: '/administration/import', label: 'Import' }}
        subtitle="Step 2 of 2. What each payer owed and held when the old system closed, the day before the cut-over."
      />
      <AdministrationTabs />

      <form
        className={forms.form}
        onSubmit={(event) => {
          event.preventDefault();
          const next = sendable();
          if (next !== null) {
            check.mutate(next);
          }
        }}
      >
        <div className={forms.grid}>
          <TextField
            id={dateId}
            type="date"
            label="Cut-over date"
            hint="The first day documents are raised here. The balances are as at the day before."
            requiredMark
            value={cutOverDate}
            onChange={(event) => {
              setCutOverDate(event.target.value);
              changed();
            }}
          />
          <FileField
            label="Trial balance (trial_balance.csv)"
            hint="Columns account (Receivable, Advance, Credit) and amount. The files' totals must equal it to the paisa."
            required
            onChange={choose(setTrialBalance)}
          />
          <FileField
            label="Unpaid bills (bills.csv)"
            hint="Columns legacy_number, mrn, bill_date, outstanding_amount; optionally guarantor_ref and due_date."
            onChange={choose(setBills)}
          />
          <FileField
            label="Advances and credit balances (advances.csv)"
            hint="Columns legacy_reference, received_date, amount, method, and mrn or guarantor_ref; optionally kind."
            onChange={choose(setAdvances)}
          />
        </div>
        {refusal !== null && (
          <Banner tone="danger" title="Too large to send" live>
            {refusal}
          </Banner>
        )}
        {check.error !== null && <ProblemAlert problem={problemOf(check.error)} />}
        <div className={forms.actions}>
          <button type="submit" className={buttons.secondary} disabled={!ready || pending}>
            {check.isPending ? 'Checking…' : 'Check the files'}
          </button>
        </div>
      </form>

      <div className={styles.result} aria-live="polite">
        {found !== undefined && (
          <>
            {accepted ? (
              <Banner tone="success" title="The files agree with the trial balance" live>
                Nothing was written yet. Submit the batch, and a second person approves it before anything is posted.
              </Banner>
            ) : (
              <Banner tone="danger" title="The files cannot be submitted as they are" live>
                Every row below must be put right first, in the files.
              </Banner>
            )}
            <dl className={styles.figures}>
              <div className={styles.tile}>
                <dt>Opening receivables</dt>
                <dd>{count(found.receivables)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Owed in all</dt>
                <dd>
                  <Money value={found.receivableTotal} />
                </dd>
              </div>
              <div className={styles.tile}>
                <dt>Advances and credit balances</dt>
                <dd>{count(found.advances)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Held in all</dt>
                <dd>
                  <Money value={found.advanceTotal} />
                </dd>
              </div>
            </dl>
            {found.rejected.length > 0 && (
              <Findings findings={found.rejected} fileName="opening-balances-findings.csv" />
            )}
            {accepted && (
              <form
                className={forms.form}
                onSubmit={(event) => {
                  event.preventDefault();
                  const next = sendable();
                  if (next !== null) {
                    submit.mutate(next);
                  }
                }}
              >
                <TextField
                  id={commentId}
                  label="Note for the person who approves"
                  hint="Optional. For example, which closing trial balance the totals were agreed with."
                  maxLength={500}
                  value={comment}
                  onChange={(event) => setComment(event.target.value)}
                />
                {submit.error !== null && <ProblemAlert problem={problemOf(submit.error)} />}
                <div className={forms.actions}>
                  <button type="submit" className={buttons.primary} disabled={pending}>
                    {submit.isPending ? 'Submitting…' : 'Submit for approval'}
                  </button>
                </div>
              </form>
            )}
          </>
        )}
      </div>
    </section>
  );
}
