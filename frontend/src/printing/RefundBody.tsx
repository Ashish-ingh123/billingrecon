import type { ReactNode } from 'react';
import { formatDate, formatMoney } from '../format';
import { paiseOfServer } from '../money';
import { methodLabels } from '../payments/api';
import type { Refund } from '../refunds/api';
import { ContactFacts } from './Contact';
import type { PatientContact } from './patientContact';
import styles from './Printing.module.css';
import type { PrintLayout } from './settings';
import { amountInWords } from './words';

/**
 * What a printed refund voucher says between its head and its foot (ADR 0054; laid out by the settings of ADR 0059):
 * to whom money was paid back, how much in figures and in words, how, from which payment or credit note, why, and a
 * line for the receiver's signature. The settings choose what is shown; the amount is the stored one.
 */
export function RefundBody({
  refund,
  layout,
  payerName,
  contact,
  cashierName,
  source,
}: {
  refund: Refund;
  layout: PrintLayout;
  payerName: string | undefined;
  /** The contact of the payer's patient, when the settings ask for it and the payer is a patient's. */
  contact: PatientContact | undefined;
  /** Who drafted the refund, when the settings ask for it and a user did. */
  cashierName: string | null | undefined;
  /** The payment or the credit note the money came from, by its number. */
  source: ReactNode;
}) {
  return (
    <>
      <dl className={styles.facts}>
        <div>
          <dt>Voucher number</dt>
          <dd>{refund.number}</dd>
        </div>
        <div>
          <dt>Date</dt>
          <dd>{formatDate(refund.refundDate)}</dd>
        </div>
        {layout.showPayer && (
          <div>
            <dt>Paid to</dt>
            <dd>{payerName ?? '…'}</dd>
          </div>
        )}
        {layout.showPatientContact && <ContactFacts contact={contact} />}
        <div>
          <dt>Paid by</dt>
          <dd>
            {methodLabels[refund.method]}
            {refund.reference !== null && `, reference ${refund.reference}`}
          </dd>
        </div>
        <div>
          <dt>Refunded from</dt>
          <dd>{source}</dd>
        </div>
        <div>
          <dt>Reason</dt>
          <dd>{refund.reason}</dd>
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
          <dt>Amount refunded</dt>
          <dd>{formatMoney(refund.amount)}</dd>
        </div>
      </dl>
      {layout.showAmountInWords && (
        <p className={styles.words}>
          <strong>In words:</strong> {amountInWords(paiseOfServer(refund.amount))}
        </p>
      )}
      <p className={styles.small}>Received the amount above.</p>
      <p className={styles.small}>Signature of the receiver: ______________________</p>
    </>
  );
}
