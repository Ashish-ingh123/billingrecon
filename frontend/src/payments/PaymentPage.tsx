import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { useSession } from '../auth/AuthContext';
import { useCan } from '../auth/permissions';
import { useBill } from '../bills/api';
import { AuditTrail } from '../components/AuditTrail';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Banner } from '../components/Banner';
import { Details } from '../components/Details';
import summary from '../components/Summary.module.css';
import { TotalsCard } from '../components/TotalsCard';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { PrintLink } from '../printing/PrintLink';
import { ReasonForm } from '../components/ReasonForm';
import { useDocumentAction } from '../components/useDocumentAction';
import { formatDate, formatDecimal, formatInstant } from '../format';
import { paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import {
  allocationStatusLabels,
  comparedPaymentFields,
  getPaymentWithAllocations,
  methodLabels,
  paymentKeys,
  paymentStatusLabels,
  paymentsChanged,
  requestAllocationReversal,
  requestPaymentReversal,
  usePaymentWithAllocations,
  type Allocation,
  type PaymentWithAllocations,
} from './api';

/** One payment (US-30 to US-33): what was received, where it went, what is left, and the reversals asked for. */
export function PaymentPage() {
  const { id = '' } = useParams();
  const payment = usePaymentWithAllocations(id);

  return payment.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Payment" back={{ to: '/payments', label: 'Payments' }} />
      <LoadState error={payment.error} what="the payment" />
    </section>
  ) : (
    <PaymentView record={payment.data} />
  );
}

function BillNumber({ id }: { id: string }) {
  const bill = useBill(id);
  return <Link to={`/bills/${id}`}>{bill.data?.number ?? 'the bill'}</Link>;
}

function PaymentView({ record }: { record: PaymentWithAllocations }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const session = useSession();
  const payer = usePayer(record.payment.payerId);
  const canAllocate = useCan('RecordPayments');
  const canAskReversal = useCan('RequestReversals');
  const canRefund = useCan('DraftRefunds');
  // What a reversal is being asked for: 'payment', or the id of an allocation; null while nothing is.
  const [asking, setAsking] = useState<string | null>(null);
  const { payment, allocations } = record;

  const show = (changed: PaymentWithAllocations) => {
    queryClient.setQueryData(paymentKeys.one(changed.payment.id), changed);
    paymentsChanged(queryClient);
  };

  const action = useDocumentAction<PaymentWithAllocations, PaymentWithAllocations>({
    current: record,
    compared: comparedPaymentFields,
    reload: () => getPaymentWithAllocations(client, payment.id),
    onDone: (changed) => {
      setAsking(null);
      show(changed);
    },
    onReloaded: show,
  });

  const posted = payment.status === 'Posted';
  const allocationWaits = allocations.some((allocation) => allocation.status === 'PendingReversal');
  const holdsCash = paiseOfServer(payment.unappliedAmount) > 0n;

  const askPaymentReversal = (reason: string) =>
    action.run({
      identity: `POST /payments/${payment.id}/reverse ${payment.version} ${JSON.stringify(reason)}`,
      verb: 'request the reversal of',
      send: (key) => requestPaymentReversal(client, payment, reason, key),
      done: () => 'The reversal is asked for: the payment waits for a Supervisor or an administrator to decide.',
    });

  const askAllocationReversal = (allocation: Allocation, reason: string) =>
    action.run({
      identity: `POST /payments/${payment.id}/allocations/${allocation.id}/reverse ${payment.version} ${JSON.stringify(reason)}`,
      verb: 'request the reversal of',
      send: (key) => requestAllocationReversal(client, payment, allocation, reason, key),
      done: () => 'The reversal is asked for: the allocation waits for a Supervisor or an administrator to decide.',
    });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={payment.number}
        back={{ to: '/payments', label: 'Payments' }}
        subtitle={
          <>
            Fiscal year {payment.fiscalYear} ·{' '}
            <Badge tone={paymentStatusLabels[payment.status].tone} icon={paymentStatusLabels[payment.status].icon}>
              {paymentStatusLabels[payment.status].label}
            </Badge>
          </>
        }
        actions={
          posted && (
            <>
              {canAllocate && holdsCash && (
                <Link className={buttons.primary} to={`/payments/${payment.id}/allocate`}>
                  Allocate to bills
                </Link>
              )}
              {canRefund && holdsCash && (
                <Link className={buttons.secondary} to={`/refunds/new?paymentId=${payment.id}`}>
                  Draft a refund
                </Link>
              )}
              {canAskReversal && !allocationWaits && asking === null && (
                <button type="button" className={buttons.danger} onClick={() => setAsking('payment')}>
                  Request reversal
                </button>
              )}
            </>
          )
        }
      />
      {payment.status !== 'Reversed' && <PrintLink to={`/payments/${payment.id}/print`}>Print the receipt</PrintLink>}
      {action.notices}
      {asking === 'payment' && (
        <ReasonForm
          label="Why should this payment be reversed? (a dishonoured cheque, for one)"
          confirmLabel="Ask for the reversal"
          busy={action.pending}
          onConfirm={askPaymentReversal}
          onCancel={() => setAsking(null)}
        />
      )}
      {payment.status === 'PendingReversal' && (
        <Banner tone="warning" title="Waiting for its reversal" live>
          This payment waits for its reversal: a Supervisor or an administrator other than the one who asked decides.
          Nothing can be allocated from it meanwhile.
        </Banner>
      )}
      {allocationWaits && (
        <Banner tone="warning" title="An allocation waits for its reversal" live>
          An allocation of this payment waits for its reversal; nothing else can be reversed until it is decided.
        </Banner>
      )}
      {payment.status === 'Reversed' && (
        <Banner tone="danger" title="Reversed">
          Reversed {formatInstant(payment.reversedAtUtc)}: {payment.reversalReason}
        </Banner>
      )}
      <div className={summary.split}>
        <Details
          items={[
            [
              'Payer',
              payer.data === undefined ? '…' : <Link to={`/payers/${payment.payerId}`}>{payer.data.name}</Link>,
            ],
            ['Payment date', formatDate(payment.paymentDate)],
            ['Method', methodLabels[payment.method]],
            ['Reference', payment.reference],
            ['Notes', payment.notes],
            ['Recorded', formatInstant(payment.createdAtUtc)],
          ]}
        />
        <div>
          <TotalsCard
            label="Figures of the payment"
            rows={[
              { label: 'Amount', value: payment.amount, strong: true },
              { label: 'Unapplied cash', value: payment.unappliedAmount, apart: true },
            ]}
          />
        </div>
      </div>

      <section className={forms.section} aria-labelledby="allocations-title">
        <h2 id="allocations-title">Allocations</h2>
        {allocations.length === 0 ? (
          <p className={table.empty}>Nothing of this payment is applied to a bill yet.</p>
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
                {allocations.map((allocation) => (
                  <tr key={allocation.id}>
                    <td>
                      <BillNumber id={allocation.billId} />
                    </td>
                    <td className={table.number}>{formatDecimal(allocation.amount)}</td>
                    <td>{formatInstant(allocation.allocatedAtUtc)}</td>
                    <td>
                      {allocation.allocatedByUserId === null
                        ? 'The system'
                        : allocation.allocatedByUserId === session.userId
                          ? 'You'
                          : allocation.allocatedByUserId.slice(0, 8)}
                    </td>
                    <td>
                      <Badge
                        tone={allocationStatusLabels[allocation.status].tone}
                        icon={allocationStatusLabels[allocation.status].icon}
                      >
                        {allocationStatusLabels[allocation.status].label}
                      </Badge>
                    </td>
                    <td>
                      {allocation.status === 'Reversed' ? (
                        allocation.reversalReason
                      ) : posted && canAskReversal && allocation.status === 'Active' && !allocationWaits ? (
                        asking === allocation.id ? (
                          <ReasonForm
                            label="Why should this allocation be reversed?"
                            confirmLabel="Ask for the reversal"
                            busy={action.pending}
                            onConfirm={(reason) => askAllocationReversal(allocation, reason)}
                            onCancel={() => setAsking(null)}
                          />
                        ) : (
                          asking === null && (
                            <button type="button" className={buttons.link} onClick={() => setAsking(allocation.id)}>
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

      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="Payment" entityId={payment.id} />
      </section>
    </section>
  );
}
