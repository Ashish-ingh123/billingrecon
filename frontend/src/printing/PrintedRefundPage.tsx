import { useParams } from 'react-router';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { useCreditNote } from '../creditNotes/api';
import { usePayer } from '../payers/api';
import { usePaymentWithAllocations } from '../payments/api';
import { useRefund, type Refund } from '../refunds/api';
import { stillReading, useLetterhead, usePatientOfPayer, useRecorder, type Letterhead } from './api';
import { PrintSheet } from './PrintSheet';
import { RefundBody } from './RefundBody';
import { settingsOf, usePrintSettings, type PrintLayout } from './settings';

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
 * and when, from which payment or credit note, and why. Only a posted refund has one. Since Prompt 10.4c it is laid out
 * by the hospital's print settings for a refund voucher (ADR 0059).
 */
export function PrintedRefundPage() {
  const { id = '' } = useParams();
  const refund = useRefund(id);
  const letterhead = useLetterhead();
  const settings = usePrintSettings();

  if (refund.data === undefined || letterhead.data === undefined || settings.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a refund voucher" back={{ to: `/refunds/${id}`, label: 'the refund' }} />
        <LoadState error={refund.error ?? letterhead.error ?? settings.error} what="the refund" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print the voucher of ${refund.data.number ?? 'a refund'}`} />
      {refund.data.status === 'Posted' ? (
        <RefundSheet
          refund={refund.data}
          letterhead={letterhead.data}
          layout={settingsOf(settings.data, 'Refund').layout}
        />
      ) : (
        <Banner tone="warning" title="This refund has no voucher">
          Only a posted refund is printed: this one is a draft, waits for approval, or was voided.
        </Banner>
      )}
    </section>
  );
}

function RefundSheet({ refund, letterhead, layout }: { refund: Refund; letterhead: Letterhead; layout: PrintLayout }) {
  const payer = usePayer(refund.payerId);
  // Read only when the settings ask for them, and waited for before a print.
  const recorder = useRecorder('refunds', refund.id, layout.showCashierName);
  const patient = usePatientOfPayer(payer.data?.patientId, layout.showPatientContact);

  return (
    <PrintSheet
      kind="refunds"
      id={refund.id}
      back={{ to: `/refunds/${refund.id}`, label: 'the refund' }}
      title="Refund Voucher"
      letterhead={letterhead}
      layout={layout}
      waiting={stillReading(recorder) || stillReading(patient)}
    >
      <RefundBody
        refund={refund}
        layout={layout}
        payerName={payer.data?.name}
        contact={patient.data}
        cashierName={recorder.data?.name}
        source={<Source refund={refund} />}
      />
    </PrintSheet>
  );
}
