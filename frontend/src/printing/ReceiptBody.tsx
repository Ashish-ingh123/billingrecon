import type { ReactNode } from 'react';
import { formatDate, formatMoney } from '../format';
import { paiseOfServer } from '../money';
import { methodLabels, type PaymentWithAllocations } from '../payments/api';
import type { Letterhead } from './api';
import { ContactFacts } from './Contact';
import type { PatientContact } from './patientContact';
import styles from './Printing.module.css';
import type { PrintLayout } from './settings';
import { amountInWords } from './words';

/**
 * What a printed receipt says between its head and its foot (ADR 0054; laid out by the settings of ADR 0059): from
 * whom, how much in figures and in words, how and when it was paid, and the bills it was applied to with what is left
 * unapplied. The settings choose what is shown; the amounts are the stored ones.
 */
export function ReceiptBody({
  record,
  letterhead,
  layout,
  payerName,
  contact,
  cashierName,
  billNumber,
}: {
  record: PaymentWithAllocations;
  letterhead: Letterhead;
  layout: PrintLayout;
  payerName: string | undefined;
  /** The contact of the payer's patient, when the settings ask for it and the payer is a patient's. */
  contact: PatientContact | undefined;
  /** Who recorded the payment, when the settings ask for it and a user did. */
  cashierName: string | null | undefined;
  /** How the number of a bill an allocation names is shown. */
  billNumber: (billId: string) => ReactNode;
}) {
  const { payment } = record;
  const applied = record.allocations.filter((allocation) => allocation.status !== 'Reversed');
  const opening = payment.origin === 'Opening';

  return (
    <>
      <dl className={styles.facts}>
        <div>
          <dt>{opening ? 'Reference' : 'Receipt number'}</dt>
          <dd>{payment.number}</dd>
        </div>
        <div>
          <dt>Date</dt>
          <dd>{formatDate(payment.paymentDate)}</dd>
        </div>
        {layout.showPayer && (
          <div>
            <dt>Received from</dt>
            <dd>{payerName ?? '…'}</dd>
          </div>
        )}
        {layout.showPatientContact && <ContactFacts contact={contact} />}
        <div>
          <dt>Paid by</dt>
          <dd>
            {methodLabels[payment.method]}
            {payment.reference !== null && `, reference ${payment.reference}`}
          </dd>
        </div>
        {layout.showCashierName && cashierName !== null && cashierName !== undefined && (
          <div>
            <dt>Cashier</dt>
            <dd>{cashierName}</dd>
          </div>
        )}
      </dl>

      <dl className={styles.totals}>
        <div className={styles.grand}>
          <dt>Amount received</dt>
          <dd>{formatMoney(payment.amount)}</dd>
        </div>
      </dl>
      {layout.showAmountInWords && (
        <p className={styles.words}>
          <strong>In words:</strong> {amountInWords(paiseOfServer(payment.amount))}
        </p>
      )}

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
                  <td>{billNumber(allocation.billId)}</td>
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
    </>
  );
}
