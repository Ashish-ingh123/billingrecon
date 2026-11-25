import { useParams } from 'react-router';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { ReviewLink } from '../approvals/ReviewLink';
import { AuditTrail } from '../components/AuditTrail';
import { Banner } from '../components/Banner';
import { Details } from '../components/Details';
import { LoadState } from '../components/Loading';
import { Money } from '../components/Money';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatInstant } from '../format';
import { batchStatusLabels, useOpeningBatch, type OpeningBatch } from './api';
import styles from './Imports.module.css';

const count = (value: number | string) => Number(value).toLocaleString('en-IN');

function StateBanner({ batch }: { batch: OpeningBatch }) {
  switch (batch.status) {
    case 'PendingApproval':
      return (
        <Banner tone="warning" title="Waiting for approval">
          Nothing is posted yet. A second person, a supervisor or another administrator, approves or rejects the batch.{' '}
          <ReviewLink documentType="OpeningBatch" />
        </Banner>
      );
    case 'Approved':
      return (
        <Banner tone="success" title="Approved and posted">
          Every opening receivable and advance of the batch is posted, under numbers that begin with OBR and OBA.
        </Banner>
      );
    case 'Rejected':
      return (
        <Banner tone="danger" title="Rejected">
          Nothing of the batch was posted. The files can be put right and submitted again.
        </Banner>
      );
  }
}

/**
 * One opening batch (Prompt 10.3b, ADR 0053): where it stands, what it brings in, and its history. Its files are never
 * shown: the API does not answer them back.
 */
export function OpeningBatchPage() {
  const { id = '' } = useParams();
  const batch = useOpeningBatch(id);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Opening batch"
        back={{ to: '/administration/import', label: 'Import' }}
        subtitle={batch.data === undefined ? undefined : `Cut-over ${formatDate(batch.data.cutOverDate)}`}
      />
      <AdministrationTabs />
      {batch.data === undefined ? (
        <LoadState error={batch.error} what="the batch" />
      ) : (
        <>
          <StateBanner batch={batch.data} />
          <Details
            items={[
              ['State', batchStatusLabels[batch.data.status]],
              ['Cut-over date', formatDate(batch.data.cutOverDate)],
              ['Opening receivables', count(batch.data.receivables)],
              ['Owed in all', <Money key="owed" value={batch.data.receivableTotal} />],
              ['Advances and credit balances', count(batch.data.advances)],
              ['Held in all', <Money key="held" value={batch.data.advanceTotal} />],
              ['Submitted', formatInstant(batch.data.submittedAtUtc)],
              ['Decided', batch.data.decidedAtUtc === null ? 'Not yet' : formatInstant(batch.data.decidedAtUtc)],
            ]}
          />
          <div className={styles.section}>
            <AuditTrail entityType="OpeningBatch" entityId={batch.data.id} />
          </div>
        </>
      )}
    </section>
  );
}
