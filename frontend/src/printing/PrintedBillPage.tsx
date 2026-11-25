import { useParams } from 'react-router';
import { useBill, type Bill } from '../bills/api';
import { Banner } from '../components/Banner';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal, formatMoney } from '../format';
import { asRupees, paiseOfServer } from '../money';
import { usePatient } from '../patients/api';
import { usePayer } from '../payers/api';
import { useLetterhead, type Letterhead } from './api';
import styles from './Printing.module.css';
import { PrintSheet } from './PrintSheet';
import { billDocumentKind, billDocumentTitles, exemptWording, halfRate, splitTax } from './tax';
import { amountInWords } from './words';

const printable: readonly Bill['status'][] = ['Posted', 'PartiallyPaid', 'Settled'];
const rupees = (paise: bigint) => formatMoney(asRupees(paise));

/**
 * The printed bill (Prompt 10.4b, ADR 0054): a tax invoice when the bill carries tax, with each line's stored tax shown
 * in its central and state halves; a bill of supply with the exempt wording when it carries none; the statement of an
 * opening balance for a bill brought in from the system before. Every figure is the stored one.
 */
export function PrintedBillPage() {
  const { id = '' } = useParams();
  const bill = useBill(id);
  const letterhead = useLetterhead();

  if (bill.data === undefined || letterhead.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Print a bill" back={{ to: `/bills/${id}`, label: 'the bill' }} />
        <LoadState error={bill.error ?? letterhead.error} what="the bill" />
      </section>
    );
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Print ${bill.data.number ?? 'a bill'}`} />
      {printable.includes(bill.data.status) ? (
        <BillSheet bill={bill.data} letterhead={letterhead.data} />
      ) : (
        <Banner tone="warning" title="This bill is not printed">
          Only a posted bill is handed to a patient: this one is a draft, waits for approval, or was voided.
        </Banner>
      )}
    </section>
  );
}

function BillSheet({ bill, letterhead }: { bill: Bill; letterhead: Letterhead }) {
  const patient = usePatient(bill.patientId);
  const payer = usePayer(bill.payerId);
  const kind = billDocumentKind(bill);
  const taxed = kind === 'TaxInvoice';
  const halves = bill.lines.map((line) => splitTax(paiseOfServer(line.taxAmount)));
  const cgst = halves.reduce((sum, half) => sum + half.cgst, 0n);
  const sgst = halves.reduce((sum, half) => sum + half.sgst, 0n);

  return (
    <PrintSheet
      kind="bills"
      id={bill.id}
      back={{ to: `/bills/${bill.id}`, label: 'the bill' }}
      title={billDocumentTitles[kind]}
      letterhead={letterhead}
    >
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
          <dd>{patient.data === undefined ? '…' : `${patient.data.fullName} (MRN ${patient.data.mrn})`}</dd>
        </div>
        <div>
          <dt>Billed to</dt>
          <dd>{payer.data?.name ?? '…'}</dd>
        </div>
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
              {kind !== 'OpeningBalance' && <th scope="col">SAC</th>}
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
                {kind !== 'OpeningBalance' && <td>{line.sacCode ?? ''}</td>}
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

      <p className={styles.words}>
        <strong>In words:</strong> {amountInWords(paiseOfServer(bill.total))}
      </p>
      {kind === 'BillOfSupply' && <p className={styles.small}>{exemptWording}</p>}
      {kind === 'OpeningBalance' && (
        <p className={styles.small}>
          This is the balance still owed on a bill raised before {letterhead.name} began billing in this system. It is a
          statement of that balance, not a new bill.
        </p>
      )}
    </PrintSheet>
  );
}
