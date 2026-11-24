import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { ReadOnlyField, SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import {
  comparedPatientFields,
  correctPatient,
  emptyPatient,
  getPatient,
  patientFields,
  patientKeys,
  patientValues,
  registerPatient,
  usePatient,
  type Patient,
  type PatientValues,
} from './api';

const required = (what: string) => (value: string) => value.trim() !== '' || `Enter the ${what}.`;

/** The fields of a patient (US-10); the MRN only when registering, since it never changes (BR-101). */
function PatientFields({ form, fixedMrn }: { form: UseFormReturn<PatientValues>; fixedMrn?: string }) {
  const {
    register,
    formState: { errors },
  } = form;

  return (
    <div className={forms.grid}>
      {fixedMrn === undefined ? (
        <TextField
          id="patient-mrn"
          label="MRN"
          hint="The medical record number of the hospital's records. It never changes."
          error={errors.mrn?.message}
          {...register('mrn', { validate: required('MRN') })}
        />
      ) : (
        <ReadOnlyField label="MRN" value={fixedMrn} hint="The MRN never changes." />
      )}
      <TextField
        id="patient-full-name"
        label="Full name"
        autoComplete="off"
        error={errors.fullName?.message}
        {...register('fullName', { validate: required('full name') })}
      />
      <TextField
        id="patient-date-of-birth"
        label="Date of birth"
        type="date"
        error={errors.dateOfBirth?.message}
        {...register('dateOfBirth')}
      />
      <SelectField id="patient-sex" label="Sex" error={errors.sex?.message} {...register('sex')}>
        <option value="">Not given</option>
        <option value="Male">Male</option>
        <option value="Female">Female</option>
        <option value="Other">Other</option>
        <option value="Unknown">Unknown</option>
      </SelectField>
      <TextField id="patient-phone" label="Phone" type="tel" error={errors.phone?.message} {...register('phone')} />
      <TextField
        id="patient-email"
        label="E-mail address"
        type="email"
        error={errors.email?.message}
        {...register('email')}
      />
      <TextField
        id="patient-address-line-1"
        label="Address line 1"
        error={errors.addressLine1?.message}
        {...register('addressLine1')}
      />
      <TextField
        id="patient-address-line-2"
        label="Address line 2"
        error={errors.addressLine2?.message}
        {...register('addressLine2')}
      />
      <TextField id="patient-city" label="City" error={errors.city?.message} {...register('city')} />
      <TextField id="patient-state" label="State" error={errors.state?.message} {...register('state')} />
      <TextField id="patient-pin-code" label="PIN code" error={errors.pinCode?.message} {...register('pinCode')} />
    </div>
  );
}

/** Registers a patient (US-10); a duplicate MRN is refused by the API (BR-101). */
export function NewPatientPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<PatientValues>({ defaultValues: emptyPatient });
  const outcome = useRecordForm<Patient, PatientValues>({ form, fields: patientFields });

  const registering = useMutation({
    mutationFn: (values: PatientValues) => registerPatient(client, values),
    onMutate: outcome.clear,
    onSuccess: (patient) => {
      queryClient.setQueryData(patientKeys.one(patient.id), patient);
      void queryClient.invalidateQueries({ queryKey: [...patientKeys.all, 'list'] });
      void navigate(`/patients/${patient.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="Register patient" back={{ to: '/patients', label: 'Patients' }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => registering.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={patientFields} />}
        <PatientFields form={form} />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={registering.isPending}
            aria-busy={registering.isPending}
          >
            {registering.isPending ? 'Registering…' : 'Register patient'}
          </button>
          <Link to="/patients">Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a patient's details: every field but the MRN, sent with the version the form started from (ADR 0039). */
export function EditPatientPage() {
  const { id = '' } = useParams();
  const patient = usePatient(id);

  return patient.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit patient" back={{ to: `/patients/${id}`, label: 'Patient' }} />
      <LoadState error={patient.error} what="the patient" />
    </section>
  ) : (
    <EditPatientForm key={patient.data.id} initial={patient.data} />
  );
}

function EditPatientForm({ initial }: { initial: Patient }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<PatientValues>({ defaultValues: patientValues(initial) });
  const outcome = useRecordForm<Patient, PatientValues>({
    form,
    fields: patientFields,
    initial,
    compared: comparedPatientFields,
    valuesOf: patientValues,
    reload: () => getPatient(client, initial.id),
  });

  const correcting = useMutation({
    mutationFn: ({ values, version }: { values: PatientValues; version: string }) =>
      correctPatient(client, initial.id, version, values),
    onMutate: outcome.clear,
    onSuccess: (patient) => {
      queryClient.setQueryData(patientKeys.one(patient.id), patient);
      void queryClient.invalidateQueries({ queryKey: [...patientKeys.all, 'list'] });
      void navigate(`/patients/${patient.id}`);
    },
    onError: outcome.failed,
  });

  const base = outcome.base ?? initial;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Edit ${initial.fullName}`}
        back={{ to: `/patients/${initial.id}`, label: initial.fullName }}
      />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => correcting.mutate({ values, version: base.version }))(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={patientFields} />}
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
        <PatientFields form={form} fixedMrn={initial.mrn} />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={correcting.isPending}
            aria-busy={correcting.isPending}
          >
            {correcting.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/patients/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
