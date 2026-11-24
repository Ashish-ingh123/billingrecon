import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { ReviewLink } from '../approvals/ReviewLink';
import { useSession } from '../auth/AuthContext';
import { useCan } from '../auth/permissions';
import { useBill } from '../bills/api';
import { AuditTrail } from '../components/AuditTrail';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { ConfirmAction } from '../components/ConfirmAction';
import table from '../components/DataTable.module.css';
import { Banner } from '../components/Banner';
import { Details } from '../components/Details';
import summary from '../components/Summary.module.css';
import { TotalsCard } from '../components/TotalsCard';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ReasonForm } from '../components/ReasonForm';
import { useDocumentAction } from '../components/useDocumentAction';
import { formatDate, formatDecimal, formatInstant } from '../format';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import {
  applicationStatusLabels,
  comparedCreditNoteFields,
  creditNoteKeys,
  creditNotesChanged,
  creditNoteStatusLabels,
  getCreditNote,
  requestApplicationReversal,
  submitCreditNote,
  useCreditNote,
  voidCreditNote,
  type CreditApplication,
  type CreditNote,
} from './api';

/** What a credit note without a number is called, by its state. */
const unnumbered: Record<CreditNote['status'], string> = {
  Draft: 'Draft credit note',
  PendingApproval: 'Credit note waiting for approval',
  Voided: 'Voided draft credit note',
  Posted: 'Credit note',
};

/** One credit note (US-24, BR-209 to BR-214): its bill, lines and totals, the credit it applied and what is left. */
export function CreditNotePage() {
  const { id = '' } = useParams();
  const creditNote = useCreditNote(id);

  return creditNote.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Credit note" back={{ to: '/credit-notes', label: 'Credit notes' }} />
      <LoadState error={creditNote.error} what="the credit note" />
    </section>
  ) : (
    <CreditNoteView creditNote={creditNote.data} />
  );
}

function BillNumber({ id }: { id: string }) {
  const bill = useBill(id);
  return <Link to={`/bills/${id}`}>{bill.data?.number ?? 'the bill'}</Link>;
}

function CreditNoteView({ creditNote }: { creditNote: CreditNote }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const session = useSession();
  const canDraft = useCan('DraftCreditNotesAndAdjustments');
  const canAskReversal = useCan('RequestReversals');
  const canRefund = useCan('DraftRefunds');
  const payer = usePayer(creditNote.payerId);
  // What is being asked for: 'void', or the id of an application to reverse; null while nothing is.
  const [asking, setAsking] = useState<string | null>(null);

  const show = (changed: CreditNote) => {
    queryClient.setQueryData(creditNoteKeys.one(changed.id), changed);
    creditNotesChanged(queryClient);
  };

  const action = useDocumentAction<CreditNote, CreditNote>({
    current: creditNote,
    compared: comparedCreditNoteFields,
    reload: () => getCreditNote(client, creditNote.id),
    onDone: (changed) => {
      setAsking(null);
      show(changed);
    },
    onReloaded: show,
  });

  const submit = () =>
    action.run({
      identity: `POST /credit-notes/${creditNote.id}/post ${creditNote.version}`,
      verb: 'submit',
      send: (key) => submitCreditNote(client, creditNote, key),
      done: () => 'Submitted: the credit note waits for a Supervisor or an administrator, who posts it.',
    });

  const voidDraft = (reason: string) =>
    action.run({
      identity: `POST /credit-notes/${creditNote.id}/void ${creditNote.version} ${JSON.stringify(reason)}`,
      verb: 'void',
      send: (key) => voidCreditNote(client, creditNote, reason, key),
      done: () => 'The draft credit note is voided.',
    });

  const askReversal = (application: CreditApplication, reason: string) =>
    action.run({
      identity: `POST /credit-notes/${creditNote.id}/applications/${application.id}/reverse ${creditNote.version} ${JSON.stringify(reason)}`,
      verb: 'request the reversal of',
      send: (key) => requestApplicationReversal(client, creditNote, application, reason, key),
      done: () => 'The reversal is asked for: the application waits for a Supervisor or an administrator to decide.',
    });

  const posted = creditNote.status === 'Posted';
  const holdsCredit = paiseOfServer(creditNote.availableAmount) > 0n;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={creditNote.number ?? unnumbered[creditNote.status]}
        back={{ to: '/credit-notes', label: 'Credit notes' }}
        subtitle={
          <>
            {creditNote.fiscalYear !== null && <>Fiscal year {creditNote.fiscalYear} · </>}
            <Badge
              tone={creditNoteStatusLabels[creditNote.status].tone}
              icon={creditNoteStatusLabels[creditNote.status].icon}
            >
              {creditNoteStatusLabels[creditNote.status].label}
            </Badge>
          </>
        }
        actions={
          asking === null && (
            <>
              {canDraft && creditNote.status === 'Draft' && (
                <>
                  <Link className={buttons.secondary} to={`/credit-notes/${creditNote.id}/edit`}>
                    Edit
                  </Link>
                  <ConfirmAction
                    label="Submit for approval"
                    confirmLabel="Confirm: submit the credit note"
                    busy={action.pending}
                    onConfirm={submit}
                  />
                  <button type="button" className={buttons.danger} onClick={() => setAsking('void')}>
                    Void draft
                  </button>
                </>
              )}
              {posted && holdsCredit && canDraft && (
                <Link className={buttons.primary} to={`/credit-notes/${creditNote.id}/apply`}>
                  Apply credit to bills
                </Link>
              )}
              {posted && holdsCredit && canRefund && (
                <Link className={buttons.secondary} to={`/refunds/new?creditNoteId=${creditNote.id}`}>
                  Draft a refund
                </Link>
              )}
            </>
          )
        }
      />
      {action.notices}
      {asking === 'void' && (
        <ReasonForm
          label="Why is the draft credit note voided?"
          confirmLabel="Confirm: void the draft"
          busy={action.pending}
          onConfirm={voidDraft}
          onCancel={() => setAsking(null)}
        />
      )}
      {creditNote.status === 'PendingApproval' && (
        <p className={forms.note} role="status">
          This credit note waits for approval: a Supervisor or an administrator other than the one who submitted it
          posts it or returns it to draft. <ReviewLink documentType="CreditNote" />
        </p>
      )}
      {creditNote.status === 'Voided' && (
        <Banner tone="danger" title="Voided">
          Voided {formatInstant(creditNote.voidedAtUtc)}: {creditNote.voidReason}
        </Banner>
      )}
      <div className={summary.split}>
        <Details
          items={[
            ['Bill', <BillNumber key="bill" id={creditNote.billId} />],
            [
              'Payer',
              payer.data === undefined ? '…' : <Link to={`/payers/${creditNote.payerId}`}>{payer.data.name}</Link>,
            ],
            ['Credit note date', formatDate(creditNote.creditNoteDate)],
            ['Reason', creditNote.reason],
            ['Posted', formatInstant(creditNote.postedAtUtc)],
          ]}
        />
        <div>
          <TotalsCard
            label="Figures of the credit note"
            rows={[
              { label: 'Total', value: creditNote.total, strong: true },
              ...(posted ? [{ label: 'Available credit', value: creditNote.availableAmount, apart: true }] : []),
            ]}
          />
        </div>
      </div>

      <section className={forms.section} aria-labelledby="lines-title">
        <h2 id="lines-title">Lines</h2>
        {creditNote.lines.length === 0 ? (
          <p className={table.empty}>No lines yet.</p>
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
                    Discount (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Tax (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Total (₹)
                  </th>
                </tr>
              </thead>
              <tbody>
                {creditNote.lines.map((line) => (
                  <tr key={line.lineNo}>
                    <td>{line.lineNo}</td>
                    <td>
                      {line.description}
                      {line.billLineId !== null && <span className={forms.note}> · a line of the bill</span>}
                    </td>
                    <td className={table.number}>{formatDecimal(line.quantity, 3)}</td>
                    <td className={table.number}>{formatDecimal(line.unitPrice, 4)}</td>
                    <td className={table.number}>{formatDecimal(line.discountAmount)}</td>
                    <td className={table.number}>{formatDecimal(line.taxAmount)}</td>
                    <td className={table.number}>{formatDecimal(line.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th scope="row" colSpan={6}>
                    Total
                  </th>
                  <td className={table.number}>{formatDecimal(creditNote.total)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </section>

      {posted && (
        <section className={forms.section} aria-labelledby="applications-title">
          <h2 id="applications-title">Credit applied</h2>
          {creditNote.applications.length === 0 ? (
            <p className={table.empty}>None of this credit is applied to a bill.</p>
          ) : (
            <div className={table.wrap}>
              <table className={table.table}>
                <thead>
                  <tr>
                    <th scope="col">Bill</th>
                    <th scope="col" className={table.number}>
                      Amount (₹)
                    </th>
                    <th scope="col">Applied</th>
                    <th scope="col">By</th>
                    <th scope="col">Status</th>
                    <th scope="col">
                      <span className={forms.note}>Reversal</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {creditNote.applications.map((application) => (
                    <tr key={application.id}>
                      <td>
                        <BillNumber id={application.billId} />
                      </td>
                      <td className={table.number}>{formatDecimal(application.amount)}</td>
                      <td>
                        {formatInstant(application.allocatedAtUtc)}
                        {application.appliedAtPosting && <span className={forms.note}> · at posting</span>}
                      </td>
                      <td>
                        {application.allocatedByUserId === null
                          ? 'The system'
                          : application.allocatedByUserId === session.userId
                            ? 'You'
                            : application.allocatedByUserId.slice(0, 8)}
                      </td>
                      <td>
                        <Badge
                          tone={applicationStatusLabels[application.status].tone}
                          icon={applicationStatusLabels[application.status].icon}
                        >
                          {applicationStatusLabels[application.status].label}
                        </Badge>
                      </td>
                      <td>
                        {application.status === 'Reversed' ? (
                          application.reversalReason
                        ) : application.status === 'PendingReversal' ? (
                          <ReviewLink documentType="CreditNoteAllocation" />
                        ) : application.appliedAtPosting ? (
                          <span className={forms.note}>Made by the posting; not reversed.</span>
                        ) : canAskReversal ? (
                          asking === application.id ? (
                            <ReasonForm
                              label="Why should this application of credit be reversed?"
                              confirmLabel="Ask for the reversal"
                              busy={action.pending}
                              onConfirm={(reason) => askReversal(application, reason)}
                              onCancel={() => setAsking(null)}
                            />
                          ) : (
                            asking === null && (
                              <button type="button" className={buttons.link} onClick={() => setAsking(application.id)}>
                                Request reversal
                              </button>
                            )
                          )
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="CreditNote" entityId={creditNote.id} />
      </section>
    </section>
  );
}
