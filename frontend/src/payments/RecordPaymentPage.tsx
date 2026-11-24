import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import { PayerPicker } from '../bills/pickers';
import buttons from '../components/buttons.module.css';
import fieldStyles from '../components/Field.module.css';
import { SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { todayInIndia } from '../format';
import { checkAmount } from '../money';
import { usePayer, type Payer } from '../payers/api';
import {
  checkReference,
  methodLabels,
  paymentFields,
  paymentKeys,
  paymentMethods,
  paymentsChanged,
  recordPayment,
  recordPaymentRequest,
  type Payment,
  type PaymentValues,
} from './api';

/**
 * Records a payment from a payer (US-30, BR-301, BR-302): it is posted at once, with its number. One request with the key
 * of its operation, which a retry of the same request reuses (ADR 0040 point 2). With `?payerId=` the payer is chosen.
 */
export function RecordPaymentPage() {
  const [params] = useSearchParams();
  const payerId = params.get('payerId');

  return payerId === null ? <RecordPaymentForm initialPayer={null} /> : <WithPresetPayer payerId={payerId} />;
}

function WithPresetPayer({ payerId }: { payerId: string }) {
  const preset = usePayer(payerId);

  if (preset.data === undefined && preset.error === null) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Record payment" back={{ to: '/payments', label: 'Payments' }} />
        <p role="status">Loading the payer…</p>
      </section>
    );
  }

  // An inactive payer receives no payment (BR-309): the user chooses another.
  return <RecordPaymentForm initialPayer={preset.data?.status === 'Active' ? preset.data : null} />;
}

function RecordPaymentForm({ initialPayer }: { initialPayer: Payer | null }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const [payer, setPayer] = useState<Payer | null>(initialPayer);
  const [payerError, setPayerError] = useState<string | undefined>(undefined);
  const [retryable, setRetryable] = useState(false);
  const form = useForm<PaymentValues>({
    defaultValues: { paymentDate: todayInIndia(), method: '', reference: '', amount: '', notes: '' },
  });
  const method = useWatch({ control: form.control, name: 'method' });
  const outcome = useRecordForm<Payment, PaymentValues>({ form, fields: [...paymentFields] });
  const {
    register,
    formState: { errors },
  } = form;

  const recording = useMutation({
    mutationFn: (body: ReturnType<typeof recordPaymentRequest>) =>
      send(`POST /payments ${JSON.stringify(body)}`, (key) => recordPayment(client, body, key)),
    onMutate: () => {
      outcome.clear();
      setRetryable(false);
    },
    onSuccess: (payment) => {
      queryClient.removeQueries({ queryKey: paymentKeys.one(payment.id) });
      paymentsChanged(queryClient);
      void navigate(`/payments/${payment.id}`);
    },
    onError: async (error) => {
      setRetryable(isRetryable(error));
      await outcome.failed(error);
    },
  });

  const submit = (event: FormEvent) => {
    setPayerError(payer === null ? 'Choose the payer.' : undefined);
    return form.handleSubmit((values) => {
      if (payer !== null) {
        recording.mutate(recordPaymentRequest(payer.id, values));
      }
    })(event);
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="Record payment" back={{ to: '/payments', label: 'Payments' }} />
      <form className={forms.form} noValidate onSubmit={(event) => void submit(event)}>
        {outcome.problem !== null && (
          <ProblemAlert problem={outcome.problem} placedFields={[...outcome.placed, 'payerId']} />
        )}
        {retryable && (
          <p className={forms.note}>
            Nothing is done twice: recording again with nothing changed finishes the same request.
          </p>
        )}
        <div className={forms.grid}>
          <div className={forms.wide}>
            <PayerPicker
              selected={payer}
              onSelect={(chosen) => {
                setPayer(chosen);
                setPayerError(undefined);
              }}
              error={payerError ?? outcome.problem?.fieldErrors.payerId?.[0]}
            />
          </div>
          <TextField
            id="payment-date"
            label="Payment date"
            requiredMark
            type="date"
            max={todayInIndia()}
            error={errors.paymentDate?.message}
            {...register('paymentDate', { validate: (value) => value !== '' || 'Enter the payment date.' })}
          />
          <SelectField
            id="payment-method"
            label="Method"
            requiredMark
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
            id="payment-reference"
            label="Reference"
            hint={method === 'Cash' ? 'Optional for cash.' : 'The UPI id, card authorisation, UTR or cheque number.'}
            error={errors.reference?.message}
            {...register('reference', { validate: (value, values) => checkReference(value, values.method) })}
          />
          <TextField
            id="payment-amount"
            label="Amount (₹)"
            leading="₹"
            requiredMark
            inputMode="decimal"
            error={errors.amount?.message}
            {...register('amount', {
              validate: (value) => checkAmount(value, 999999999999n, 'the largest payment, ₹9,999,999,999.99'),
            })}
          />
          <div className={`${fieldStyles.field} ${forms.wide}`}>
            <label className={fieldStyles.label} htmlFor="payment-notes">
              Notes
            </label>
            <textarea
              id="payment-notes"
              className={fieldStyles.input}
              rows={2}
              {...register('notes', {
                validate: (value) => value.length <= 500 || 'The notes are at most 500 characters.',
              })}
            />
            {errors.notes?.message !== undefined && <p className={fieldStyles.error}>{errors.notes.message}</p>}
          </div>
        </div>
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={recording.isPending}
            aria-busy={recording.isPending}
          >
            {recording.isPending ? 'Recording…' : 'Record payment'}
          </button>
          <Link to="/payments">Cancel</Link>
        </div>
      </form>
    </section>
  );
}
