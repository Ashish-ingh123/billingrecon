import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm, useWatch, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import fieldStyles from '../components/Field.module.css';
import { ReadOnlyField, SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { useCreditNote } from '../creditNotes/api';
import { formatDate, formatDecimal, todayInIndia } from '../format';
import { checkAmount, paiseOfServer } from '../money';
import { checkReference, methodLabels, paymentMethods, usePaymentWithAllocations } from '../payments/api';
import { usePayer } from '../payers/api';
import {
  comparedRefundFields,
  correctRefund,
  draftRefund,
  draftRefundRequest,
  getRefund,
  refundFields,
  refundKeys,
  refundsChanged,
  refundValues,
  useRefund,
  type Refund,
  type RefundSource,
  type RefundValues,
} from './api';

/** What a refund is paid from, as the form shows it: the document, its date, its payer, and what it still holds. */
interface SourceFacts {
  source: RefundSource;
  label: string;
  date: string;
  payerId: string;
  /** What the source holds now, the cap of the refund (BR-307). */
  available: number | string;
  link: string;
}

function RefundFields({ form, facts }: { form: UseFormReturn<RefundValues>; facts: SourceFacts }) {
  const {
    register,
    control,
    formState: { errors },
  } = form;
  const method = useWatch({ control, name: 'method' });
  const cap = paiseOfServer(facts.available);

  return (
    <>
      <TextField
        id="refund-date"
        label="Refund date"
        type="date"
        min={facts.date}
        max={todayInIndia()}
        hint={`Not before the date of the source, ${formatDate(facts.date)}.`}
        error={errors.refundDate?.message}
        {...register('refundDate', { validate: (value) => value !== '' || 'Enter the refund date.' })}
      />
      <TextField
        id="refund-amount"
        label="Amount (₹)"
        inputMode="decimal"
        hint={`At most ₹${formatDecimal(facts.available)}, what the source holds now.`}
        error={errors.amount?.message}
        {...register('amount', {
          validate: (value) => checkAmount(value, cap, `what the source holds, ₹${formatDecimal(facts.available)}`),
        })}
      />
      <SelectField
        id="refund-method"
        label="Method"
        error={errors.method?.message}
        {...register('method', { validate: (value) => value !== '' || 'Choose the method.' })}
      >
        <option value="">Choose…</option>
        {paymentMethods.map((value) => (
          <option key={value} value={value}>
            {methodLabels[value]}
          </option>
        ))}
      </SelectField>
      <TextField
        id="refund-reference"
        label="Reference"
        hint={
          method === 'Cash'
            ? 'Optional for cash.'
            : 'The UPI id, card authorisation, UTR or cheque number of the payout.'
        }
        error={errors.reference?.message}
        {...register('reference', { validate: (value, values) => checkReference(value, values.method) })}
      />
      <div className={`${fieldStyles.field} ${forms.wide}`}>
        <label className={fieldStyles.label} htmlFor="refund-reason">
          Reason
        </label>
        <textarea
          id="refund-reason"
          className={fieldStyles.input}
          rows={2}
          aria-invalid={errors.reason !== undefined}
          {...register('reason', {
            validate: (value) =>
              value.trim() === ''
                ? 'Give the reason for the refund.'
                : value.length <= 500 || 'The reason is at most 500 characters.',
          })}
        />
        {errors.reason?.message !== undefined && <p className={fieldStyles.error}>{errors.reason.message}</p>}
      </div>
    </>
  );
}

function SourceSummary({ facts }: { facts: SourceFacts }) {
  const payer = usePayer(facts.payerId);

  return (
    <>
      <ReadOnlyField label="From" value={<Link to={facts.link}>{facts.label}</Link>} hint="The source never changes." />
      <ReadOnlyField label="Payer" value={payer.data?.name ?? '…'} />
      <ReadOnlyField label="It holds" value={`₹${formatDecimal(facts.available)}`} />
    </>
  );
}

/** Drafts a refund from its source (US-34, ADR 0041 point 2): `?paymentId=` or `?creditNoteId=`. */
export function NewRefundPage() {
  const [params] = useSearchParams();
  const paymentId = params.get('paymentId');
  const creditNoteId = params.get('creditNoteId');

  if (paymentId !== null) {
    return <FromPayment id={paymentId} />;
  }

  if (creditNoteId !== null) {
    return <FromCreditNote id={creditNoteId} />;
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New refund" back={{ to: '/refunds', label: 'Refunds' }} />
      <p className={forms.note}>A refund is drafted from a payment&apos;s page or from the payer&apos;s statement.</p>
    </section>
  );
}

function FromPayment({ id }: { id: string }) {
  const payment = usePaymentWithAllocations(id);
  if (payment.data === undefined) {
    return <Loading error={payment.error} />;
  }

  const { payment: source } = payment.data;
  return (
    <NewRefundForm
      facts={{
        source: { kind: 'Payment', id: source.id },
        label: `Payment ${source.number}`,
        date: source.paymentDate,
        payerId: source.payerId,
        available: source.unappliedAmount,
        link: `/payments/${source.id}`,
      }}
    />
  );
}

function FromCreditNote({ id }: { id: string }) {
  const creditNote = useCreditNote(id);
  if (creditNote.data === undefined) {
    return <Loading error={creditNote.error} />;
  }

  const source = creditNote.data;
  return (
    <NewRefundForm
      facts={{
        source: { kind: 'CreditNote', id: source.id },
        label: `Credit note ${source.number ?? ''}`.trim(),
        date: source.creditNoteDate,
        payerId: source.payerId,
        available: source.availableAmount,
        link: `/credit-notes/${source.id}`,
      }}
    />
  );
}

function Loading({ error }: { error: unknown }) {
  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New refund" back={{ to: '/refunds', label: 'Refunds' }} />
      <LoadState error={error} what="the source of the refund" />
    </section>
  );
}

function NewRefundForm({ facts }: { facts: SourceFacts }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const [retryable, setRetryable] = useState(false);
  const form = useForm<RefundValues>({
    defaultValues: { refundDate: todayInIndia(), amount: '', method: '', reference: '', reason: '' },
  });
  const outcome = useRecordForm<Refund, RefundValues>({ form, fields: [...refundFields] });

  const drafting = useMutation({
    mutationFn: (body: ReturnType<typeof draftRefundRequest>) =>
      send(`POST /refunds ${JSON.stringify(body)}`, (key) => draftRefund(client, body, key)),
    onMutate: () => {
      outcome.clear();
      setRetryable(false);
    },
    onSuccess: (refund) => {
      queryClient.setQueryData(refundKeys.one(refund.id), refund);
      refundsChanged(queryClient);
      void navigate(`/refunds/${refund.id}`);
    },
    onError: async (error) => {
      setRetryable(isRetryable(error));
      await outcome.failed(error);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="Draft a refund" back={{ to: facts.link, label: facts.label }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => drafting.mutate(draftRefundRequest(facts.source, values)))(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={outcome.placed} />}
        {retryable && (
          <p className={forms.note}>
            Nothing is done twice: saving again with nothing changed finishes the same request.
          </p>
        )}
        <div className={forms.grid}>
          <SourceSummary facts={facts} />
          <RefundFields form={form} facts={facts} />
        </div>
        <p className={forms.note}>
          A refund needs a second person: once drafted, it is submitted, and a Supervisor or an administrator posts it.
        </p>
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={drafting.isPending}
            aria-busy={drafting.isPending}
          >
            {drafting.isPending ? 'Saving…' : 'Save draft'}
          </button>
          <Link to={facts.link}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a draft refund: every field but its source, with the version it started from (ADR 0039). */
export function EditRefundPage() {
  const { id = '' } = useParams();
  const refund = useRefund(id);

  if (refund.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Edit refund" back={{ to: `/refunds/${id}`, label: 'Refund' }} />
        <LoadState error={refund.error} what="the refund" />
      </section>
    );
  }

  return refund.data.status === 'Draft' ? (
    <EditRefundSource key={refund.data.id} refund={refund.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit refund" back={{ to: `/refunds/${id}`, label: 'Refund' }} />
      <p className={forms.note}>Only a draft refund can be changed; a posted refund is never changed (BR-308).</p>
    </section>
  );
}

function EditRefundSource({ refund }: { refund: Refund }) {
  const fromPayment = refund.sourceKind === 'Payment';
  const payment = usePaymentWithAllocations(refund.paymentId ?? '', fromPayment);
  const creditNote = useCreditNote(refund.creditNoteId ?? '', !fromPayment);
  const facts: SourceFacts | null = fromPayment
    ? payment.data === undefined
      ? null
      : {
          source: { kind: 'Payment', id: payment.data.payment.id },
          label: `Payment ${payment.data.payment.number}`,
          date: payment.data.payment.paymentDate,
          payerId: refund.payerId,
          available: payment.data.payment.unappliedAmount,
          link: `/payments/${payment.data.payment.id}`,
        }
    : creditNote.data === undefined
      ? null
      : {
          source: { kind: 'CreditNote', id: creditNote.data.id },
          label: `Credit note ${creditNote.data.number ?? ''}`.trim(),
          date: creditNote.data.creditNoteDate,
          payerId: refund.payerId,
          available: creditNote.data.availableAmount,
          link: `/credit-notes/${creditNote.data.id}`,
        };

  return facts === null ? (
    <Loading error={fromPayment ? payment.error : creditNote.error} />
  ) : (
    <EditRefundForm initial={refund} facts={facts} />
  );
}

function EditRefundForm({ initial, facts }: { initial: Refund; facts: SourceFacts }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<RefundValues>({ defaultValues: refundValues(initial) });
  const outcome = useRecordForm<Refund, RefundValues>({
    form,
    fields: [...refundFields],
    initial,
    compared: comparedRefundFields,
    valuesOf: refundValues,
    reload: () => getRefund(client, initial.id),
  });
  const base = outcome.base ?? initial;

  const correcting = useMutation({
    mutationFn: (values: RefundValues) => correctRefund(client, base, values),
    onMutate: outcome.clear,
    onSuccess: (refund) => {
      queryClient.setQueryData(refundKeys.one(refund.id), refund);
      refundsChanged(queryClient);
      void navigate(`/refunds/${refund.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit draft refund" back={{ to: `/refunds/${initial.id}`, label: 'Draft refund' }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => correcting.mutate(values))(event)}
      >
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
        <div className={forms.grid}>
          <SourceSummary facts={facts} />
          <RefundFields form={form} facts={facts} />
        </div>
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={correcting.isPending}
            aria-busy={correcting.isPending}
          >
            {correcting.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/refunds/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
