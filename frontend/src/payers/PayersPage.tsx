import { useId } from 'react';
import { Link } from 'react-router';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import { ListFilters, Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { useListParams } from '../components/listParams';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { StatusBadge } from '../components/StatusBadge';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { kindLabel, payerKinds, usePayers, type Payer } from './api';

/** The kind filter names a group of payers, not one payer's kind as a cell says it. */
const kindGroups: Readonly<Record<Payer['kind'], string>> = {
  SelfPay: 'Self-pay payers',
  Insurer: 'Insurers',
  Corporate: 'Corporates',
};

const columns: readonly TableColumn<Payer>[] = [
  {
    id: 'name',
    header: 'Name',
    fixed: true,
    sort: 'name',
    cell: (payer) => <Link to={`/payers/${payer.id}`}>{payer.name}</Link>,
  },
  { id: 'kind', header: 'Kind', sort: 'kind', cell: (payer) => kindLabel(payer.kind) },
  {
    id: 'paysFor',
    header: 'Pays for',
    cell: (payer) => (payer.patientId === null ? 'Guarantor' : 'A patient (their own payer)'),
  },
  { id: 'phone', header: 'Phone', cell: (payer) => payer.phone },
  { id: 'status', header: 'Status', fixed: true, cell: (payer) => <StatusBadge status={payer.status} /> },
];

/**
 * The payers (US-11, US-12): who owes the bills, each a patient's own payer or a guarantor; found by name, status and
 * kind, in the order asked for. The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function PayersPage() {
  const canChange = useCan('WriteReferenceData');
  const params = useListParams(['kind']);
  const payers = usePayers(params);
  const kindId = useId();

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Payers"
        subtitle="Who owes the bills: a patient's own payer, or a guarantor who pays for others. A patient's payer is created from the patient's page."
        actions={
          canChange && (
            <Link className={buttons.primary} to="/payers/new">
              New guarantor
            </Link>
          )
        }
      />
      <ListFilters
        params={params}
        onChange={params.change}
        searchLabel="Name"
        busy={payers.isFetching}
        inUse={params.inUse}
        onClear={params.clear}
      >
        <div className={listStyles.field}>
          <label htmlFor={kindId}>Kind</label>
          <select
            id={kindId}
            className={listStyles.input}
            value={params.extra.kind ?? ''}
            onChange={(event) => params.change({ extra: { kind: event.target.value } })}
          >
            <option value="">All</option>
            {payerKinds.map((kind) => (
              <option key={kind} value={kind}>
                {kindGroups[kind]}
              </option>
            ))}
          </select>
        </div>
      </ListFilters>
      {payers.data === undefined ? (
        <LoadState error={payers.error} what="payers" />
      ) : payers.data.items.length === 0 ? (
        <EmptyState icon="wallet">
          {params.inUse === 0 && params.page === 1 ? 'No payer exists yet.' : 'No payer matches.'}
        </EmptyState>
      ) : (
        <>
          <DataTable
            tableId="payers"
            label="Payers"
            columns={columns}
            rows={payers.data.items}
            keyOf={(payer) => payer.id}
            sort={params.sort}
            onSort={(sort) => params.change({ sort })}
            busy={payers.isFetching}
          />
          <Pagination
            page={params.page}
            totalCount={Number(payers.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
