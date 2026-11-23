import { useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { useStatusActions } from '../components/useStatusActions';
import { formatDate, formatInstant } from '../format';
import { usePayerOfPatient } from '../payers/api';
import {
  comparedPatientFields,
  deactivatePatient,
  getPatient,
  patientKeys,
  reactivatePatient,
  usePatient,
  type Patient,
} from './api';

/** One patient: their details, their payer, and what a user who may change them can do. */
export function PatientPage() {
  const { id = '' } = useParams();
  const patient = usePatient(id);

  return patient.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Patient" back={{ to: '/patients', label: 'Patients' }} />
      <LoadState error={patient.error} what="the patient" />
    </section>
  ) : (
    <PatientView patient={patient.data} />
  );
}

function PatientView({ patient }: { patient: Patient }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const canChange = useCan('WriteReferenceData');

  const status = useStatusActions({
    record: patient,
    what: 'patient',
    deactivate: () => deactivatePatient(client, patient),
    reactivate: () => reactivatePatient(client, patient),
    compared: comparedPatientFields,
    reload: () => getPatient(client, patient.id),
    onChanged: (changed) => {
      queryClient.setQueryData(patientKeys.one(changed.id), changed);
      void queryClient.invalidateQueries({ queryKey: [...patientKeys.all, 'list'] });
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={patient.fullName}
        back={{ to: '/patients', label: 'Patients' }}
        subtitle={
          <>
            MRN {patient.mrn} · <StatusBadge status={patient.status} />
          </>
        }
        actions={
          canChange && (
            <>
              <Link className={buttons.secondary} to={`/patients/${patient.id}/edit`}>
                Edit
              </Link>
              {status.actions}
            </>
          )
        }
      />
      {status.notices}
      <Details
        items={[
          ['Date of birth', formatDate(patient.dateOfBirth)],
          ['Sex', patient.sex],
          ['Phone', patient.phone],
          ['E-mail address', patient.email],
          ['Address', [patient.addressLine1, patient.addressLine2].filter(Boolean).join(', ')],
          ['City', patient.city],
          ['State', patient.state],
          ['PIN code', patient.pinCode],
          ['Registered', formatInstant(patient.createdAtUtc)],
          ['Last changed', formatInstant(patient.updatedAtUtc)],
        ]}
      />
      {patient.status === 'Inactive' && (
        <p className={forms.note}>An inactive patient can be read and corrected, but no new bill is raised for them.</p>
      )}
      <PatientPayer patient={patient} canChange={canChange} />
      <p className={forms.section}>
        <Link to={`/bills?patientId=${patient.id}`}>The bills of this patient</Link>
      </p>
    </section>
  );
}

/** The payer linked to the patient (BR-104): at most one, created from here. */
function PatientPayer({ patient, canChange }: { patient: Patient; canChange: boolean }) {
  const payer = usePayerOfPatient(patient.id);

  return (
    <section className={forms.section} aria-labelledby="payer-title">
      <h2 id="payer-title">Payer</h2>
      {payer.isPending || payer.error !== null ? (
        <LoadState error={payer.error} what="the payer" />
      ) : payer.data === null ? (
        <>
          <p className={forms.note}>No payer is linked to this patient.</p>
          {canChange &&
            (patient.status === 'Active' ? (
              <Link className={buttons.secondary} to={`/payers/new?patientId=${patient.id}`}>
                Create the patient&apos;s payer
              </Link>
            ) : (
              <p className={forms.note}>An inactive patient cannot be given a payer.</p>
            ))}
        </>
      ) : (
        <p>
          <Link to={`/payers/${payer.data.id}`}>{payer.data.name}</Link> · <StatusBadge status={payer.data.status} />
        </p>
      )}
    </section>
  );
}
