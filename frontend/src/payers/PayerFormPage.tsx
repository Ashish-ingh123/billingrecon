import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { ReadOnlyField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { usePatient, type Patient } from '../patients/api';
import {
  comparedPayerFields,
  correctPayer,
  createPayer,
  getPayer,
  kindLabel,
  payerFields,
  payerKeys,
  payersChanged,
  payerValues,
  usePayer,
  type Payer,
  type PayerValues,
} from './api';

const required = (what: string) => (value: string) => value.trim() !== '' || `Enter the ${what}.`;

/** The fields of a payer a user types: the name and the contact details. */
function PayerFields({ form }: { form: UseFormReturn<PayerValues> }) {
  const {
    register,
    formState: { errors },
  } = form;

  return (
    <>
      <TextField
        id="payer-name"
        label="Name"
        autoComplete="off"
        error={errors.name?.message}
        {...register('name', { validate: required('name') })}
      />
      <TextField id="payer-phone" label="Phone" type="tel" error={errors.phone?.message} {...register('phone')} />
      <TextField
        id="payer-email"
        label="E-mail address"
        type="email"
        error={errors.email?.message}
        {...register('email')}
      />
    </>
  );
}

/**
 * Creates a self-pay payer (US-11): with `?patientId=`, the patient's own payer, named after them unless the user writes
 * another name; without it, a guarantor who pays for others (BR-104).
 */
export function NewPayerPage() {
  const [params] = useSearchParams();
  const patientId = params.get('patientId');

  return patientId === null ? <NewPayerForm patient={null} /> : <NewPatientPayer patientId={patientId} />;
}

function NewPatientPayer({ patientId }: { patientId: string }) {
  const patient = usePatient(patientId);

  return patient.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="New payer" back={{ to: `/patients/${patientId}`, label: 'Patient' }} />
      <LoadState error={patient.error} what="the patient" />
    </section>
  ) : (
    <NewPayerForm patient={patient.data} />
  );
}

function NewPayerForm({ patient }: { patient: Patient | null }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<PayerValues>({ defaultValues: { name: patient?.fullName ?? '', phone: '', email: '' } });
  const outcome = useRecordForm<Payer, PayerValues>({ form, fields: payerFields });
  const back =
    patient === null ? { to: '/payers', label: 'Payers' } : { to: `/patients/${patient.id}`, label: patient.fullName };

  const creating = useMutation({
    mutationFn: (values: PayerValues) => createPayer(client, values, patient?.id ?? null),
    onMutate: outcome.clear,
    onSuccess: (payer) => {
      queryClient.setQueryData(payerKeys.one(payer.id), payer);
      payersChanged(queryClient);

      void navigate(`/payers/${payer.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={patient === null ? 'New guarantor' : `Payer for ${patient.fullName}`} back={back} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => creating.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={payerFields} />}
        <div className={forms.grid}>
          <ReadOnlyField label="Kind" value={kindLabel('SelfPay')} hint="Insurers and corporate payers arrive in R4." />
          <ReadOnlyField
            label="Pays for"
            value={patient === null ? 'Others, as a guarantor' : `${patient.fullName} (MRN ${patient.mrn})`}
            hint="This never changes."
          />
          <PayerFields form={form} />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={creating.isPending}>
            {creating.isPending ? 'Creating…' : 'Create payer'}
          </button>
          <Link to={back.to}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a payer's name and contact details, sent with the version the form started from (ADR 0039). */
export function EditPayerPage() {
  const { id = '' } = useParams();
  const payer = usePayer(id);

  return payer.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit payer" back={{ to: `/payers/${id}`, label: 'Payer' }} />
      <LoadState error={payer.error} what="the payer" />
    </section>
  ) : (
    <EditPayerForm key={payer.data.id} initial={payer.data} />
  );
}

function EditPayerForm({ initial }: { initial: Payer }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<PayerValues>({ defaultValues: payerValues(initial) });
  const outcome = useRecordForm<Payer, PayerValues>({
    form,
    fields: payerFields,
    initial,
    compared: comparedPayerFields,
    valuesOf: payerValues,
    reload: () => getPayer(client, initial.id),
  });

  const correcting = useMutation({
    mutationFn: ({ values, version }: { values: PayerValues; version: string }) =>
      correctPayer(client, initial.id, version, values),
    onMutate: outcome.clear,
    onSuccess: (payer) => {
      queryClient.setQueryData(payerKeys.one(payer.id), payer);
      payersChanged(queryClient);
      void navigate(`/payers/${payer.id}`);
    },
    onError: outcome.failed,
  });

  const base = outcome.base ?? initial;

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Edit ${initial.name}`} back={{ to: `/payers/${initial.id}`, label: initial.name }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => correcting.mutate({ values, version: base.version }))(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={payerFields} />}
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
          <ReadOnlyField label="Kind" value={kindLabel(initial.kind)} />
          <PayerFields form={form} />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={correcting.isPending}>
            {correcting.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/payers/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
