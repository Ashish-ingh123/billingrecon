import { useParams } from 'react-router';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { useCreditNote } from '../creditNotes/api';
import { formatDate, formatMoney } from '../format';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import { methodLabels, usePaymentWithAllocations } from '../payments/api';
import { useRefund, type Refund } from '../refunds/api';
import { useLetterhead, type Letterhead } from './api';
import styles from './Printing.module.css';
import { PrintSheet } from './PrintSheet';
import { amountInWords } from './words';

/** The document the refund was drawn from, by its number. */
function Source({ refund }: { refund: Refund }) {
  const payment = usePaymentWithAllocations(refund.paymentId ?? '', refund.paymentId !== null);
  const creditNote = useCreditNote(refund.creditNoteId ?? '', refund.creditNoteId !== null);

  return refund.paymentId !== null ? (
    <>Payment {payment.data?.payment.number ?? '…'}</>
  ) : (
    <>Credit note {creditNote.data?.number ?? '…'}</>
  );
}

/**
 * The voucher of a refund (Prompt 10.4b, ADR 0054): to whom money was paid back, how much in figures and in words, how
 * and when, from which payment or credit note, and why. Only a posted refund has one.
 */
export function PrintedRefundPage() {
  const { id = '' } = useParams();
  const refund = useRefund(id);
  const letterhead = useLetterhead();

  if (refund.data === undefined || letterhead.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a refund voucher" back={{ to: `/refunds/${id}`, label: 'the refund' }} />
        <LoadState error={refund.error ?? letterhead.error} what="the refund" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print the voucher of ${refund.data.number ?? 'a refund'}`} />
      {refund.data.status === 'Posted' ? (
        <RefundSheet refund={refund.data} letterhead={letterhead.data} />
      ) : (
        <Banner tone="warning" title="This refund has no voucher">
          Only a posted refund is printed: this one is a draft, waits for approval, or was voided.
        </Banner>
      )}
    </section>
  );
}

function RefundSheet({ refund, letterhead }: { refund: Refund; letterhead: Letterhead }) {
  const payer = usePayer(refund.payerId);

  return (
    <PrintSheet
      kind="refunds"
      id={refund.id}
      back={{ to: `/refunds/${refund.id}`, label: 'the refund' }}
      title="Refund Voucher"
      letterhead={letterhead}
    >
      <dl className={styles.facts}>
        <div>
          <dt>Voucher number</dt>
          <dd>{refund.number}</dd>
        </div>
        <div>
          <dt>Date</dt>
          <dd>{formatDate(refund.refundDate)}</dd>
        </div>
        <div>
          <dt>Paid to</dt>
          <dd>{payer.data?.name ?? '…'}</dd>
        </div>
        <div>
          <dt>Paid by</dt>
          <dd>
            {methodLabels[refund.method]}
            {refund.reference !== null && `, reference ${refund.reference}`}
          </dd>
        </div>
        <div>
          <dt>Refunded from</dt>
          <dd>
            <Source refund={refund} />
          </dd>
        </div>
        <div>
          <dt>Reason</dt>
          <dd>{refund.reason}</dd>
        </div>
      </dl>

      <dl className={styles.totals}>
        <div className={styles.grand}>
          <dt>Amount refunded</dt>
          <dd>{formatMoney(refund.amount)}</dd>
        </div>
      </dl>
      <p className={styles.words}>
        <strong>In words:</strong> {amountInWords(paiseOfServer(refund.amount))}
      </p>
      <p className={styles.small}>Received the amount above.</p>
      <p className={styles.small}>Signature of the receiver: ______________________</p>
    </PrintSheet>
  );
}
