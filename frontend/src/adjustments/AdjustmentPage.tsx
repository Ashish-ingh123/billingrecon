import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { ReviewLink } from '../approvals/ReviewLink';
import { useCan } from '../auth/permissions';
import { useBill } from '../bills/api';
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
import { usePayer } from '../payers/api';
import {
  adjustmentKeys,
  adjustmentsChanged,
  adjustmentStatusLabels,
  comparedAdjustmentFields,
  getAdjustment,
  kindLabels,
  submitAdjustment,
  useAdjustment,
  voidAdjustment,
  type Adjustment,
} from './api';

/** What an adjustment without a number is called, by its state. */
const unnumbered: Record<Adjustment['status'], string> = {
  Draft: 'Draft adjustment',
  PendingApproval: 'Adjustment waiting for approval',
  Voided: 'Voided draft adjustment',
  Posted: 'Adjustment',
};

/** One adjustment (BR-501 to BR-503): the bill it lowers, by how much and why, and what its maker can do with a draft. */
export function AdjustmentPage() {
  const { id = '' } = useParams();
  const adjustment = useAdjustment(id);

  return adjustment.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Adjustment" back={{ to: '/adjustments', label: 'Adjustments' }} />
      <LoadState error={adjustment.error} what="the adjustment" />
    </section>
  ) : (
    <AdjustmentView adjustment={adjustment.data} />
  );
}

function AdjustmentView({ adjustment }: { adjustment: Adjustment }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const canDraft = useCan('DraftCreditNotesAndAdjustments');
  const bill = useBill(adjustment.billId);
  const payer = usePayer(adjustment.payerId);
  const [voiding, setVoiding] = useState(false);

  const show = (changed: Adjustment) => {
    queryClient.setQueryData(adjustmentKeys.one(changed.id), changed);
    adjustmentsChanged(queryClient);
  };

  const action = useDocumentAction<Adjustment, Adjustment>({
    current: adjustment,
    compared: comparedAdjustmentFields,
    reload: () => getAdjustment(client, adjustment.id),
    onDone: (changed) => {
      setVoiding(false);
      show(changed);
    },
    onReloaded: show,
  });

  const submit = () =>
    action.run({
      identity: `POST /adjustments/${adjustment.id}/post ${adjustment.version}`,
      verb: 'submit',
      send: (key) => submitAdjustment(client, adjustment, key),
      done: () => 'Submitted: the adjustment waits for a Supervisor or an administrator, who posts it.',
    });

  const voidDraft = (reason: string) =>
    action.run({
      identity: `POST /adjustments/${adjustment.id}/void ${adjustment.version} ${JSON.stringify(reason)}`,
      verb: 'void',
      send: (key) => voidAdjustment(client, adjustment, reason, key),
      done: () => 'The draft adjustment is voided.',
    });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={adjustment.number ?? unnumbered[adjustment.status]}
        back={{ to: '/adjustments', label: 'Adjustments' }}
        subtitle={
          <>
            {adjustment.fiscalYear !== null && <>Fiscal year {adjustment.fiscalYear} · </>}
            <Badge
              tone={adjustmentStatusLabels[adjustment.status].tone}
              icon={adjustmentStatusLabels[adjustment.status].icon}
            >
              {adjustmentStatusLabels[adjustment.status].label}
            </Badge>
          </>
        }
        actions={
          canDraft &&
          adjustment.status === 'Draft' &&
          !voiding && (
            <>
              <Link className={buttons.secondary} to={`/adjustments/${adjustment.id}/edit`}>
                Edit
              </Link>
              <ConfirmAction
                label="Submit for approval"
                confirmLabel="Confirm: submit the adjustment"
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
          label="Why is the draft adjustment voided?"
          confirmLabel="Confirm: void the draft"
          busy={action.pending}
          onConfirm={voidDraft}
          onCancel={() => setVoiding(false)}
        />
      )}
      {adjustment.status === 'PendingApproval' && (
        <p className={forms.note} role="status">
          This adjustment waits for approval: a Supervisor or an administrator other than the one who submitted it posts
          it or returns it to draft. <ReviewLink documentType="Adjustment" />
        </p>
      )}
      {adjustment.status === 'Voided' && (
        <Banner tone="danger" title="Voided">
          Voided {formatInstant(adjustment.voidedAtUtc)}: {adjustment.voidReason}
        </Banner>
      )}
      <div className={summary.split}>
        <Details
          items={[
            [
              'Bill',
              <Link key="bill" to={`/bills/${adjustment.billId}`}>
                {bill.data?.number ?? 'the bill'}
              </Link>,
            ],
            [
              'Payer',
              payer.data === undefined ? '…' : <Link to={`/payers/${adjustment.payerId}`}>{payer.data.name}</Link>,
            ],
            ['Kind', kindLabels[adjustment.kind]],
            ['Adjustment date', formatDate(adjustment.adjustmentDate)],
            ['Reason', adjustment.reason],
            ['Posted', formatInstant(adjustment.postedAtUtc)],
          ]}
        />
        <div>
          <TotalsCard
            label="Figures of the adjustment"
            rows={[{ label: 'Amount', value: adjustment.amount, strong: true }]}
          />
        </div>
      </div>
      <section className={forms.section} aria-labelledby="trail-title">
        <h2 id="trail-title">Audit trail</h2>
        <AuditTrail entityType="Adjustment" entityId={adjustment.id} />
      </section>
    </section>
  );
}
