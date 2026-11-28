import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { Link } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useIdempotentRequest } from '../api/useIdempotentRequest';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { SelectField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatDecimal, formatMoney } from '../format';
import type { ChosenFile } from '../imports/api';
import { FileField } from '../imports/FileField';
import {
  directionLabels,
  dryRunStatement,
  importStatement,
  maxStatementLength,
  statementsChanged,
  useChannels,
  type StatementBody,
  type StatementDryRun,
  type StatementImport,
} from './api';
import styles from './Reconciliation.module.css';

const count = (value: number | string) => Number(value).toLocaleString('en-IN');

/** How many of the lines read the page shows; the counts and the money cover them all. */
const previewRows = 50;

/**
 * Imports a bank's statement into its channel (Prompt 13.1c, US-61; BR-1003, BR-1004; ADR 0065). The file is checked
 * first, a dry run that stores nothing and names every row it refuses; the import then stores exactly the file the
 * check read, each line once. A line the channel already holds from an earlier file is not stored again.
 */
export function StatementImportPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const sendOnce = useIdempotentRequest();
  const channels = useChannels();
  const channelId = useId();
  const [channel, setChannel] = useState('');
  const [file, setFile] = useState<ChosenFile | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const banks = (channels.data ?? []).filter((candidate) => candidate.kind === 'Bank' && candidate.status === 'Active');

  const check = useMutation({ mutationFn: (body: StatementBody) => dryRunStatement(client, body) });
  const run = useMutation({
    mutationFn: (body: StatementBody) => sendOnce(JSON.stringify(body), (key) => importStatement(client, body, key)),
    onSuccess: () => statementsChanged(queryClient),
  });
  const checked: StatementDryRun | undefined = check.data;
  const imported: StatementImport | undefined = run.data;

  const reset = () => {
    setRefusal(null);
    check.reset();
    run.reset();
  };

  const body = (fileHash: string | null): StatementBody | null =>
    file === null || channel === '' ? null : { channelId: channel, fileName: file.name, text: file.text, fileHash };

  const sendCheck = () => {
    const request = body(null);
    if (request === null) {
      return;
    }

    if (request.text.length > maxStatementLength) {
      setRefusal(
        `${request.fileName} is larger than 1 MB (${count(request.text.length)} characters; at most ${count(maxStatementLength)}). Download the statement in shorter periods and import them one after another.`,
      );
      return;
    }

    setRefusal(null);
    run.reset();
    check.mutate(request);
  };

  const canImport =
    checked !== undefined && !checked.alreadyImported && Number(checked.linesToAdd) > 0 && imported === undefined;

  if (channels.data === undefined) {
    return <LoadState error={channels.error} what="settlement channels" />;
  }

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Import a bank statement"
        back={{ to: '/reconciliation', label: 'Bank reconciliation' }}
        subtitle="Download the statement from the bank as CSV, check it, then import it. Each line is stored once."
      />

      {banks.length === 0 ? (
        <Banner tone="warning" title="No bank account is set up">
          An administrator sets up the hospital&apos;s bank accounts and how their statements are read, under Users and
          settings, Channels.
        </Banner>
      ) : (
        <form
          className={forms.form}
          onSubmit={(event) => {
            event.preventDefault();
            sendCheck();
          }}
        >
          <div className={forms.grid}>
            <SelectField
              id={channelId}
              label="Bank account"
              requiredMark
              value={channel}
              onChange={(event) => {
                setChannel(event.target.value);
                reset();
              }}
            >
              <option value="">Choose the account</option>
              {banks.map((bank) => (
                <option key={bank.id} value={bank.id}>
                  {bank.name}
                  {bank.accountLabel === null || bank.accountLabel === undefined ? '' : ` (…${bank.accountLabel})`}
                </option>
              ))}
            </SelectField>
            <FileField
              label="Statement (CSV)"
              hint="Saved as UTF-8, at most 1 MB. The file stays in this page until you import it."
              required
              onChange={(chosen) => {
                setFile(chosen);
                reset();
              }}
            />
          </div>
          {refusal !== null && (
            <Banner tone="danger" title="Too large to send" live>
              {refusal}
            </Banner>
          )}
          {check.error !== null && <ProblemAlert problem={problemOf(check.error)} />}
          {run.error !== null && <ProblemAlert problem={problemOf(run.error)} />}
          <div className={forms.actions}>
            <button
              type="submit"
              className={buttons.secondary}
              disabled={file === null || channel === '' || check.isPending}
            >
              {check.isPending ? 'Checking…' : 'Check the file'}
            </button>
            <button
              type="button"
              className={buttons.primary}
              disabled={!canImport || run.isPending}
              aria-describedby="import-note"
              onClick={() => {
                const request = checked === undefined ? null : body(checked.fileHash);
                if (request !== null) {
                  run.mutate(request);
                }
              }}
            >
              {run.isPending ? 'Importing…' : 'Import'}
            </button>
          </div>
          <p id="import-note" className={forms.note}>
            Import is offered once the file was checked and has lines the account does not hold yet. Rows that cannot be
            read are left out and listed; correct them in the file and import it again later.
          </p>
        </form>
      )}

      <div className={styles.result} aria-live="polite">
        {imported !== undefined && (
          <Banner tone="success" title="Imported" live>
            {count(imported.linesAdded)} lines are in.{' '}
            <Link to={`/reconciliation?import=${imported.id}`}>See the lines of this import</Link>
          </Banner>
        )}
        {checked !== undefined && imported === undefined && (
          <>
            {checked.alreadyImported ? (
              <Banner tone="warning" title="This file was imported already" live>
                Nothing would be added. Choose another file.
              </Banner>
            ) : Number(checked.linesToAdd) === 0 ? (
              <Banner tone="warning" title="Nothing new in this file" live>
                The account holds every line of it from an earlier statement.
              </Banner>
            ) : (
              <Banner tone="success" title="The file can be imported" live>
                Nothing was stored yet. Choose Import to bring in {count(checked.linesToAdd)} lines.
              </Banner>
            )}
            <dl className={styles.figures}>
              <div className={styles.tile}>
                <dt>Rows read</dt>
                <dd>{count(checked.rowsRead)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>New lines</dt>
                <dd>{count(checked.linesToAdd)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Held already</dt>
                <dd>{count(checked.linesAlreadyHeld)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Rows refused</dt>
                <dd>{count(checked.rowsRefused)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Money in</dt>
                <dd>{formatMoney(checked.moneyIn)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Money out</dt>
                <dd>{formatMoney(checked.moneyOut)}</dd>
              </div>
            </dl>
          </>
        )}
        {checked !== undefined && checked.refusals.length > 0 && (
          <section className={styles.section} aria-labelledby="refused-title">
            <h2 id="refused-title">
              {checked.refusals.length === 1
                ? '1 row cannot be read'
                : `${count(checked.refusals.length)} rows cannot be read`}
            </h2>
            <div className={table.wrap}>
              <table className={table.table}>
                <thead>
                  <tr>
                    <th scope="col">Line</th>
                    <th scope="col">Why</th>
                  </tr>
                </thead>
                <tbody>
                  {checked.refusals.map((row) => (
                    <tr key={`${String(row.rowNumber)}:${row.reason}`}>
                      <td>{row.rowNumber}</td>
                      <td>{row.reason}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}
        {checked !== undefined && checked.lines.length > 0 && imported === undefined && (
          <section className={styles.section} aria-labelledby="preview-title">
            <h2 id="preview-title">
              The first {count(Math.min(previewRows, checked.lines.length))} lines read
              {checked.firstDate !== null && checked.firstDate !== undefined
                ? `, ${formatDate(checked.firstDate)} to ${formatDate(checked.lastDate)}`
                : ''}
            </h2>
            <div className={table.wrap}>
              <table className={table.table}>
                <thead>
                  <tr>
                    <th scope="col">Line</th>
                    <th scope="col">Date</th>
                    <th scope="col">Description</th>
                    <th scope="col">Reference</th>
                    <th scope="col">Direction</th>
                    <th scope="col" className={table.number}>
                      Amount (₹)
                    </th>
                    <th scope="col">Here already</th>
                  </tr>
                </thead>
                <tbody>
                  {checked.lines.slice(0, previewRows).map((line) => (
                    <tr key={line.rowNumber}>
                      <td>{line.rowNumber}</td>
                      <td>{formatDate(line.transactionDate)}</td>
                      <td className={styles.wrapCell}>{line.description}</td>
                      <td>{line.reference ?? '—'}</td>
                      <td>{directionLabels[line.direction]}</td>
                      <td className={table.number}>{formatDecimal(line.amount)}</td>
                      <td>{line.alreadyHeld ? 'Yes' : 'No'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}
      </div>
    </section>
  );
}
