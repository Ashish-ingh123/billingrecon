import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { useForm, type Path, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import { useBill, type Bill } from '../bills/api';
import { billTotals, lineAmounts, type LineAmounts } from '../bills/lineMath';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { ReadOnlyField, TextField } from '../components/Field';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { formatDate, formatDecimal, todayInIndia } from '../format';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import {
  comparedCreditNoteFields,
  correctHeader,
  createCreditNote,
  creditNoteKeys,
  creditNotesChanged,
  creditNoteValues,
  getCreditNote,
  headerChanged,
  headerFields,
  lineField,
  linesChanged,
  newCreditNoteRequest,
  replaceLines,
  useCreditNote,
  type CreditNote,
  type CreditNoteValues,
} from './api';
import { CreditLinesEditor } from './CreditLinesEditor';

const retryNote = 'Nothing is done twice: saving again with nothing changed finishes the same request.';

/** The bill statuses a credit note may be drafted against: posted, whatever has been paid since. */
const postedStatuses: readonly Bill['status'][] = ['Posted', 'PartiallyPaid', 'Settled'];

function editorField(apiField: string): Path<CreditNoteValues> | null {
  return (headerFields as readonly string[]).includes(apiField)
    ? (apiField as Path<CreditNoteValues>)
    : lineField(apiField);
}

/** The total of the lines the editor holds is not more than the bill (BR-210, for this credit note alone). */
function totalWithinBill(values: CreditNoteValues, bill: Bill): string | null {
  const lines = values.lines.map((line) => lineAmounts(line)).filter((line): line is LineAmounts => line !== null);
  return billTotals(lines).total <= paiseOfServer(bill.total)
    ? null
    : `The credit note comes to more than the bill, ₹${formatDecimal(bill.total)}.`;
}

function HeaderFields({ form, bill }: { form: UseFormReturn<CreditNoteValues>; bill: Bill }) {
  const {
    register,
    formState: { errors },
  } = form;
  const payer = usePayer(bill.payerId);

  return (
    <>
      <ReadOnlyField
        label="Bill"
        value={<Link to={`/bills/${bill.id}`}>{bill.number}</Link>}
        hint={`Of ${formatDate(bill.billDate)}, ₹${formatDecimal(bill.total)}. The bill never changes.`}
      />
      <ReadOnlyField label="Payer" value={payer.data?.name ?? '…'} />
      <TextField
        id="credit-note-date"
        label="Credit note date"
        type="date"
        min={bill.billDate}
        max={todayInIndia()}
        hint="Not before the bill date, and within the hospital's back-dating limit."
        error={errors.creditNoteDate?.message}
        {...register('creditNoteDate', { validate: (value) => value !== '' || 'Enter the credit note date.' })}
      />
      <div className={`${fieldStyles.field} ${forms.wide}`}>
        <label className={fieldStyles.label} htmlFor="credit-note-reason">
          Reason
        </label>
        <textarea
          id="credit-note-reason"
          className={fieldStyles.input}
          rows={2}
          aria-invalid={errors.reason !== undefined}
          {...register('reason', {
            validate: (value) =>
              value.trim() === ''
                ? 'Give the reason for the credit note.'
                : value.length <= 500 || 'The reason is at most 500 characters.',
          })}
        />
        {errors.reason?.message !== undefined && <p className={fieldStyles.error}>{errors.reason.message}</p>}
      </div>
    </>
  );
}

/** Drafts a credit note against a posted bill (US-24), from the bill's page: `?billId=`. */
export function NewCreditNotePage() {
  const [params] = useSearchParams();
  const billId = params.get('billId');
  const bill = useBill(billId ?? '', billId !== null);

  if (billId === null) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="New credit note" back={{ to: '/credit-notes', label: 'Credit notes' }} />
        <p className={forms.note}>A credit note is drafted from the page of the posted bill it corrects.</p>
      </section>
    );
  }

  if (bill.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="New credit note" back={{ to: '/credit-notes', label: 'Credit notes' }} />
        <LoadState error={bill.error} what="the bill" />
      </section>
    );
  }

  return postedStatuses.includes(bill.data.status) ? (
    <NewCreditNoteForm bill={bill.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="New credit note" back={{ to: `/bills/${bill.data.id}`, label: 'Bill' }} />
      <p className={forms.note}>A credit note corrects a posted bill; this bill is not posted.</p>
    </section>
  );
}

function NewCreditNoteForm({ bill }: { bill: Bill }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const [retryable, setRetryable] = useState(false);
  const [totalError, setTotalError] = useState<string | null>(null);
  const form = useForm<CreditNoteValues>({
    defaultValues: { creditNoteDate: todayInIndia(), reason: '', lines: [] },
  });
  const outcome = useRecordForm<CreditNote, CreditNoteValues>({
    form,
    fields: [...headerFields],
    mapField: editorField,
  });

  const creating = useMutation({
    mutationFn: (body: ReturnType<typeof newCreditNoteRequest>) =>
      send(`POST /credit-notes ${JSON.stringify(body)}`, (key) => createCreditNote(client, body, key)),
    onMutate: () => {
      outcome.clear();
      setRetryable(false);
    },
    onSuccess: (creditNote) => {
      queryClient.setQueryData(creditNoteKeys.one(creditNote.id), creditNote);
      creditNotesChanged(queryClient);
      void navigate(`/credit-notes/${creditNote.id}`);
    },
    onError: async (error) => {
      setRetryable(isRetryable(error));
      await outcome.failed(error);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Credit note for ${bill.number ?? 'the bill'}`}
        back={{ to: `/bills/${bill.id}`, label: bill.number ?? 'Bill' }}
      />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => {
            const tooMuch = totalWithinBill(values, bill);
            setTotalError(tooMuch);
            if (tooMuch === null) {
              creating.mutate(newCreditNoteRequest(bill.id, values));
            }
          })(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={outcome.placed} />}
        {retryable && <p className={forms.note}>{retryNote}</p>}
        {totalError !== null && (
          <p className={fieldStyles.error} role="alert">
            {totalError}
          </p>
        )}
        <div className={forms.grid}>
          <HeaderFields form={form} bill={bill} />
        </div>
        <CreditLinesEditor form={form} bill={bill} />
        <p className={forms.note}>
          A credit note needs a second person: once drafted, it is submitted, and a Supervisor or an administrator posts
          it.
        </p>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={creating.isPending}>
            {creating.isPending ? 'Saving…' : 'Save draft'}
          </button>
          <Link to={`/bills/${bill.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a draft credit note: its date and reason, then its lines, each sent only when it changed (ADR 0040 point 1). */
export function EditCreditNotePage() {
  const { id = '' } = useParams();
  const creditNote = useCreditNote(id);
  const bill = useBill(creditNote.data?.billId ?? '', creditNote.data !== undefined);

  if (creditNote.data === undefined || (creditNote.data.status === 'Draft' && bill.data === undefined)) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Edit credit note" back={{ to: `/credit-notes/${id}`, label: 'Credit note' }} />
        <LoadState error={creditNote.error ?? bill.error} what="the credit note" />
      </section>
    );
  }

  return creditNote.data.status === 'Draft' && bill.data !== undefined ? (
    <EditCreditNoteForm key={creditNote.data.id} initial={creditNote.data} bill={bill.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit credit note" back={{ to: `/credit-notes/${id}`, label: 'Credit note' }} />
      <p className={forms.note}>Only a draft credit note can be changed; a posted one is never changed.</p>
    </section>
  );
}

function EditCreditNoteForm({ initial, bill }: { initial: CreditNote; bill: Bill }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<CreditNoteValues>({ defaultValues: creditNoteValues(initial) });
  const outcome = useRecordForm<CreditNote, CreditNoteValues>({
    form,
    fields: [...headerFields],
    initial,
    compared: comparedCreditNoteFields,
    valuesOf: creditNoteValues,
    reload: () => getCreditNote(client, initial.id),
    mapField: editorField,
  });
  const [headerSaved, setHeaderSaved] = useState(false);
  const [totalError, setTotalError] = useState<string | null>(null);
  const savedThisAttempt = useRef(false);
  const base = outcome.base ?? initial;

  const saving = useMutation({
    mutationFn: async (values: CreditNoteValues) => {
      savedThisAttempt.current = false;
      let current = base;
      if (headerChanged(values, current)) {
        current = await correctHeader(client, current, values);
        savedThisAttempt.current = true;
        outcome.rebase(current);
        queryClient.setQueryData(creditNoteKeys.one(current.id), current);
      }

      if (linesChanged(values, current)) {
        current = await replaceLines(client, current, values);
      }

      return current;
    },
    onMutate: () => {
      outcome.clear();
      setHeaderSaved(false);
    },
    onSuccess: (creditNote) => {
      queryClient.setQueryData(creditNoteKeys.one(creditNote.id), creditNote);
      creditNotesChanged(queryClient);
      void navigate(`/credit-notes/${creditNote.id}`);
    },
    onError: async (error) => {
      setHeaderSaved(savedThisAttempt.current);
      if (savedThisAttempt.current) {
        creditNotesChanged(queryClient);
      }

      await outcome.failed(error);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Edit draft credit note"
        back={{ to: `/credit-notes/${initial.id}`, label: 'Draft credit note' }}
      />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => {
            const tooMuch = totalWithinBill(values, bill);
            setTotalError(tooMuch);
            if (tooMuch === null) {
              saving.mutate(values);
            }
          })(event)
        }
      >
        {headerSaved && (
          <p className={forms.note} role="status">
            The date and the reason were saved; the lines were not. Correct them and save again.
          </p>
        )}
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={outcome.placed} />}
        {outcome.conflict !== null && (
          <ConflictNotice
            changes={outcome.conflict.changes}
            onLoadLatest={outcome.loadLatest}
            onKeepMine={outcome.keepMine}
          />
        )}
        {outcome.kept && (
          <p className={forms.note} role="status">
            Your changes are kept. Saving now replaces the latest version.
          </p>
        )}
        {totalError !== null && (
          <p className={fieldStyles.error} role="alert">
            {totalError}
          </p>
        )}
        <div className={forms.grid}>
          <HeaderFields form={form} bill={bill} />
        </div>
        <CreditLinesEditor form={form} bill={bill} />
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending}>
            {saving.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/credit-notes/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
