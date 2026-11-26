import { useParams } from 'react-router';
import { useBill, type Bill } from '../bills/api';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { usePatient } from '../patients/api';
import { usePayer } from '../payers/api';
import { useLetterhead, type Letterhead } from './api';
import { BillBody } from './BillBody';
import { PrintSheet } from './PrintSheet';
import { settingsOf, usePrintSettings, type PrintLayout } from './settings';
import { billDocumentKind, billDocumentTitles } from './tax';

const printable: readonly Bill['status'][] = ['Posted', 'PartiallyPaid', 'Settled'];

/**
 * The printed bill (Prompt 10.4b, ADR 0054): a tax invoice when the bill carries tax, with each line's stored tax shown
 * in its central and state halves; a bill of supply with the exempt wording when it carries none; the statement of an
 * opening balance for a bill brought in from the system before. Every figure is the stored one. Since Prompt 10.4c it is
 * laid out by the hospital's print settings for a bill (ADR 0059).
 */
export function PrintedBillPage() {
  const { id = '' } = useParams();
  const bill = useBill(id);
  const letterhead = useLetterhead();
  const settings = usePrintSettings();

  if (bill.data === undefined || letterhead.data === undefined || settings.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a bill" back={{ to: `/bills/${id}`, label: 'the bill' }} />
        <LoadState error={bill.error ?? letterhead.error ?? settings.error} what="the bill" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print ${bill.data.number ?? 'a bill'}`} />
      {printable.includes(bill.data.status) ? (
        <BillSheet bill={bill.data} letterhead={letterhead.data} layout={settingsOf(settings.data, 'Bill').layout} />
      ) : (
        <Banner tone="warning" title="This bill is not printed">
          Only a posted bill is handed to a patient: this one is a draft, waits for approval, or was voided.
        </Banner>
      )}
    </section>
  );
}

function BillSheet({ bill, letterhead, layout }: { bill: Bill; letterhead: Letterhead; layout: PrintLayout }) {
  const patient = usePatient(bill.patientId);
  const payer = usePayer(bill.payerId);

  return (
    <PrintSheet
      kind="bills"
      id={bill.id}
      back={{ to: `/bills/${bill.id}`, label: 'the bill' }}
      title={billDocumentTitles[billDocumentKind(bill)]}
      letterhead={letterhead}
      layout={layout}
    >
      <BillBody
        bill={bill}
        letterhead={letterhead}
        layout={layout}
        patient={patient.data}
        payerName={payer.data?.name}
      />
    </PrintSheet>
  );
}
