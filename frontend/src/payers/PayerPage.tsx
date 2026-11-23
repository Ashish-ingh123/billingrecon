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
import { formatInstant } from '../format';
import { usePatient } from '../patients/api';
import {
  comparedPayerFields,
  deactivatePayer,
  getPayer,
  kindLabel,
  payerKeys,
  payersChanged,
  reactivatePayer,
  usePayer,
  type Payer,
} from './api';

/** One payer: who they are, whom they pay for, and what a user who may change them can do. */
export function PayerPage() {
  const { id = '' } = useParams();
  const payer = usePayer(id);

  return payer.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Payer" back={{ to: '/payers', label: 'Payers' }} />
      <LoadState error={payer.error} what="the payer" />
    </section>
  ) : (
    <PayerView payer={payer.data} />
  );
}

function PayerView({ payer }: { payer: Payer }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const canChange = useCan('WriteReferenceData');

  const status = useStatusActions({
    record: payer,
    what: 'payer',
    deactivate: () => deactivatePayer(client, payer),
    reactivate: () => reactivatePayer(client, payer),
    compared: comparedPayerFields,
    reload: () => getPayer(client, payer.id),
    onChanged: (changed) => {
      queryClient.setQueryData(payerKeys.one(changed.id), changed);
      payersChanged(queryClient);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={payer.name}
        back={{ to: '/payers', label: 'Payers' }}
        subtitle={
          <>
            {kindLabel(payer.kind)} · <StatusBadge status={payer.status} />
          </>
        }
        actions={
          canChange && (
            <>
              <Link className={buttons.secondary} to={`/payers/${payer.id}/edit`}>
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
          ['Pays for', payer.patientId === null ? 'Others, as a guarantor' : <LinkedPatient id={payer.patientId} />],
          ['Phone', payer.phone],
          ['E-mail address', payer.email],
          ['Created', formatInstant(payer.createdAtUtc)],
          ['Last changed', formatInstant(payer.updatedAtUtc)],
        ]}
      />
      {payer.status === 'Inactive' && (
        <p className={forms.note}>
          An inactive payer receives no new bills, payments or refunds; what they owe is still allocated and reported.
        </p>
      )}
      <PayerLinks payer={payer} />
    </section>
  );
}

function LinkedPatient({ id }: { id: string }) {
  const patient = usePatient(id);

  return patient.data === undefined ? (
    <span>{patient.error === null ? 'Loading…' : 'Their patient (cannot be read now)'}</span>
  ) : (
    <Link to={`/patients/${id}`}>
      {patient.data.fullName} (MRN {patient.data.mrn})
    </Link>
  );
}

/** Where the payer's money is: their statement, their bills, payments and refunds, and recording a payment from them. */
function PayerLinks({ payer }: { payer: Payer }) {
  const canRecord = useCan('RecordPayments');
  const canReadRefunds = useCan('ReadRefunds');

  return (
    <section className={forms.section} aria-labelledby="money-title">
      <h2 id="money-title">Money</h2>
      <ul className={forms.links}>
        <li>
          <Link to={`/payers/${payer.id}/statement`}>Statement: what they owe, unapplied cash and credit</Link>
        </li>
        <li>
          <Link to={`/bills?payerId=${payer.id}`}>The bills this payer owes</Link>
        </li>
        <li>
          <Link to={`/payments?payerId=${payer.id}`}>Their payments</Link>
        </li>
        {canReadRefunds && (
          <li>
            <Link to={`/refunds?payerId=${payer.id}`}>Their refunds</Link>
          </li>
        )}
        {canRecord && payer.status === 'Active' && (
          <li>
            <Link to={`/payments/new?payerId=${payer.id}`}>Record a payment from them</Link>
          </li>
        )}
      </ul>
    </section>
  );
}
