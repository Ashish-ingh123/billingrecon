import { Link } from 'react-router';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { Money } from '../components/Money';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatInstant } from '../format';
import { batchStatusLabels, batchStatusTones, useOpeningBatches } from './api';
import styles from './Imports.module.css';

/**
 * Bringing in a hospital's records, for its administrator (Prompt 10.3b, ADR 0053): the two steps in their order, and
 * the opening batches that were submitted, with where each stands.
 */
export function ImportIndexPage() {
  const batches = useOpeningBatches(true);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="Who signs in to this hospital, what each may do, and how the hospital's documents are numbered."
      />
      <AdministrationTabs />

      <ol className={styles.steps}>
        <li className={styles.step}>
          <span className={styles.stepNumber}>Step 1</span>
          <h2>Patients and guarantors</h2>
          <p>
            Bring in the people of the old system first. A patient already here is matched by MRN and left as it is.
          </p>
          <p>
            <Link className={buttons.secondary} to="/administration/import/patients">
              Import patients and guarantors
            </Link>
          </p>
        </li>
        <li className={styles.step}>
          <span className={styles.stepNumber}>Step 2</span>
          <h2>Opening balances</h2>
          <p>
            Then what they owed and held on the day before the cut-over, checked against the old system&apos;s trial
            balance. A second person approves the batch before anything is posted.
          </p>
          <p>
            <Link className={buttons.secondary} to="/administration/import/opening-balances">
              Import opening balances
            </Link>
          </p>
        </li>
      </ol>

      <h2 id="batches-title">Opening batches</h2>
      {batches.data === undefined ? (
        <LoadState error={batches.error} what="the opening batches" />
      ) : batches.data.items.length === 0 ? (
        <EmptyState icon="building">No opening batch was submitted yet.</EmptyState>
      ) : (
        <div className={table.wrap}>
          <table className={table.table} aria-labelledby="batches-title">
            <thead>
              <tr>
                <th scope="col">Submitted</th>
                <th scope="col">Cut-over date</th>
                <th scope="col">State</th>
                <th scope="col" className={table.number}>
                  Owed
                </th>
                <th scope="col" className={table.number}>
                  Held
                </th>
              </tr>
            </thead>
            <tbody>
              {batches.data.items.map((batch) => (
                <tr key={batch.id}>
                  <td>
                    <Link to={`/administration/import/batches/${batch.id}`}>{formatInstant(batch.submittedAtUtc)}</Link>
                  </td>
                  <td>{formatDate(batch.cutOverDate)}</td>
                  <td>
                    <Badge tone={batchStatusTones[batch.status]}>{batchStatusLabels[batch.status]}</Badge>
                  </td>
                  <td className={table.number}>
                    <Money value={batch.receivableTotal} />
                  </td>
                  <td className={table.number}>
                    <Money value={batch.advanceTotal} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
