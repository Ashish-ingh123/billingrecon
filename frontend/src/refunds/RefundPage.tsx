import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
import { AuditTrail } from '../components/AuditTrail';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { ConfirmAction } from '../components/ConfirmAction';
import { Banner } from '../components/Banner';
import { Details } from '../components/Details';
import summary from '../components/Summary.module.css';
import { TotalsCard } from '../components/TotalsCard';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ReasonForm } from '../components/ReasonForm';
import { useDocumentAction } from '../components/useDocumentAction';
import { formatDate, formatInstant } from '../format';
import { methodLabels } from '../payments/api';
import { usePayer } from '../payers/api';
import {
  comparedRefundFields,
  getRefund,
  refundKeys,
  refundsChanged,
  refundStatusLabels,
  submitRefund,
  useRefund,
  voidRefund,
  type Refund,
} from './api';

/** What a refund without a number is called, by its state. */
const unnumbered: Record<Refund['status'], string> = {
  Draft: 'Draft refund',
  PendingApproval: 'Refund waiting for approval',
  Voided: 'Voided draft refund',
  Posted: 'Refund',
};

/** One refund (US-34): its source, amount and payout, its state, and what its maker can still do with a draft. */
export function RefundPage() {
  const { id = '' } = useParams();
  const refund = useRefund(id);

  return refund.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Refund" back={{ to: '/refunds', label: 'Refunds' }} />
      <LoadState error={refund.error} what="the refund" />
    </section>
  ) : (
    <RefundView refund={refund.data} />
  );
}

function RefundView({ refund }: { refund: Refund }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const canDraft = useCan('DraftRefunds');
  const payer = usePayer(refund.payerId);
  const [voiding, setVoiding] = useState(false);

  const show = (changed: Refund) => {
    queryClient.setQueryData(refundKeys.one(changed.id), changed);
    refundsChanged(queryClient);
  };

  const action = useDocumentAction<Refund, Refund>({
    current: refund,
    compared: comparedRefundFields,
    reload: () => getRefund(client, refund.id),
    onDone: (changed) => {
      setVoiding(false);
      show(changed);
    },
    onReloaded: show,
  });

  const submit = () =>
    action.run({
      identity: `POST /refunds/${refund.id}/post ${refund.version}`,
      verb: 'submit',
      send: (key) => submitRefund(client, refund, key),
      done: () => 'Submitted: the refund waits for a Supervisor or an administrator, who posts it.',
    });

  const voidDraft = (reason: string) =>
    action.run({
      identity: `POST /refunds/${refund.id}/void ${refund.version} ${JSON.stringify(reason)}`,
      verb: 'void',
      send: (key) => voidRefund(client, refund, reason, key),
      done: () => 'The draft refund is voided.',
    });

  const source =
    refund.sourceKind === 'Payment' && refund.paymentId !== null ? (
      <Link to={`/payments/${refund.paymentId}`}>The payment</Link>
    ) : refund.creditNoteId !== null ? (
      <Link to={`/credit-notes/${refund.creditNoteId}`}>The credit note</Link>
    ) : (
      'A credit note'
    );

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={refund.number ?? unnumbered[refund.status]}
        back={{ to: '/refunds', label: 'Refunds' }}
        subtitle={
          <>
            {refund.fiscalYear !== null && <>Fiscal year {refund.fiscalYear} · </>}
            <Badge tone={refundStatusLabels[refund.status].tone} icon={refundStatusLabels[refund.status].icon}>
              {refundStatusLabels[refund.status].label}
            </Badge>
          </>
        }
        actions={
          canDraft &&
          refund.status === 'Draft' &&
          !voiding && (
            <>
              <Link className={buttons.secondary} to={`/refunds/${refund.id}/edit`}>
                Edit
              </Link>
              <ConfirmAction
                label="Submit for approval"
                confirmLabel="Confirm: submit the refund"
                busy={action.pending}
                onConfirm={submit}
              />
              <button type="button" className={buttons.danger} onClick={() => setVoiding(true)}>
                Void draft
              </button>
            </>
          )
        }
      />
      {action.notices}
      {voiding && (
        <ReasonForm
          label="Why is the draft refund voided?"
          confirmLabel="Confirm: void the draft"
          busy={action.pending}
          onConfirm={voidDraft}
          onCancel={() => setVoiding(false)}
        />
      )}
      {refund.status === 'PendingApproval' && (
        <Banner tone="warning" title="Waiting for approval" live>
          This refund waits for approval: a Supervisor or an administrator other than the one who submitted it posts it
          or returns it to draft.
        </Banner>
      )}
      {refund.status === 'Voided' && (
        <Banner tone="danger" title="Voided">
          Voided {formatInstant(refund.voidedAtUtc)}: {refund.voidReason}
        </Banner>
      )}
      <div className={summary.split}>
        <Details
          items={[
            ['Payer', payer.data === undefined ? '…' : <Link to={`/payers/${refund.payerId}`}>{payer.data.name}</Link>],
            ['From', source],
            ['Refund date', formatDate(refund.refundDate)],
            ['Method', methodLabels[refund.method]],
            ['Reference', refund.reference],
            ['Reason', refund.reason],
            ['Posted', formatInstant(refund.postedAtUtc)],
          ]}
        />
        <div>
          <TotalsCard label="Figures of the refund" rows={[{ label: 'Amount', value: refund.amount, strong: true }]} />
        </div>
      </div>
      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="Refund" entityId={refund.id} />
      </section>
    </section>
  );
}
