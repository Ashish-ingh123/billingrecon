import { Link } from 'react-router';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import { ListFilters, Pagination } from '../components/ListControls';
import { useListParams } from '../components/listParams';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { formatDate } from '../format';
import { usePatients, type Patient } from './api';

const columns: readonly TableColumn<Patient>[] = [
  { id: 'mrn', header: 'MRN', sort: 'mrn', cell: (patient) => patient.mrn },
  {
    id: 'name',
    header: 'Name',
    fixed: true,
    sort: 'fullName',
    cell: (patient) => <Link to={`/patients/${patient.id}`}>{patient.fullName}</Link>,
  },
  {
    id: 'dateOfBirth',
    header: 'Date of birth',
    sort: 'dateOfBirth',
    cell: (patient) => formatDate(patient.dateOfBirth),
  },
  { id: 'phone', header: 'Phone', cell: (patient) => patient.phone },
  { id: 'status', header: 'Status', fixed: true, cell: (patient) => <StatusBadge status={patient.status} /> },
];

/**
 * The patients of the hospital (US-10, US-12): found by MRN or name, by status, in the order asked for, a page at a time.
 * The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
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
      <ListFilters
        params={params}
        onChange={params.change}
        searchLabel="MRN or name"
        busy={patients.isFetching}
        inUse={params.inUse}
        onClear={params.clear}
      />
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
          <DataTable
            tableId="patients"
            label="Patients"
            columns={columns}
            rows={patients.data.items}
            keyOf={(patient) => patient.id}
            sort={params.sort}
            onSort={(sort) => params.change({ sort })}
            busy={patients.isFetching}
          />
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
