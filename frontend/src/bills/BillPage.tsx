import { Link, useParams } from 'react-router';
import { adjustmentStatusLabels, kindLabels, useAdjustments } from '../adjustments/api';
import { ReviewLink } from '../approvals/ReviewLink';
import { useCan } from '../auth/permissions';
import { Badge } from '../components/Badge';
import { AuditTrail } from '../components/AuditTrail';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { creditNoteStatusLabels, useCreditNotes } from '../creditNotes/api';
import { formatDate, formatDecimal, formatInstant, formatPercent } from '../format';
import { usePatient } from '../patients/api';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import { useBill, type Bill } from './api';
import { BillStatusBadge } from './BillStatusBadge';
import styles from './Bills.module.css';
import { useBillActions } from './useBillActions';

/** What a bill without a number is called: a draft, one waiting for approval, or a voided draft. */
const unnumbered: Record<Bill['status'], string> = {
  Draft: 'Draft bill',
  PendingApproval: 'Bill waiting for approval',
  Voided: 'Voided draft',
  Posted: 'Bill',
  PartiallyPaid: 'Bill',
  Settled: 'Bill',
};

/** One bill (US-20, US-21, US-23): its status, number, parties, lines, the server's totals, its balance and its trail. */
export function BillPage() {
  const { id = '' } = useParams();
  const bill = useBill(id);

  return bill.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Bill" back={{ to: '/bills', label: 'Bills' }} />
      <LoadState error={bill.error} what="the bill" />
    </section>
  ) : (
    <BillView bill={bill.data} />
  );
}

/** The bill statuses that are posted: a credit note may correct them, an adjustment lower those that still owe. */
const posted: readonly Bill['status'][] = ['Posted', 'PartiallyPaid', 'Settled'];

function BillView({ bill }: { bill: Bill }) {
  const canDraft = useCan('WriteBills');
  const canCorrect = useCan('DraftCreditNotesAndAdjustments');
  const isPosted = posted.includes(bill.status);
  const owes = paiseOfServer(bill.balanceDue) > 0n;
  const patient = usePatient(bill.patientId);
  const payer = usePayer(bill.payerId);
  const draftActions = useBillActions(bill);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={bill.number ?? unnumbered[bill.status]}
        back={{ to: '/bills', label: 'Bills' }}
        subtitle={
          <>
            {bill.fiscalYear !== null && <>Fiscal year {bill.fiscalYear} · </>}
            <BillStatusBadge status={bill.status} />
          </>
        }
        actions={
          canDraft && bill.status === 'Draft' ? (
            <>
              <Link className={buttons.secondary} to={`/bills/${bill.id}/edit`}>
                Edit
              </Link>
              {draftActions.actions}
            </>
          ) : (
            canCorrect &&
            isPosted && (
              <>
                <Link className={buttons.secondary} to={`/credit-notes/new?billId=${bill.id}`}>
                  Draft a credit note
                </Link>
                {owes && (
                  <Link className={buttons.secondary} to={`/adjustments/new?billId=${bill.id}`}>
                    Draft an adjustment
                  </Link>
                )}
              </>
            )
          )
        }
      />
      {draftActions.notices}
      {bill.status === 'PendingApproval' && (
        <p className={forms.note} role="status">
          This bill waits for approval: a Supervisor or an administrator other than the one who sent it posts it or
          returns it to draft. Until then it cannot change. <ReviewLink documentType="Bill" />
        </p>
      )}
      {bill.status === 'Voided' && (
        <p className={forms.note}>
          Voided {formatInstant(bill.voidedAtUtc)}: {bill.voidReason}
        </p>
      )}
      <Details
        items={[
          [
            'Patient',
            patient.data === undefined ? (
              '…'
            ) : (
              <Link to={`/patients/${bill.patientId}`}>
                {patient.data.fullName} (MRN {patient.data.mrn})
              </Link>
            ),
          ],
          ['Payer', payer.data === undefined ? '…' : <Link to={`/payers/${bill.payerId}`}>{payer.data.name}</Link>],
          ['Bill date', formatDate(bill.billDate)],
          ['Due date', formatDate(bill.dueDate)],
          ['Encounter reference', bill.encounterReference],
          ['Posted', formatInstant(bill.postedAtUtc)],
          ['Notes', bill.notes],
        ]}
      />

      <section className={forms.section} aria-labelledby="lines-title">
        <h2 id="lines-title">Lines</h2>
        {bill.lines.length === 0 ? (
          <p className={table.empty}>This draft has no lines yet.</p>
        ) : (
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">#</th>
                  <th scope="col">Description</th>
                  <th scope="col" className={table.number}>
                    Quantity
                  </th>
                  <th scope="col" className={table.number}>
                    Unit price (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Discount (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Tax
                  </th>
                  <th scope="col" className={table.number}>
                    Tax (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Line total (₹)
                  </th>
                </tr>
              </thead>
              <tbody>
                {bill.lines.map((line) => (
                  <tr key={line.id}>
                    <td>{String(line.lineNo)}</td>
                    <td>
                      {line.description}
                      {line.serviceItemId !== null && <span className={styles.fromCatalogue}> · catalogue</span>}
                    </td>
                    <td className={table.number}>{String(line.quantity)}</td>
                    <td className={table.number}>{formatDecimal(line.unitPrice, 4)}</td>
                    <td className={table.number}>{formatDecimal(line.lineAmount)}</td>
                    <td className={table.number}>{formatDecimal(line.discountAmount)}</td>
                    <td className={table.number}>{formatPercent(line.taxRatePercent)}</td>
                    <td className={table.number}>{formatDecimal(line.taxAmount)}</td>
                    <td className={table.number}>{formatDecimal(line.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <dl className={styles.totals}>
          <div>
            <dt>Subtotal</dt>
            <dd>₹{formatDecimal(bill.subTotal)}</dd>
          </div>
          <div>
            <dt>Discounts</dt>
            <dd>₹{formatDecimal(bill.discountTotal)}</dd>
          </div>
          <div>
            <dt>Tax</dt>
            <dd>₹{formatDecimal(bill.taxTotal)}</dd>
          </div>
          <div className={styles.grand}>
            <dt>Total</dt>
            <dd>₹{formatDecimal(bill.total)}</dd>
          </div>
          <div className={styles.grand}>
            <dt>Balance due</dt>
            <dd>₹{formatDecimal(bill.balanceDue)}</dd>
          </div>
        </dl>
      </section>

      {isPosted && <BillCorrections bill={bill} />}

      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="Bill" entityId={bill.id} />
      </section>
    </section>
  );
}

/** The credit notes and adjustments of a posted bill (ADR 0042): what corrected it, and what waits to. */
function BillCorrections({ bill }: { bill: Bill }) {
  const canReadAdjustments = useCan('ReadAdjustments');
  const creditNotes = useCreditNotes({ status: undefined, billId: bill.id, payerId: '', page: 1 });

  return (
    <section className={forms.section} aria-labelledby="corrections-title">
      <h2 id="corrections-title">Credit notes and adjustments</h2>
      {creditNotes.data === undefined ? (
        <LoadState error={creditNotes.error} what="the credit notes" />
      ) : creditNotes.data.items.length === 0 ? (
        <p className={table.empty}>No credit note corrects this bill.</p>
      ) : (
        <ul className={styles.corrections} aria-label="Credit notes of the bill">
          {creditNotes.data.items.map((creditNote) => (
            <li key={creditNote.id}>
              <Link to={`/credit-notes/${creditNote.id}`}>{creditNote.number ?? 'Credit note without a number'}</Link> ·
              ₹{formatDecimal(creditNote.total)}{' '}
              <Badge tone={creditNoteStatusLabels[creditNote.status].tone}>
                {creditNoteStatusLabels[creditNote.status].label}
              </Badge>
            </li>
          ))}
        </ul>
      )}
      {canReadAdjustments && <BillAdjustments billId={bill.id} />}
    </section>
  );
}

function BillAdjustments({ billId }: { billId: string }) {
  const adjustments = useAdjustments({ status: undefined, kind: undefined, billId, payerId: '', page: 1 });

  return adjustments.data === undefined ? (
    <LoadState error={adjustments.error} what="the adjustments" />
  ) : adjustments.data.items.length === 0 ? (
    <p className={table.empty}>No adjustment lowers this bill.</p>
  ) : (
    <ul className={styles.corrections} aria-label="Adjustments of the bill">
      {adjustments.data.items.map((adjustment) => (
        <li key={adjustment.id}>
          <Link to={`/adjustments/${adjustment.id}`}>{adjustment.number ?? 'Adjustment without a number'}</Link> ·{' '}
          {kindLabels[adjustment.kind]} of ₹{formatDecimal(adjustment.amount)}{' '}
          <Badge tone={adjustmentStatusLabels[adjustment.status].tone}>
            {adjustmentStatusLabels[adjustment.status].label}
          </Badge>
        </li>
      ))}
    </ul>
  );
}
