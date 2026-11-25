import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import forms from '../components/forms.module.css';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { fileLimits, importParties, tooLarge, type ChosenFile, type PartiesBody, type PartyImport } from './api';
import { FileField } from './FileField';
import { Findings } from './Findings';
import styles from './Imports.module.css';

const count = (value: number | string) => Number(value).toLocaleString('en-IN');

/**
 * Step one of bringing in a hospital's records (Prompt 10.3b, ADR 0053): its patients, each with the self-pay payer it
 * is, and its guarantors. The files are checked first, which writes nothing and names every row that cannot be brought
 * in; only files with no such row can be imported. A patient the hospital has already is matched, never merged.
 */
export function PartiesImportPage() {
  const client = useApiClient();
  const [patients, setPatients] = useState<ChosenFile | null>(null);
  const [guarantors, setGuarantors] = useState<ChosenFile | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const run = useMutation({ mutationFn: (body: PartiesBody) => importParties(client, body) });
  const result: PartyImport | undefined = run.data;

  const choose = (set: (file: ChosenFile | null) => void) => (file: ChosenFile | null) => {
    set(file);
    setRefusal(null);
    run.reset();
  };

  const send = (dryRun: boolean) => {
    if (patients === null) {
      return;
    }

    const body: PartiesBody = { patientsCsv: patients.text, guarantorsCsv: guarantors?.text ?? null, dryRun };
    const large = tooLarge(body, [
      [patients, fileLimits.patients],
      [guarantors, fileLimits.guarantors],
    ]);
    setRefusal(large);
    if (large === null) {
      run.mutate(body);
    }
  };

  const checkedClean = result !== undefined && result.dryRun && result.rejected.length === 0;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Import patients and guarantors"
        back={{ to: '/administration/import', label: 'Import' }}
        subtitle="Step 1 of 2. Bring in the people first: the opening balances name them by MRN and by guarantor reference."
      />
      <AdministrationTabs />

      <form
        className={forms.form}
        onSubmit={(event) => {
          event.preventDefault();
          send(true);
        }}
      >
        <div className={forms.grid}>
          <FileField
            label="Patients (patients.csv)"
            hint="Columns mrn and full_name; optionally date_of_birth, sex, phone, email, the address, city, state, pin_code."
            required
            onChange={choose(setPatients)}
          />
          <FileField
            label="Guarantors (guarantors.csv)"
            hint="Optional. Columns guarantor_ref and name; optionally phone and email."
            onChange={choose(setGuarantors)}
          />
        </div>
        {refusal !== null && (
          <Banner tone="danger" title="Too large to send" live>
            {refusal}
          </Banner>
        )}
        {run.error !== null && <ProblemAlert problem={problemOf(run.error)} />}
        <div className={forms.actions}>
          <button type="submit" className={buttons.secondary} disabled={patients === null || run.isPending}>
            {run.isPending && run.variables.dryRun ? 'Checking…' : 'Check the files'}
          </button>
          <button
            type="button"
            className={buttons.primary}
            disabled={!checkedClean || run.isPending}
            aria-describedby="import-note"
            onClick={() => send(false)}
          >
            {run.isPending && !run.variables.dryRun ? 'Importing…' : 'Import'}
          </button>
        </div>
        <p id="import-note" className={forms.note}>
          Import is offered once the files were checked and no row was rejected. The files stay in this page only.
        </p>
      </form>

      <div className={styles.result} aria-live="polite">
        {result !== undefined && (
          <>
            {result.written && (
              <Banner tone="success" title="Imported" live>
                The patients, their payers and the guarantors are in. The same files can be sent again: what is here is
                matched and left as it is.
              </Banner>
            )}
            {checkedClean && (
              <Banner tone="success" title="The files can be imported" live>
                Nothing was written yet. Choose Import to bring them in.
              </Banner>
            )}
            {!result.dryRun && !result.written && (
              <Banner tone="danger" title="Nothing was imported" live>
                A row was rejected, so no row was written.
              </Banner>
            )}
            <dl className={styles.figures}>
              <div className={styles.tile}>
                <dt>{result.written ? 'Patients registered' : 'New patients'}</dt>
                <dd>{count(result.patientsCreated)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Patients already here</dt>
                <dd>{count(result.patientsMatched)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>{result.written ? 'Guarantors added' : 'New guarantors'}</dt>
                <dd>{count(result.guarantorsCreated)}</dd>
              </div>
              <div className={styles.tile}>
                <dt>Guarantors already here</dt>
                <dd>{count(result.guarantorsMatched)}</dd>
              </div>
            </dl>
            {result.rejected.length > 0 && (
              <Findings findings={result.rejected} fileName="patients-import-findings.csv" />
            )}
          </>
        )}
      </div>
    </section>
  );
}
