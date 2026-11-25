import { useParams } from 'react-router';
import { useBill } from '../bills/api';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatMoney } from '../format';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import { methodLabels, usePaymentWithAllocations, type PaymentWithAllocations } from '../payments/api';
import { useLetterhead, type Letterhead } from './api';
import styles from './Printing.module.css';
import { PrintSheet } from './PrintSheet';
import { amountInWords } from './words';

function BillNumber({ id }: { id: string }) {
  const bill = useBill(id);
  return <>{bill.data?.number ?? '…'}</>;
}

/**
 * The receipt of a payment (Prompt 10.4b, ADR 0054): from whom, how much in figures and in words, how and when it was
 * paid, and the bills it was applied to with what is left unapplied. A reversed payment has no receipt.
 */
export function PrintedReceiptPage() {
  const { id = '' } = useParams();
  const payment = usePaymentWithAllocations(id);
  const letterhead = useLetterhead();

  if (payment.data === undefined || letterhead.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a receipt" back={{ to: `/payments/${id}`, label: 'the payment' }} />
        <LoadState error={payment.error ?? letterhead.error} what="the payment" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print the receipt of ${payment.data.payment.number}`} />
      {payment.data.payment.status === 'Reversed' ? (
        <Banner tone="warning" title="This payment has no receipt">
          The payment was reversed: it holds no money, so no receipt is printed for it.
        </Banner>
      ) : (
        <ReceiptSheet record={payment.data} letterhead={letterhead.data} />
      )}
    </section>
  );
}

function ReceiptSheet({ record, letterhead }: { record: PaymentWithAllocations; letterhead: Letterhead }) {
  const { payment } = record;
  const payer = usePayer(payment.payerId);
  const applied = record.allocations.filter((allocation) => allocation.status !== 'Reversed');
  const opening = payment.origin === 'Opening';

  return (
    <PrintSheet
      kind="payments"
      id={payment.id}
      back={{ to: `/payments/${payment.id}`, label: 'the payment' }}
      title={opening ? 'Statement of Opening Advance' : 'Receipt'}
      letterhead={letterhead}
    >
      <dl className={styles.facts}>
        <div>
          <dt>{opening ? 'Reference' : 'Receipt number'}</dt>
          <dd>{payment.number}</dd>
        </div>
        <div>
          <dt>Date</dt>
          <dd>{formatDate(payment.paymentDate)}</dd>
        </div>
        <div>
          <dt>Received from</dt>
          <dd>{payer.data?.name ?? '…'}</dd>
        </div>
        <div>
          <dt>Paid by</dt>
          <dd>
            {methodLabels[payment.method]}
            {payment.reference !== null && `, reference ${payment.reference}`}
          </dd>
        </div>
      </dl>

      <dl className={styles.totals}>
        <div className={styles.grand}>
          <dt>Amount received</dt>
          <dd>{formatMoney(payment.amount)}</dd>
        </div>
      </dl>
      <p className={styles.words}>
        <strong>In words:</strong> {amountInWords(paiseOfServer(payment.amount))}
      </p>

      {applied.length > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Applied to bill</th>
                <th scope="col" className={styles.number}>
                  Amount
                </th>
              </tr>
            </thead>
            <tbody>
              {applied.map((allocation) => (
                <tr key={allocation.id}>
                  <td>
                    <BillNumber id={allocation.billId} />
                  </td>
                  <td className={styles.number}>{formatMoney(allocation.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className={styles.small}>
        Not yet applied to a bill, as of this print: {formatMoney(payment.unappliedAmount)}.
      </p>
      {opening && (
        <p className={styles.small}>
          This amount was held for the payer before {letterhead.name} began billing in this system. It is a statement of
          that advance, not a receipt for money received now.
        </p>
      )}
    </PrintSheet>
  );
}
