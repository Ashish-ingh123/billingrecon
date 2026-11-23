import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { answer } from '../api/call';
import { useCan } from '../auth/permissions';
import table from '../components/DataTable.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal } from '../format';
import { methodLabels, mostOpenBills, usePayerOpenBills } from '../payments/api';
import { usePayer } from './api';
import styles from './Statement.module.css';

/** The most rows of unapplied payments and available credit a statement shows. */
const mostRows = 100;

/**
 * A payer's statement (US-40, BR-404, ADR 0041 point 3): what they owe, their unapplied cash, their available credit and
 * the net, from the balance report; their outstanding bills; the payments holding unapplied cash, each to allocate or
 * refund; and the credit notes holding available credit, each to refund. Every figure is the server's.
 */
export function StatementPage() {
  const { id = '' } = useParams();
  const client = useApiClient();
  const canAllocate = useCan('RecordPayments');
  const canRefund = useCan('DraftRefunds');
  const payer = usePayer(id);
  const balance = useQuery({
    queryKey: ['statement', id, 'balance'],
    queryFn: () =>
      answer(client.GET('/api/v1/reports/payers/{payerId}/balance', { params: { path: { payerId: id } } })),
  });
  const unapplied = useQuery({
    queryKey: ['statement', id, 'unapplied'],
    queryFn: () =>
      answer(
        client.GET('/api/v1/reports/unapplied-cash', {
          params: { query: { PayerId: id, Page: 1, PageSize: mostRows } },
        }),
      ),
  });
  const credit = useQuery({
    queryKey: ['statement', id, 'credit'],
    queryFn: () =>
      answer(
        client.GET('/api/v1/reports/available-credit', {
          params: { query: { PayerId: id, Page: 1, PageSize: mostRows } },
        }),
      ),
  });
  const bills = usePayerOpenBills(id);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={payer.data === undefined ? 'Statement' : `Statement of ${payer.data.name}`}
        back={{ to: `/payers/${id}`, label: payer.data?.name ?? 'Payer' }}
        subtitle="What the payer owes, the money held for them, and their credit, as the ledger has them now."
      />
      {balance.data === undefined ? (
        <LoadState error={balance.error} what="the balances" />
      ) : (
        <Details
          items={[
            ['Owed on bills', `₹${formatDecimal(balance.data.receivable)}`],
            ['Unapplied cash', `₹${formatDecimal(balance.data.unappliedCash)}`],
            ['Available credit', `₹${formatDecimal(balance.data.creditAvailable)}`],
            [
              'Net outstanding',
              <strong key="net" className={styles.net}>
                ₹{formatDecimal(balance.data.netOutstanding)}
              </strong>,
            ],
          ]}
        />
      )}

      <section className={forms.section} aria-labelledby="bills-title">
        <h2 id="bills-title">Outstanding bills</h2>
        {bills.data === undefined ? (
          <LoadState error={bills.error} what="the bills" />
        ) : bills.data.bills.length === 0 ? (
          <p className={table.empty}>Nothing is owed on a posted bill.</p>
        ) : (
          <>
            {bills.data.truncated && (
              <p className={forms.note}>The {mostOpenBills} oldest bills that owe something are shown.</p>
            )}
            <div className={table.wrap}>
              <table className={table.table}>
                <thead>
                  <tr>
                    <th scope="col">Bill</th>
                    <th scope="col">Bill date</th>
                    <th scope="col">Due date</th>
                    <th scope="col" className={table.number}>
                      Total (₹)
                    </th>
                    <th scope="col" className={table.number}>
                      Balance due (₹)
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {bills.data.bills.map((bill) => (
                    <tr key={bill.id}>
                      <td>
                        <Link to={`/bills/${bill.id}`}>{bill.number}</Link>
                      </td>
                      <td>{formatDate(bill.billDate)}</td>
                      <td>{formatDate(bill.dueDate)}</td>
                      <td className={table.number}>{formatDecimal(bill.total)}</td>
                      <td className={table.number}>{formatDecimal(bill.balanceDue)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>

      <section className={forms.section} aria-labelledby="unapplied-title">
        <h2 id="unapplied-title">Payments with unapplied cash</h2>
        {unapplied.data === undefined ? (
          <LoadState error={unapplied.error} what="the payments" />
        ) : unapplied.data.items.length === 0 ? (
          <p className={table.empty}>No payment of this payer holds unapplied cash.</p>
        ) : (
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Payment</th>
                  <th scope="col">Date</th>
                  <th scope="col">Method</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Unapplied (₹)
                  </th>
                  <th scope="col">
                    <span className={forms.note}>Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {unapplied.data.items.map((payment) => (
                  <tr key={payment.paymentId}>
                    <td>
                      <Link to={`/payments/${payment.paymentId}`}>{payment.number}</Link>
                    </td>
                    <td>{formatDate(payment.paymentDate)}</td>
                    <td>{methodLabels[payment.method]}</td>
                    <td className={table.number}>{formatDecimal(payment.amount)}</td>
                    <td className={table.number}>{formatDecimal(payment.unappliedAmount)}</td>
                    <td className={styles.actions}>
                      {payment.status === 'Posted' && canAllocate && (
                        <Link to={`/payments/${payment.paymentId}/allocate`}>Allocate</Link>
                      )}
                      {payment.status === 'Posted' && canRefund && (
                        <Link to={`/refunds/new?paymentId=${payment.paymentId}`}>Refund</Link>
                      )}
                      {payment.status !== 'Posted' && <span className={forms.note}>Waiting for its reversal</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className={forms.section} aria-labelledby="credit-title">
        <h2 id="credit-title">Credit notes with available credit</h2>
        {credit.data === undefined ? (
          <LoadState error={credit.error} what="the credit notes" />
        ) : credit.data.items.length === 0 ? (
          <p className={table.empty}>No credit note of this payer holds available credit.</p>
        ) : (
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Credit note</th>
                  <th scope="col">Date</th>
                  <th scope="col">For bill</th>
                  <th scope="col" className={table.number}>
                    Total (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Available (₹)
                  </th>
                  <th scope="col">
                    <span className={forms.note}>Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {credit.data.items.map((note) => (
                  <tr key={note.creditNoteId}>
                    <td>{note.number}</td>
                    <td>{formatDate(note.creditNoteDate)}</td>
                    <td>
                      <Link to={`/bills/${note.billId}`}>{note.billNumber}</Link>
                    </td>
                    <td className={table.number}>{formatDecimal(note.total)}</td>
                    <td className={table.number}>{formatDecimal(note.availableAmount)}</td>
                    <td className={styles.actions}>
                      {canRefund && <Link to={`/refunds/new?creditNoteId=${note.creditNoteId}`}>Refund</Link>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </section>
  );
}
