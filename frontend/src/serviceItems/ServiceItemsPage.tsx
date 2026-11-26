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
import { formatDecimal, formatPercent } from '../format';
import { useServiceItems } from './api';

/** The service catalogue (BR-201): what a bill line starts from, found by code or name, a page at a time. */
export function ServiceItemsPage() {
  const canChange = useCan('WriteReferenceData');
  const params = useListParams();
  const items = useServiceItems(params);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Service items"
        subtitle="The catalogue a bill line takes its default price and tax rate from."
        actions={
          canChange && (
            <Link className={buttons.primary} to="/service-items/new">
              New service item
            </Link>
          )
        }
      />
      <ListFilters params={params} onChange={params.change} searchLabel="Code or name" busy={items.isFetching} />
      {items.data === undefined ? (
        <LoadState error={items.error} what="service items" />
      ) : items.data.items.length === 0 ? (
        <EmptyState icon="tag">
          {params.search === '' && params.status === undefined && params.page === 1
            ? 'The catalogue is empty.'
            : 'No service item matches.'}
        </EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Name</th>
                  <th scope="col">Category</th>
                  <th scope="col" className={table.number}>
                    Default unit price (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Tax rate
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {items.data.items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <Link to={`/service-items/${item.id}`}>{item.code}</Link>
                    </td>
                    <td>{item.name}</td>
                    <td>{item.category}</td>
                    <td className={table.number}>{formatDecimal(item.defaultUnitPrice, 4)}</td>
                    <td className={table.number}>{formatPercent(item.taxRatePercent)}</td>
                    <td>
                      <StatusBadge status={item.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(items.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
