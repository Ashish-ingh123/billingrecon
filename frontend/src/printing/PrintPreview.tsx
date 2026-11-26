import { useLetterheadImage, type Letterhead } from './api';
import { BillBody } from './BillBody';
import { ReceiptBody } from './ReceiptBody';
import { RefundBody } from './RefundBody';
import { sampleBill, sampleCashier, samplePatient, sampleReceipt, sampleRefund } from './samples';
import { useLayoutRules, type PrintedDocumentType, type PrintLayout } from './settings';
import { SheetFrame } from './SheetFrame';
import { billDocumentKind, billDocumentTitles } from './tax';

const titles: Record<PrintedDocumentType, string> = {
  Bill: billDocumentTitles[billDocumentKind(sampleBill)],
  Payment: 'Receipt',
  Refund: 'Refund Voucher',
};

/**
 * The preview of the print settings (ADR 0059): a sample bill, receipt or refund voucher drawn by the very components
 * that print (`SheetFrame` and the three bodies), on the hospital's own letterhead with its logo and signature, laid
 * out by the settings as they stand in the form, saved or not. What is seen here is what a print gives; only the
 * document is invented, and the mark says "Preview" where a print says Original or Duplicate.
 */
export function PrintPreview({
  documentType,
  layout,
  letterhead,
}: {
  documentType: PrintedDocumentType;
  layout: PrintLayout;
  letterhead: Letterhead;
}) {
  const logo = useLetterheadImage('logo', layout.prePrintedStationery ? null : letterhead.logoSha256);
  const signature = useLetterheadImage('signature', layout.showSignatureBlock ? letterhead.signatureSha256 : null);
  useLayoutRules(layout, 'preview');

  return (
    <SheetFrame
      preview
      title={titles[documentType]}
      mark="Preview"
      letterhead={letterhead}
      layout={layout}
      logo={logo.data}
      signature={signature.data}
    >
      {documentType === 'Bill' && (
        <BillBody
          bill={sampleBill}
          letterhead={letterhead}
          layout={layout}
          patient={samplePatient}
          payerName={samplePatient.fullName}
        />
      )}
      {documentType === 'Payment' && (
        <ReceiptBody
          record={sampleReceipt}
          letterhead={letterhead}
          layout={layout}
          payerName={samplePatient.fullName}
          contact={samplePatient}
          cashierName={sampleCashier}
          billNumber={() => sampleBill.number}
        />
      )}
      {documentType === 'Refund' && (
        <RefundBody
          refund={sampleRefund}
          layout={layout}
          payerName={samplePatient.fullName}
          contact={samplePatient}
          cashierName={sampleCashier}
          source={<>Payment {sampleReceipt.payment.number}</>}
        />
      )}
    </SheetFrame>
  );
}
