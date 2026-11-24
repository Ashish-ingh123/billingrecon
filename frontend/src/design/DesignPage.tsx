import { useState } from 'react';
import { Icon } from '../brand/Icon';
import { Badge } from '../components/Badge';
import { Banner } from '../components/Banner';
import { DocumentStatus } from '../components/DocumentStatus';
import { documentStatuses, type DocumentKind } from '../components/documentStatuses';
import { Money } from '../components/Money';
import { TotalsCard } from '../components/TotalsCard';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { Details } from '../components/Details';
import { EmptyState } from '../components/EmptyState';
import { SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { StatusBadge } from '../components/StatusBadge';
import table from '../components/DataTable.module.css';
import { toneIcons, type Tone } from '../components/tones';
import styles from './DesignPage.module.css';

const tones = Object.keys(toneIcons) as Tone[];

/**
 * The specimen of the shared components (ADR 0048), in Development builds only: every component in its states, in the
 * theme chosen. It is the page the screenshots of docs/ui/ are taken from, and where a change of a token is seen first.
 * It reads nothing and sends nothing.
 */
export function DesignPage() {
  const [pending, setPending] = useState(false);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Design specimen"
        back={{ to: '/', label: 'the app' }}
        subtitle="Every shared component in its states. Development builds only."
        actions={
          <button type="button" className={buttons.primary}>
            <Icon name="check-circle" />
            Primary action
          </button>
        }
      />

      <section className={styles.block} aria-labelledby="buttons-title">
        <h2 id="buttons-title">Buttons</h2>
        <div className={styles.row}>
          <button type="button" className={buttons.primary}>
            Primary
          </button>
          <button type="button" className={buttons.secondary}>
            Secondary
          </button>
          <button type="button" className={buttons.danger}>
            Danger
          </button>
          <button type="button" className={buttons.link}>
            Link
          </button>
          <button type="button" className={buttons.primary} disabled>
            Disabled
          </button>
          <button
            type="button"
            className={buttons.primary}
            disabled={pending}
            aria-busy={pending}
            onClick={() => setPending(true)}
          >
            {pending ? 'Saving…' : 'Press to pend'}
          </button>
        </div>
      </section>

      <section className={styles.block} aria-labelledby="fields-title">
        <h2 id="fields-title">Fields</h2>
        <div className={forms.grid}>
          <TextField id="design-plain" label="A field" placeholder="A placeholder" />
          <TextField id="design-hint" label="With a hint" hint="What the field is for." defaultValue="Typed" />
          <TextField id="design-error" label="Refused" error="The API's message for this field." defaultValue="Wrong" />
          <TextField id="design-read" label="Read only" readOnly defaultValue="Cannot change" />
          <TextField id="design-disabled" label="Disabled" disabled defaultValue="Not now" />
          <SelectField id="design-select" label="A choice" defaultValue="b">
            <option value="a">First</option>
            <option value="b">Second</option>
          </SelectField>
        </div>
      </section>

      <section className={styles.block} aria-labelledby="badges-title">
        <h2 id="badges-title">Statuses</h2>
        <div className={styles.row}>
          {tones.map((tone) => (
            <Badge key={tone} tone={tone}>
              {tone}
            </Badge>
          ))}
          <StatusBadge status="Active" />
          <StatusBadge status="Inactive" />
        </div>
      </section>

      <section className={styles.block} aria-labelledby="every-status-title">
        <h2 id="every-status-title">Every status of every document</h2>
        {(Object.keys(documentStatuses) as DocumentKind[]).map((kind) => (
          <p key={kind} className={styles.row}>
            <strong>{kind}</strong>
            {Object.keys(documentStatuses[kind]).map((status) => (
              <DocumentStatus
                key={status}
                kind={kind}
                status={status as keyof (typeof documentStatuses)[typeof kind]}
              />
            ))}
          </p>
        ))}
      </section>

      <section className={styles.block} aria-labelledby="money-title">
        <h2 id="money-title">Money</h2>
        <p className={styles.row}>
          <Money value={1234567} /> <Money value={49.98} /> <Money value={500} credit /> <Money value={-12.5} />{' '}
          <Money value={1049.48} strong />
        </p>
        <TotalsCard
          label="Specimen totals"
          rows={[
            { label: 'Subtotal', value: 1000 },
            { label: 'Discounts', value: 0.5 },
            { label: 'Tax', value: 49.98 },
            { label: 'Total', value: 1049.48, strong: true },
            { label: 'Balance due', value: 549.48, strong: true, apart: true },
          ]}
        />
        <TotalsCard
          label="Specimen estimate"
          rows={[{ label: 'Total', value: 731.12, strong: true }]}
          estimate="An estimate until the draft is saved; the bill then shows the server's figures."
        />
        <TextField id="specimen-amount" label="Amount (₹)" leading="₹" requiredMark inputMode="decimal" />
        <TextField id="specimen-rate" label="Tax rate (%)" trailing="%" inputMode="decimal" />
      </section>

      <section className={styles.block} aria-labelledby="banners-title">
        <h2 id="banners-title">Banners</h2>
        <Banner tone="warning" title="Waiting for approval">
          This bill waits for approval: a Supervisor or an administrator other than the one who sent it posts it.
        </Banner>
        <Banner tone="danger" title="Voided">
          Voided 20 Nov 2026, 10:30 am: entered twice.
        </Banner>
        <Banner tone="success" title="Posted">
          The bill is posted as BIL/26-27/000123.
        </Banner>
      </section>

      <section className={styles.block} aria-labelledby="table-title">
        <h2 id="table-title">A list</h2>
        <div className={table.wrap}>
          <table className={table.table}>
            <thead>
              <tr>
                <th scope="col">Number</th>
                <th scope="col">Payer</th>
                <th scope="col" className={table.number}>
                  Total (₹)
                </th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {[
                ['BIL/26-27/000123', 'Asha Verma', '12,34,567.00', 'success', 'Settled'],
                ['BIL/26-27/000124', 'Ravi Kumar', '2,511.07', 'accent', 'Posted'],
                ['BIL/26-27/000125', 'Meera Iyer', '689.00', 'warning', 'Waiting'],
              ].map(([number, payer, total, tone, status]) => (
                <tr key={number}>
                  <td>
                    <a href="#table-title">{number}</a>
                  </td>
                  <td>{payer}</td>
                  <td className={table.number}>{total}</td>
                  <td>
                    <Badge tone={tone as Tone}>{status}</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className={styles.block} aria-labelledby="record-title">
        <h2 id="record-title">A record</h2>
        <Details
          items={[
            ['Patient', 'Asha Verma (MRN 1001)'],
            ['Payer', 'Asha Verma'],
            ['Bill date', '24 Nov 2026'],
            ['Notes', null],
          ]}
        />
      </section>

      <section className={styles.block} aria-labelledby="states-title">
        <h2 id="states-title">Loading, empty, refused, changed</h2>
        <div className={styles.stack}>
          <LoadState error={null} what="the specimen" />
          <EmptyState icon="inbox">Nothing waits here.</EmptyState>
          <ProblemAlert
            problem={{
              status: 422,
              errorCode: 'bill_has_no_lines',
              title: 'A bill needs at least one line to be posted.',
              detail: null,
              fieldErrors: {},
              correlationId: '9f1c2b7e-0000-4000-8000-000000000000',
              retryAfterSeconds: null,
            }}
          />
          <ConflictNotice
            changes={[{ label: 'Phone', before: '98765 43210', now: '91234 56789' }]}
            onLoadLatest={() => undefined}
            onKeepMine={() => undefined}
          />
        </div>
      </section>
    </section>
  );
}
