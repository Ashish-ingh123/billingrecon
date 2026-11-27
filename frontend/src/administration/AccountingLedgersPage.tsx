import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { answer, ifMatch } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import {
  accountingLedgersKey,
  exportLedgerLabels,
  type AccountingLedgers,
  type ExportLedger,
} from '../reports/accountingExport';
import { AdministrationTabs } from './AdministrationTabs';

/** The twelve names as the form holds them: a name that is none as an empty one. */
type LedgerValues = Record<LedgerField, string>;
type LedgerField =
  | 'receivable'
  | 'unappliedCash'
  | 'creditAvailable'
  | 'revenue'
  | 'taxOutput'
  | 'adjustments'
  | 'openingBalances'
  | 'cash'
  | 'card'
  | 'upi'
  | 'bankTransfer'
  | 'cheque';

const fields = [
  'receivable',
  'unappliedCash',
  'creditAvailable',
  'revenue',
  'taxOutput',
  'adjustments',
  'openingBalances',
  'cash',
  'card',
  'upi',
  'bankTransfer',
  'cheque',
] as const satisfies readonly LedgerField[];

/** The ledger of the export each field names, for its label. */
const ledgerOf: Record<LedgerField, ExportLedger> = {
  receivable: 'Receivable',
  unappliedCash: 'UnappliedCash',
  creditAvailable: 'CreditAvailable',
  revenue: 'Revenue',
  taxOutput: 'TaxOutput',
  adjustments: 'Adjustments',
  openingBalances: 'OpeningBalances',
  cash: 'Cash',
  card: 'Card',
  upi: 'Upi',
  bankTransfer: 'BankTransfer',
  cheque: 'Cheque',
};

const groups: readonly { title: string; fields: readonly LedgerField[] }[] = [
  { title: 'Balances', fields: ['receivable', 'unappliedCash', 'creditAvailable', 'openingBalances'] },
  { title: 'Income and tax', fields: ['revenue', 'taxOutput', 'adjustments'] },
  { title: 'Money received, by how it came', fields: ['cash', 'card', 'upi', 'bankTransfer', 'cheque'] },
];

/** Longest name, as the API has it. */
const nameMaxLength = 100;

function valuesOf(ledgers: AccountingLedgers): LedgerValues {
  return Object.fromEntries(fields.map((field) => [field, ledgers[field] ?? ''])) as LedgerValues;
}

const nameRule = {
  validate: (value: string) =>
    (value.trim().length <= nameMaxLength && !/[\p{Cc}]/u.test(value)) ||
    `A name has at most ${String(nameMaxLength)} characters, on one line.`,
};

/**
 * The names of the accountant's ledgers (Prompt 11.4b, ADR 0062): the ledger in the accountant's books that each line
 * of the export goes to, twelve names the hospital gives from its own chart of accounts. Until a ledger a period uses
 * is named, the export of that period is refused.
 */
export function AccountingLedgersPage() {
  const client = useApiClient();
  const ledgers = useQuery({
    queryKey: accountingLedgersKey,
    queryFn: ({ signal }) => answer(client.GET('/api/v1/settings/accounting-ledgers', { signal })),
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="Who signs in to this hospital, what each may do, and how the hospital's documents are numbered."
      />
      <AdministrationTabs />
      {ledgers.data === undefined ? (
        <LoadState error={ledgers.error} what="the accountant's ledgers" />
      ) : (
        <AccountingLedgersForm initial={ledgers.data} />
      )}
    </section>
  );
}

function AccountingLedgersForm({ initial }: { initial: AccountingLedgers }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(false);
  const form = useForm<LedgerValues>({ defaultValues: valuesOf(initial), mode: 'onChange' });
  const outcome = useRecordForm<AccountingLedgers, LedgerValues>({
    form,
    fields,
    initial,
    compared: fields.map((field) => ({
      label: exportLedgerLabels[ledgerOf[field]],
      value: (ledgers: AccountingLedgers) => ledgers[field] ?? 'None',
    })),
    valuesOf,
    reload: () => answer(client.GET('/api/v1/settings/accounting-ledgers')),
  });
  const base = outcome.base ?? initial;
  const {
    register,
    formState: { errors },
  } = form;

  const saving = useMutation({
    mutationFn: (values: LedgerValues) =>
      answer(
        client.PUT('/api/v1/settings/accounting-ledgers', {
          body: Object.fromEntries(
            fields.map((field) => [field, values[field].trim() === '' ? null : values[field].trim()]),
          ),
          headers: ifMatch(base.version),
        }),
      ),
    onMutate: () => {
      setSaved(false);
      outcome.clear();
    },
    onSuccess: (next) => {
      queryClient.setQueryData(accountingLedgersKey, next);
      outcome.rebase(next);
      form.reset(valuesOf(next));
      setSaved(true);
    },
    onError: outcome.failed,
  });

  return (
    <form
      className={forms.form}
      noValidate
      aria-label="The accountant's ledgers"
      onSubmit={(event) => void form.handleSubmit((values) => saving.mutate(values))(event)}
    >
      <p className={forms.note}>
        Each line of the <Link to="/reports/accounting-export">accountant's export</Link> goes to one of these ledgers.
        Give each the exact name of a ledger that already exists in the accountant's books: the package refuses a name
        it does not know. Two may share a name, for example card and UPI into one bank account. A ledger left empty is
        not named, and no export of a period that uses it is made.
      </p>
      {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={[...fields]} />}
      {outcome.conflict !== null && (
        <ConflictNotice
          changes={outcome.conflict.changes}
          onLoadLatest={outcome.loadLatest}
          onKeepMine={outcome.keepMine}
        />
      )}
      {saved && (
        <Banner tone="success" title="Saved" live>
          The next export names these ledgers.
        </Banner>
      )}
      {groups.map((group) => (
        <section key={group.title} className={forms.section} aria-labelledby={`ledgers-${group.fields[0] ?? ''}`}>
          <h2 id={`ledgers-${group.fields[0] ?? ''}`}>{group.title}</h2>
          <div className={forms.grid}>
            {group.fields.map((field) => (
              <TextField
                key={field}
                id={`ledger-${field}`}
                label={exportLedgerLabels[ledgerOf[field]]}
                maxLength={nameMaxLength}
                autoComplete="off"
                error={errors[field]?.message}
                {...register(field, nameRule)}
              />
            ))}
          </div>
        </section>
      ))}
      <div className={forms.actions}>
        <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
          {saving.isPending ? 'Saving…' : 'Save the ledger names'}
        </button>
      </div>
    </form>
  );
}
