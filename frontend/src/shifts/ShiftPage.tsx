import { Link, useParams } from 'react-router';
import { ReviewLink } from '../approvals/ReviewLink';
import { useSession } from '../auth/AuthContext';
import { useCan } from '../auth/permissions';
import { AuditTrail } from '../components/AuditTrail';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal, formatInstant, formatMoney } from '../format';
import { differenceOfServer, shiftStatusLabels, useShift, type CashierShift } from './api';

function ShiftBanner({ shift, mine }: { shift: CashierShift; mine: boolean }) {
  switch (shift.status) {
    case 'Open':
      return (
        <Banner tone="success" title="This shift is open">
          {mine
            ? 'The cash you take and the cash refunds you drafted join it until you close it.'
            : 'Cash its cashier takes joins it until they close it.'}
        </Banner>
      );
    case 'PendingApproval':
      return (
        <Banner tone="warning" title="The closing waits for approval">
          The count differs from the expected cash, so a second person decides: a supervisor or an administrator who is
          not its cashier. <ReviewLink documentType="CashierShift" />
        </Banner>
      );
    case 'Closed':
      return (
        <Banner tone="accent" title="This shift is closed">
          A closed shift does not change. A cash payment of it that is reversed later is shown outside the shifts in the
          day-end report.
        </Banner>
      );
  }
}

/**
 * One shift (Prompt 11.2b, ADR 0060): whose it is, what its drawer should hold as the ledger has it, and once it is
 * counted the count, what it came to and the difference. Its cashier closes it from here; its closing slip is printed
 * from here once it is counted.
 */
export function ShiftPage() {
  const { id = '' } = useParams();
  const shift = useShift(id);
  const { userId } = useSession();
  const canWork = useCan('WorkShift');
  const canReadTrail = useCan('DayEnd');

  if (shift.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Shift" back={{ to: '/shifts', label: 'Shifts' }} />
        <LoadState error={shift.error} what="the shift" />
      </section>
    );
  }

  const record = shift.data;
  const mine = record.cashierUserId === userId;
  const look = shiftStatusLabels[record.status];
  const counted = record.count ?? null;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Shift of ${formatDate(record.businessDate)}`}
        back={{ to: '/shifts', label: 'Shifts' }}
        subtitle={`${record.cashierName ?? 'A cashier'} · ${look.label}`}
        actions={
          <>
            {mine && canWork && record.status === 'Open' && (
              <Link className={buttons.primary} to={`/shifts/${record.id}/close`}>
                Count and close
              </Link>
            )}
            {counted !== null && (
              <Link className={buttons.secondary} to={`/shifts/${record.id}/print`}>
                Print the closing slip
              </Link>
            )}
          </>
        }
      />
      <ShiftBanner shift={record} mine={mine} />
      <Details
        items={[
          ['Cashier', record.cashierName],
          ['Business date', formatDate(record.businessDate)],
          ['Opened', formatInstant(record.openedAtUtc)],
          ['Opening float', formatMoney(record.openingFloat)],
          ['Cash taken', formatMoney(record.cashReceived)],
          ['Cash paid out', formatMoney(record.cashPaidOut)],
          ['Expected cash', formatMoney(record.expectedCash)],
          ['Cash documents', String(record.cashDocuments)],
          ['Counted cash', record.countedCash === null ? null : formatMoney(record.countedCash)],
          ['Difference', record.difference === null ? null : differenceOfServer(record.difference)],
          ['Reason for the difference', record.differenceReason],
          ['Counted', record.countedAtUtc === null ? null : formatInstant(record.countedAtUtc)],
          ['Closed', record.closedAtUtc === null ? null : formatInstant(record.closedAtUtc)],
        ]}
      />
      {counted !== null && (
        <div className={forms.section}>
          <h2>The count</h2>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Note or coin</th>
                  <th scope="col" className={table.number}>
                    Pieces
                  </th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                </tr>
              </thead>
              <tbody>
                {counted
                  .filter((line) => Number(line.pieces) > 0)
                  .map((line) => (
                    <tr key={String(line.denomination)}>
                      <td>₹{String(line.denomination)}</td>
                      <td className={table.number}>{String(line.pieces)}</td>
                      <td className={table.number}>{formatDecimal(Number(line.denomination) * Number(line.pieces))}</td>
                    </tr>
                  ))}
                <tr>
                  <td>Loose change</td>
                  <td className={table.number} />
                  <td className={table.number}>{formatDecimal(record.looseChange ?? 0)}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      )}
      {canReadTrail && <AuditTrail entityType="CashierShift" entityId={record.id} />}
    </section>
  );
}
