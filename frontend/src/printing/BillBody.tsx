import type { Bill } from '../bills/api';
import { formatDate, formatDecimal, formatMoney } from '../format';
import { asRupees, paiseOfServer } from '../money';
import type { Letterhead } from './api';
import { ContactFacts } from './Contact';
import type { PatientContact } from './patientContact';
import styles from './Printing.module.css';
import type { PrintLayout } from './settings';
import { billDocumentKind, exemptWording, halfRate, splitTax } from './tax';
import { amountInWords } from './words';

const rupees = (paise: bigint) => formatMoney(asRupees(paise));

/**
 * What a printed bill says between its head and its foot (ADR 0054; laid out by the settings of ADR 0059): a tax
 * invoice when the bill carries tax, with each line's stored tax shown in its central and state halves; a bill of
 * supply with the exempt wording when it carries none; the statement of an opening balance for a bill brought in from
 * the system before. Every figure is the stored one, whatever the settings: they choose what is shown, never what it is.
 *
 * A bill that carries tax always shows its SAC column and its tax in its halves; the settings cannot switch them off
 * (the API refuses it too), and this component does not ask them.
 */
export function BillBody({
  bill,
  letterhead,
  layout,
  patient,
  payerName,
}: {
  bill: Bill;
  letterhead: Letterhead;
  layout: PrintLayout;
  patient: ({ fullName: string; mrn: string } & PatientContact) | undefined;
  payerName: string | undefined;
}) {
  const kind = billDocumentKind(bill);
  const taxed = kind === 'TaxInvoice';
  const sacColumn = kind !== 'OpeningBalance' && (taxed || layout.showSacColumn);
  const halves = bill.lines.map((line) => splitTax(paiseOfServer(line.taxAmount)));
  const cgst = halves.reduce((sum, half) => sum + half.cgst, 0n);
  const sgst = halves.reduce((sum, half) => sum + half.sgst, 0n);

  return (
    <>
      <dl className={styles.facts}>
        <div>
          <dt>{kind === 'OpeningBalance' ? 'Reference' : 'Number'}</dt>
          <dd>{bill.number}</dd>
        </div>
        <div>
          <dt>Date</dt>
          <dd>{formatDate(bill.billDate)}</dd>
        </div>
        {bill.legacyNumber !== null && (
          <div>
            <dt>Earlier bill number</dt>
            <dd>{bill.legacyNumber}</dd>
          </div>
        )}
        <div>
          <dt>Due</dt>
          <dd>{formatDate(bill.dueDate)}</dd>
        </div>
        <div>
          <dt>Patient</dt>
          <dd>{patient === undefined ? '…' : `${patient.fullName} (MRN ${patient.mrn})`}</dd>
        </div>
        {layout.showPatientContact && <ContactFacts contact={patient} />}
        {layout.showPayer && (
          <div>
            <dt>Billed to</dt>
            <dd>{payerName ?? '…'}</dd>
          </div>
        )}
        {bill.encounterReference !== null && (
          <div>
            <dt>Visit</dt>
            <dd>{bill.encounterReference}</dd>
          </div>
        )}
        {letterhead.state !== null && kind !== 'OpeningBalance' && (
          <div>
            <dt>Place of supply</dt>
            <dd>{letterhead.state}</dd>
          </div>
        )}
      </dl>

      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Description</th>
              {sacColumn && <th scope="col">SAC</th>}
              <th scope="col" className={styles.number}>
                Qty
              </th>
              <th scope="col" className={styles.number}>
                Rate
              </th>
              <th scope="col" className={styles.number}>
                Discount
              </th>
              {taxed && (
                <>
                  <th scope="col" className={styles.number}>
                    CGST
                  </th>
                  <th scope="col" className={styles.number}>
                    SGST
                  </th>
                </>
              )}
              <th scope="col" className={styles.number}>
                Amount
              </th>
            </tr>
          </thead>
          <tbody>
            {bill.lines.map((line, index) => (
              <tr key={line.id}>
                <td>{line.lineNo}</td>
                <td>{line.description}</td>
                {sacColumn && <td>{line.sacCode ?? ''}</td>}
                <td className={styles.number}>{formatDecimal(line.quantity, 3)}</td>
                <td className={styles.number}>{formatDecimal(line.unitPrice, 4)}</td>
                <td className={styles.number}>{formatMoney(line.discountAmount)}</td>
                {taxed && (
                  <>
                    <td className={styles.number}>
                      {rupees(halves[index]?.cgst ?? 0n)} ({halfRate(line.taxRatePercent)}%)
                    </td>
                    <td className={styles.number}>
                      {rupees(halves[index]?.sgst ?? 0n)} ({halfRate(line.taxRatePercent)}%)
                    </td>
                  </>
                )}
                <td className={styles.number}>{formatMoney(line.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <dl className={styles.totals}>
        <div>
          <dt>Subtotal</dt>
          <dd>{formatMoney(bill.subTotal)}</dd>
        </div>
        <div>
          <dt>Discount</dt>
          <dd>{formatMoney(bill.discountTotal)}</dd>
        </div>
        {taxed && (
          <>
            <div>
              <dt>CGST</dt>
              <dd>{rupees(cgst)}</dd>
            </div>
            <div>
              <dt>SGST</dt>
              <dd>{rupees(sgst)}</dd>
            </div>
          </>
        )}
        <div className={styles.grand}>
          <dt>Total</dt>
          <dd>{formatMoney(bill.total)}</dd>
        </div>
      </dl>

      {layout.showAmountInWords && (
        <p className={styles.words}>
          <strong>In words:</strong> {amountInWords(paiseOfServer(bill.total))}
        </p>
      )}
      {kind === 'BillOfSupply' && <p className={styles.small}>{exemptWording}</p>}
      {kind === 'OpeningBalance' && (
        <p className={styles.small}>
          This is the balance still owed on a bill raised before {letterhead.name} began billing in this system. It is a
          statement of that balance, not a new bill.
        </p>
      )}
    </>
  );
}
