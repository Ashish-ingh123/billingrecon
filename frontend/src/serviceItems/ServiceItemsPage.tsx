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
import { CategoryFilter } from '../components/table/TextFilter';
import { formatDecimal, formatPercent } from '../format';
import { useServiceItems, type ServiceItem } from './api';

const columns: readonly TableColumn<ServiceItem>[] = [
  {
    id: 'code',
    header: 'Code',
    fixed: true,
    sort: 'code',
    cell: (item) => <Link to={`/service-items/${item.id}`}>{item.code}</Link>,
  },
  { id: 'name', header: 'Name', sort: 'name', cell: (item) => item.name },
  { id: 'category', header: 'Category', cell: (item) => item.category },
  {
    id: 'price',
    header: 'Default unit price (₹)',
    numeric: true,
    sort: 'defaultUnitPrice',
    cell: (item) => formatDecimal(item.defaultUnitPrice, 4),
  },
  { id: 'tax', header: 'Tax rate', numeric: true, cell: (item) => formatPercent(item.taxRatePercent) },
  { id: 'status', header: 'Status', fixed: true, cell: (item) => <StatusBadge status={item.status} /> },
];

/**
 * The service catalogue (BR-201): what a bill line starts from, found by code or name, status and category, in the order
 * asked for, a page at a time. The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function ServiceItemsPage() {
  const canChange = useCan('WriteReferenceData');
  const params = useListParams(['category']);
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
      <ListFilters
        params={params}
        onChange={params.change}
        searchLabel="Code or name"
        busy={items.isFetching}
        inUse={params.inUse}
        onClear={params.clear}
      >
        <CategoryFilter
          value={params.extra.category ?? ''}
          onChange={(category) => params.change({ extra: { category } })}
        />
      </ListFilters>
      {items.data === undefined ? (
        <LoadState error={items.error} what="service items" />
      ) : items.data.items.length === 0 ? (
        <EmptyState icon="tag">
          {params.inUse === 0 && params.page === 1 ? 'The catalogue is empty.' : 'No service item matches.'}
        </EmptyState>
      ) : (
        <>
          <DataTable
            tableId="service-items"
            label="Service items"
            columns={columns}
            rows={items.data.items}
            keyOf={(item) => item.id}
            sort={params.sort}
            onSort={(sort) => params.change({ sort })}
            busy={items.isFetching}
          />
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
