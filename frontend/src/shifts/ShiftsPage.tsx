import { Link } from 'react-router';
import { useCan } from '../auth/permissions';
import { Badge } from '../components/Badge';
import { Banner } from '../components/Banner';
import { EmptyState } from '../components/EmptyState';
import { Pagination } from '../components/ListControls';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { FilterBar, type FilterSpec } from '../components/table/FilterBar';
import { known, useListState } from '../components/table/listState';
import { formatDate, formatDecimal, formatMoney } from '../format';
import {
  differenceOfServer,
  shiftStatuses,
  shiftStatusLabels,
  useMyShift,
  useShifts,
  type CashierShift,
  type ShiftListParams,
} from './api';
import styles from './Shifts.module.css';

/** Every filter of the shifts the address may hold (Prompt 12.2, ADR 0063); `date` is one day, as before. */
const keys = ['status', 'date', 'from', 'to'] as const;

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: shiftStatuses.map((status) => ({ value: status, label: shiftStatusLabels[status].label })),
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Business date' },
];

const money = (value: number | string) => formatDecimal(value);

const columns: readonly TableColumn<CashierShift>[] = [
  {
    id: 'date',
    header: 'Business date',
    fixed: true,
    sort: 'businessDate',
    cell: (shift) => <Link to={`/shifts/${shift.id}`}>{formatDate(shift.businessDate)}</Link>,
  },
  { id: 'cashier', header: 'Cashier', cell: (shift) => shift.cashierName },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (shift) => (
      <Badge tone={shiftStatusLabels[shift.status].tone} icon={shiftStatusLabels[shift.status].icon}>
        {shiftStatusLabels[shift.status].label}
      </Badge>
    ),
  },
  { id: 'float', header: 'Float (₹)', numeric: true, cell: (shift) => money(shift.openingFloat) },
  { id: 'taken', header: 'Taken (₹)', numeric: true, cell: (shift) => money(shift.cashReceived) },
  { id: 'paidOut', header: 'Paid out (₹)', numeric: true, cell: (shift) => money(shift.cashPaidOut) },
  { id: 'expected', header: 'Expected (₹)', numeric: true, cell: (shift) => money(shift.expectedCash) },
  {
    id: 'counted',
    header: 'Counted (₹)',
    numeric: true,
    cell: (shift) => (shift.countedCash === null ? '—' : money(shift.countedCash)),
  },
  {
    id: 'difference',
    header: 'Difference',
    sort: 'difference',
    cell: (shift) => (shift.difference === null ? '—' : differenceOfServer(shift.difference)),
  },
];

/** The shift the user has now, and what to do with it; or that they have none. */
function MyShift() {
  const mine = useMyShift();

  if (mine.data === undefined) {
    return <LoadState error={mine.error} what="your shift" />;
  }

  const shift = mine.data;
  if (shift === null) {
    return (
      <Banner tone="accent" title="You have no open shift">
        Cash is taken and paid out in an open shift. <Link to="/shifts/new">Open a shift</Link> with the float of your
        drawer before you take cash. Card, UPI, bank transfer and cheque need none.
      </Banner>
    );
  }

  return shift.status === 'Open' ? (
    <Banner tone="success" title={`Your shift of ${formatDate(shift.businessDate)} is open`}>
      Your drawer should hold {formatMoney(shift.expectedCash)} now: the float of {formatMoney(shift.openingFloat)},{' '}
      {formatMoney(shift.cashReceived)} taken and {formatMoney(shift.cashPaidOut)} paid out.{' '}
      <Link to={`/shifts/${shift.id}/close`}>Count the drawer and close the shift</Link>
    </Banner>
  ) : (
    <Banner tone="warning" title={`Your shift of ${formatDate(shift.businessDate)} waits for approval`}>
      Its count differs from the expected cash, so a supervisor decides its closing. Until then you take no cash and
      open no other shift. <Link to={`/shifts/${shift.id}`}>See the shift</Link>
    </Banner>
  );
}

/**
 * The shifts of the cash counter (Prompt 11.2b, ADR 0060): the user's own shift and what to do with it, and the list.
 * A cashier and a billing clerk see the shifts they opened; a supervisor, an administrator and a viewer see all (the
 * API decides which, BR-310 to BR-315). Filtered by status and a period of business dates, in the order asked for; the
 * API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function ShiftsPage() {
  const canWork = useCan('WorkShift');
  const canSeeDayEnd = useCan('DayEnd');
  const state = useListState(keys);
  const { values, sort, page } = state;
  const params: ShiftListParams = {
    status: known(values.status, shiftStatuses),
    date: values.date,
    dateFrom: values.from,
    dateTo: values.to,
    sort,
    page,
  };
  const shifts = useShifts(params);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Shifts"
        subtitle="A shift of the cash counter: opened with a float, closed with a count of the drawer."
        actions={canSeeDayEnd && <Link to="/reports/day-end">Day-end report</Link>}
      />
      {canWork && (
        <div className={styles.mine}>
          <MyShift />
        </div>
      )}
      <FilterBar state={state} filters={filters} busy={shifts.isFetching} />
      {shifts.data === undefined ? (
        <LoadState error={shifts.error} what="shifts" />
      ) : shifts.data.items.length === 0 ? (
        <EmptyState icon="clock">No shift matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="shifts"
            label="Shifts"
            columns={columns}
            rows={shifts.data.items}
            keyOf={(shift) => shift.id}
            sort={sort}
            onSort={state.setSort}
            busy={shifts.isFetching}
          />
          <Pagination page={page} totalCount={Number(shifts.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
