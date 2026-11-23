import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import { useBill, type Bill } from '../bills/api';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { ReadOnlyField, SelectField, TextField } from '../components/Field';
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
  adjustmentFields,
  adjustmentKeys,
  adjustmentKinds,
  adjustmentsChanged,
  adjustmentValues,
  checkAdjustmentAmount,
  comparedAdjustmentFields,
  correctAdjustment,
  draftAdjustment,
  draftAdjustmentRequest,
  getAdjustment,
  kindLabels,
  useAdjustment,
  type Adjustment,
  type AdjustmentValues,
} from './api';

/** The bill statuses an adjustment may lower: posted and still owing (BR-501). */
const owingStatuses: readonly Bill['status'][] = ['Posted', 'PartiallyPaid'];

function AdjustmentFields({ form, bill }: { form: UseFormReturn<AdjustmentValues>; bill: Bill }) {
  const {
    register,
    formState: { errors },
  } = form;
  const payer = usePayer(bill.payerId);
  const balanceDue = paiseOfServer(bill.balanceDue);

  return (
    <>
      <ReadOnlyField
        label="Bill"
        value={<Link to={`/bills/${bill.id}`}>{bill.number}</Link>}
        hint={`Of ${formatDate(bill.billDate)}. The bill never changes.`}
      />
      <ReadOnlyField label="Payer" value={payer.data?.name ?? '…'} />
      <ReadOnlyField label="Balance due" value={`₹${formatDecimal(bill.balanceDue)}`} />
      <SelectField
        id="adjustment-kind"
        label="Kind"
        hint="A rounding is under one rupee; every kind needs a second person."
        error={errors.kind?.message}
        {...register('kind', { validate: (value) => value !== '' || 'Choose the kind.' })}
      >
        <option value="">Choose…</option>
        {adjustmentKinds.map((value) => (
          <option key={value} value={value}>
            {kindLabels[value]}
          </option>
        ))}
      </SelectField>
      <TextField
        id="adjustment-date"
        label="Adjustment date"
        type="date"
        min={bill.billDate}
        max={todayInIndia()}
        hint="Not before the bill date, and within the hospital's back-dating limit."
        error={errors.adjustmentDate?.message}
        {...register('adjustmentDate', { validate: (value) => value !== '' || 'Enter the adjustment date.' })}
      />
      <TextField
        id="adjustment-amount"
        label="Amount (₹)"
        inputMode="decimal"
        hint={`At most ₹${formatDecimal(bill.balanceDue)}, what the bill still owes.`}
        error={errors.amount?.message}
        {...register('amount', {
          validate: (value, values) =>
            checkAdjustmentAmount(value, values.kind, balanceDue, formatDecimal(bill.balanceDue)),
        })}
      />
      <div className={`${fieldStyles.field} ${forms.wide}`}>
        <label className={fieldStyles.label} htmlFor="adjustment-reason">
          Reason
        </label>
        <textarea
          id="adjustment-reason"
          className={fieldStyles.input}
          rows={2}
          aria-invalid={errors.reason !== undefined}
          {...register('reason', {
            validate: (value) =>
              value.trim() === ''
                ? 'Give the reason for the adjustment.'
                : value.length <= 500 || 'The reason is at most 500 characters.',
          })}
        />
        {errors.reason?.message !== undefined && <p className={fieldStyles.error}>{errors.reason.message}</p>}
      </div>
    </>
  );
}

/** Drafts an adjustment of a posted bill (BR-501 to BR-503), from the bill's page: `?billId=`. */
export function NewAdjustmentPage() {
  const [params] = useSearchParams();
  const billId = params.get('billId');
  const bill = useBill(billId ?? '', billId !== null);
  const back = { to: '/adjustments', label: 'Adjustments' };

  if (billId === null) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="New adjustment" back={back} />
        <p className={forms.note}>An adjustment is drafted from the page of the posted bill it lowers.</p>
      </section>
    );
  }

  if (bill.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="New adjustment" back={back} />
        <LoadState error={bill.error} what="the bill" />
      </section>
    );
  }

  return owingStatuses.includes(bill.data.status) ? (
    <NewAdjustmentForm bill={bill.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="New adjustment" back={{ to: `/bills/${bill.data.id}`, label: 'Bill' }} />
      <p className={forms.note}>An adjustment lowers a posted bill that still owes something; this bill does not.</p>
    </section>
  );
}

function NewAdjustmentForm({ bill }: { bill: Bill }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const [retryable, setRetryable] = useState(false);
  const form = useForm<AdjustmentValues>({
    defaultValues: { adjustmentDate: todayInIndia(), kind: '', amount: '', reason: '' },
  });
  const outcome = useRecordForm<Adjustment, AdjustmentValues>({ form, fields: [...adjustmentFields] });

  const drafting = useMutation({
    mutationFn: (body: ReturnType<typeof draftAdjustmentRequest>) =>
      send(`POST /adjustments ${JSON.stringify(body)}`, (key) => draftAdjustment(client, body, key)),
    onMutate: () => {
      outcome.clear();
      setRetryable(false);
    },
    onSuccess: (adjustment) => {
      queryClient.setQueryData(adjustmentKeys.one(adjustment.id), adjustment);
      adjustmentsChanged(queryClient);
      void navigate(`/adjustments/${adjustment.id}`);
    },
    onError: async (error) => {
      setRetryable(isRetryable(error));
      await outcome.failed(error);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Adjust ${bill.number ?? 'the bill'}`}
        back={{ to: `/bills/${bill.id}`, label: bill.number ?? 'Bill' }}
      />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => drafting.mutate(draftAdjustmentRequest(bill.id, values)))(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={outcome.placed} />}
        {retryable && (
          <p className={forms.note}>
            Nothing is done twice: saving again with nothing changed finishes the same request.
          </p>
        )}
        <div className={forms.grid}>
          <AdjustmentFields form={form} bill={bill} />
        </div>
        <p className={forms.note}>
          Every adjustment needs a second person, a rounding included: once drafted, it is submitted, and a Supervisor
          or an administrator posts it.
        </p>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={drafting.isPending}>
            {drafting.isPending ? 'Saving…' : 'Save draft'}
          </button>
          <Link to={`/bills/${bill.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a draft adjustment as a whole (`PUT`), with the version it started from (ADR 0039). */
export function EditAdjustmentPage() {
  const { id = '' } = useParams();
  const adjustment = useAdjustment(id);
  const bill = useBill(adjustment.data?.billId ?? '', adjustment.data !== undefined);
  const back = { to: `/adjustments/${id}`, label: 'Adjustment' };

  if (adjustment.data === undefined || (adjustment.data.status === 'Draft' && bill.data === undefined)) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Edit adjustment" back={back} />
        <LoadState error={adjustment.error ?? bill.error} what="the adjustment" />
      </section>
    );
  }

  return adjustment.data.status === 'Draft' && bill.data !== undefined ? (
    <EditAdjustmentForm key={adjustment.data.id} initial={adjustment.data} bill={bill.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit adjustment" back={back} />
      <p className={forms.note}>Only a draft adjustment can be changed; a posted one is never changed.</p>
    </section>
  );
}

function EditAdjustmentForm({ initial, bill }: { initial: Adjustment; bill: Bill }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<AdjustmentValues>({ defaultValues: adjustmentValues(initial) });
  const outcome = useRecordForm<Adjustment, AdjustmentValues>({
    form,
    fields: [...adjustmentFields],
    initial,
    compared: comparedAdjustmentFields,
    valuesOf: adjustmentValues,
    reload: () => getAdjustment(client, initial.id),
  });
  const base = outcome.base ?? initial;

  const correcting = useMutation({
    mutationFn: (values: AdjustmentValues) => correctAdjustment(client, base, values),
    onMutate: outcome.clear,
    onSuccess: (adjustment) => {
      queryClient.setQueryData(adjustmentKeys.one(adjustment.id), adjustment);
      adjustmentsChanged(queryClient);
      void navigate(`/adjustments/${adjustment.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Edit draft adjustment"
        back={{ to: `/adjustments/${initial.id}`, label: 'Draft adjustment' }}
      />
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
          <AdjustmentFields form={form} bill={bill} />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={correcting.isPending}>
            {correcting.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/adjustments/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
