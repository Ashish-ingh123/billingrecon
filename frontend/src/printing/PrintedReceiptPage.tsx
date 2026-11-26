import { useParams } from 'react-router';
import { useBill } from '../bills/api';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { usePayer } from '../payers/api';
import { usePaymentWithAllocations, type PaymentWithAllocations } from '../payments/api';
import { stillReading, useLetterhead, usePatientOfPayer, useRecorder, type Letterhead } from './api';
import { PrintSheet } from './PrintSheet';
import { ReceiptBody } from './ReceiptBody';
import { settingsOf, usePrintSettings, type PrintLayout } from './settings';

function BillNumber({ id }: { id: string }) {
  const bill = useBill(id);
  return <>{bill.data?.number ?? '…'}</>;
}

/**
 * The receipt of a payment (Prompt 10.4b, ADR 0054): from whom, how much in figures and in words, how and when it was
 * paid, and the bills it was applied to with what is left unapplied. A reversed payment has no receipt. Since Prompt
 * 10.4c it is laid out by the hospital's print settings for a receipt (ADR 0059).
 */
export function PrintedReceiptPage() {
  const { id = '' } = useParams();
  const payment = usePaymentWithAllocations(id);
  const letterhead = useLetterhead();
  const settings = usePrintSettings();

  if (payment.data === undefined || letterhead.data === undefined || settings.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a receipt" back={{ to: `/payments/${id}`, label: 'the payment' }} />
        <LoadState error={payment.error ?? letterhead.error ?? settings.error} what="the payment" />
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
        <ReceiptSheet
          record={payment.data}
          letterhead={letterhead.data}
          layout={settingsOf(settings.data, 'Payment').layout}
        />
      )}
    </section>
  );
}

function ReceiptSheet({
  record,
  letterhead,
  layout,
}: {
  record: PaymentWithAllocations;
  letterhead: Letterhead;
  layout: PrintLayout;
}) {
  const { payment } = record;
  const payer = usePayer(payment.payerId);
  // Read only when the settings ask for them, and waited for before a print.
  const recorder = useRecorder('payments', payment.id, layout.showCashierName);
  const patient = usePatientOfPayer(payer.data?.patientId, layout.showPatientContact);

  return (
    <PrintSheet
      kind="payments"
      id={payment.id}
      back={{ to: `/payments/${payment.id}`, label: 'the payment' }}
      title={payment.origin === 'Opening' ? 'Statement of Opening Advance' : 'Receipt'}
      letterhead={letterhead}
      layout={layout}
      waiting={stillReading(recorder) || stillReading(patient)}
    >
      <ReceiptBody
        record={record}
        letterhead={letterhead}
        layout={layout}
        payerName={payer.data?.name}
        contact={patient.data}
        cashierName={recorder.data?.name}
        billNumber={(billId) => <BillNumber id={billId} />}
      />
    </PrintSheet>
  );
}
