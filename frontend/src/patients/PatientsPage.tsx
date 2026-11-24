import { Link } from 'react-router';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { ListFilters, Pagination } from '../components/ListControls';
import { useListParams } from '../components/listParams';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { formatDate } from '../format';
import { usePatients } from './api';

/** The patients of the hospital (US-10, US-12): found by MRN or name, by status, a page at a time. */
export function PatientsPage() {
  const canChange = useCan('WriteReferenceData');
  const params = useListParams();
  const patients = usePatients(params);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Patients"
        subtitle="Every patient bills are raised for."
        actions={
          canChange && (
            <Link className={buttons.primary} to="/patients/new">
              Register patient
            </Link>
          )
        }
      />
      <ListFilters params={params} onChange={params.change} searchLabel="MRN or name" />
      {patients.data === undefined ? (
        <LoadState error={patients.error} what="patients" />
      ) : patients.data.items.length === 0 ? (
        <EmptyState icon="user">
          {params.search === '' && params.status === undefined && params.page === 1
            ? 'No patient is registered yet.'
            : 'No patient matches.'}
        </EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">MRN</th>
                  <th scope="col">Name</th>
                  <th scope="col">Date of birth</th>
                  <th scope="col">Phone</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {patients.data.items.map((patient) => (
                  <tr key={patient.id}>
                    <td>{patient.mrn}</td>
                    <td>
                      <Link to={`/patients/${patient.id}`}>{patient.fullName}</Link>
                    </td>
                    <td>{formatDate(patient.dateOfBirth)}</td>
                    <td>{patient.phone}</td>
                    <td>
                      <StatusBadge status={patient.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(patients.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
