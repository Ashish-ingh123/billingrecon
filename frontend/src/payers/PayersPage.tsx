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
import { kindLabel, usePayers } from './api';

/** The payers (US-11, US-12): who owes the bills, each a patient's own payer or a guarantor. */
export function PayersPage() {
  const canChange = useCan('WriteReferenceData');
  const params = useListParams();
  const payers = usePayers(params);

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
      <ListFilters params={params} onChange={params.change} searchLabel="Name" />
      {payers.data === undefined ? (
        <LoadState error={payers.error} what="payers" />
      ) : payers.data.items.length === 0 ? (
        <EmptyState icon="wallet">
          {params.search === '' && params.status === undefined && params.page === 1
            ? 'No payer exists yet.'
            : 'No payer matches.'}
        </EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Kind</th>
                  <th scope="col">Pays for</th>
                  <th scope="col">Phone</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {payers.data.items.map((payer) => (
                  <tr key={payer.id}>
                    <td>
                      <Link to={`/payers/${payer.id}`}>{payer.name}</Link>
                    </td>
                    <td>{kindLabel(payer.kind)}</td>
                    <td>{payer.patientId === null ? 'Guarantor' : 'A patient (their own payer)'}</td>
                    <td>{payer.phone}</td>
                    <td>
                      <StatusBadge status={payer.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
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
