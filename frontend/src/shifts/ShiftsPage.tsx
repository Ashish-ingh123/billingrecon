import { useId } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useCan } from '../auth/permissions';
import { Badge } from '../components/Badge';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { EmptyState } from '../components/EmptyState';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal, formatMoney } from '../format';
import {
  differenceOfServer,
  shiftStatuses,
  shiftStatusLabels,
  useMyShift,
  useShifts,
  type CashierShift,
  type ShiftListParams,
  type ShiftStatus,
} from './api';
import styles from './Shifts.module.css';

function useShiftListParams(): ShiftListParams & { change: (next: Partial<ShiftListParams>) => void } {
  const [params, setParams] = useSearchParams();
  const status = params.get('status') as ShiftStatus | null;
  const page = Number.parseInt(params.get('page') ?? '1', 10);
  const current: ShiftListParams = {
    status: status !== null && shiftStatuses.includes(status) ? status : undefined,
    date: params.get('date') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };

  const change = (next: Partial<ShiftListParams>) => {
    const merged = { ...current, ...next, page: next.page ?? 1 };
    const query = new URLSearchParams();
    if (merged.status !== undefined) {
      query.set('status', merged.status);
    }

    if (merged.date !== '') {
      query.set('date', merged.date);
    }

    if (merged.page > 1) {
      query.set('page', String(merged.page));
    }

    setParams(query);
  };

  return { ...current, change };
}

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

function ShiftRow({ shift }: { shift: CashierShift }) {
  const look = shiftStatusLabels[shift.status];

  return (
    <tr>
      <td>
        <Link to={`/shifts/${shift.id}`}>{formatDate(shift.businessDate)}</Link>
      </td>
      <td>{shift.cashierName}</td>
      <td>
        <Badge tone={look.tone} icon={look.icon}>
          {look.label}
        </Badge>
      </td>
      <td className={table.number}>{formatDecimal(shift.openingFloat)}</td>
      <td className={table.number}>{formatDecimal(shift.cashReceived)}</td>
      <td className={table.number}>{formatDecimal(shift.cashPaidOut)}</td>
      <td className={table.number}>{formatDecimal(shift.expectedCash)}</td>
      <td className={table.number}>{shift.countedCash === null ? '—' : formatDecimal(shift.countedCash)}</td>
      <td>{shift.difference === null ? '—' : differenceOfServer(shift.difference)}</td>
    </tr>
  );
}

/**
 * The shifts of the cash counter (Prompt 11.2b, ADR 0060): the user's own shift and what to do with it, and the list.
 * A cashier and a billing clerk see the shifts they opened; a supervisor, an administrator and a viewer see all (the
 * API decides which, BR-310 to BR-315).
 */
export function ShiftsPage() {
  const canWork = useCan('WorkShift');
  const canSeeDayEnd = useCan('DayEnd');
  const params = useShiftListParams();
  const shifts = useShifts(params);
  const ids = { status: useId(), date: useId() };

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
      <form className={listStyles.filters} role="search" onSubmit={(event) => event.preventDefault()}>
        <div className={listStyles.field}>
          <label htmlFor={ids.status}>Status</label>
          <select
            id={ids.status}
            className={listStyles.input}
            value={params.status ?? ''}
            onChange={(event) =>
              params.change({ status: (event.target.value || undefined) as ShiftStatus | undefined })
            }
          >
            <option value="">All</option>
            {shiftStatuses.map((status) => (
              <option key={status} value={status}>
                {shiftStatusLabels[status].label}
              </option>
            ))}
          </select>
        </div>
        <div className={listStyles.field}>
          <label htmlFor={ids.date}>Business date</label>
          <input
            id={ids.date}
            className={listStyles.input}
            type="date"
            value={params.date}
            onChange={(event) => params.change({ date: event.target.value })}
          />
        </div>
        {params.date !== '' && (
          <button type="button" className={buttons.link} onClick={() => params.change({ date: '' })}>
            Every date
          </button>
        )}
      </form>
      {shifts.data === undefined ? (
        <LoadState error={shifts.error} what="shifts" />
      ) : shifts.data.items.length === 0 ? (
        <EmptyState icon="clock">No shift matches.</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Business date</th>
                  <th scope="col">Cashier</th>
                  <th scope="col">Status</th>
                  <th scope="col" className={table.number}>
                    Float (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Taken (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Paid out (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Expected (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Counted (₹)
                  </th>
                  <th scope="col">Difference</th>
                </tr>
              </thead>
              <tbody>
                {shifts.data.items.map((shift) => (
                  <ShiftRow key={shift.id} shift={shift} />
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(shifts.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
