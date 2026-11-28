import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { AdministrationTabs } from '../administration/AdministrationTabs';
import { Badge } from '../components/Badge';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import {
  channelKindLabels,
  createChannel,
  dateFormats,
  emptyMapping,
  headersOf,
  reconciliationKeys,
  setChannelActive,
  updateChannel,
  useChannel,
  useChannels,
  type ChannelBody,
  type ChannelKind,
  type ColumnMapping,
  type SettlementChannel,
} from './api';
import styles from './Reconciliation.module.css';

interface Values {
  kind: ChannelKind;
  name: string;
  accountLabel: string;
  dateWindowDays: string;
  bankChannelId: string;
  mapping: ColumnMapping;
}

const valuesOf = (channel: SettlementChannel): Values => ({
  kind: channel.kind,
  name: channel.name,
  accountLabel: channel.accountLabel ?? '',
  dateWindowDays: String(channel.dateWindowDays),
  bankChannelId: channel.bankChannelId ?? '',
  mapping: channel.mapping ?? emptyMapping,
});

const blank: Values = {
  kind: 'Bank',
  name: '',
  accountLabel: '',
  dateWindowDays: '3',
  bankChannelId: '',
  mapping: emptyMapping,
};

const orNull = (text: string | null | undefined) =>
  text === null || text === undefined || text.trim() === '' ? null : text.trim();

function bodyOf(values: Values): ChannelBody {
  const mapping = values.mapping;
  const oneColumn = mapping.amountLayout === 'OneColumn';
  return {
    name: values.name.trim(),
    accountLabel: orNull(values.accountLabel),
    dateWindowDays: Number(values.dateWindowDays),
    bankChannelId: values.kind === 'Gateway' ? orNull(values.bankChannelId) : null,
    mapping:
      values.kind === 'Bank'
        ? {
            ...mapping,
            headerRow: Number(mapping.headerRow),
            dateColumn: (mapping.dateColumn ?? '').trim(),
            descriptionColumn: (mapping.descriptionColumn ?? '').trim(),
            valueDateColumn: orNull(mapping.valueDateColumn),
            referenceColumn: orNull(mapping.referenceColumn),
            balanceColumn: orNull(mapping.balanceColumn),
            amountColumn: oneColumn ? orNull(mapping.amountColumn) : null,
            debitColumn: oneColumn ? null : orNull(mapping.debitColumn),
            creditColumn: oneColumn ? null : orNull(mapping.creditColumn),
          }
        : null,
  };
}

/** A column of the mapping, offered the header names of the sample file when one was chosen. */
function ColumnField({
  label,
  hint,
  value,
  required = false,
  listId,
  onChange,
}: {
  label: string;
  hint?: string;
  value: string | null | undefined;
  required?: boolean;
  listId: string;
  onChange: (value: string) => void;
}) {
  const id = useId();
  return (
    <TextField
      id={id}
      label={label}
      requiredMark={required}
      list={listId}
      autoComplete="off"
      value={value ?? ''}
      {...(hint === undefined ? {} : { hint })}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

/**
 * Sets up or changes a settlement channel (Prompt 13.1c, BR-1001, BR-1002; ADR 0065). A bank is read with its column
 * mapping: choose a sample of its statement and the page offers the names of its header for each column, read in the
 * browser and sent nowhere. A gateway names the bank it settles into. The kind cannot change once the channel exists.
 */
function ChannelForm({ channel }: { channel: SettlementChannel | null }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const channels = useChannels();
  const [values, setValues] = useState<Values>(channel === null ? blank : valuesOf(channel));
  const [sample, setSample] = useState<string | null>(null);
  const listId = useId();
  const ids = {
    kind: useId(),
    name: useId(),
    label: useId(),
    window: useId(),
    bank: useId(),
    header: useId(),
    format: useId(),
    layout: useId(),
    style: useId(),
    sample: useId(),
  };
  const mapping = values.mapping;
  const headers = sample === null ? [] : headersOf(sample, Number(mapping.headerRow) || 1);
  const banks = (channels.data ?? []).filter(
    (candidate) => candidate.kind === 'Bank' && candidate.status === 'Active' && candidate.id !== channel?.id,
  );

  const set = (change: Partial<Values>) => setValues((current) => ({ ...current, ...change }));
  const setMapping = (change: Partial<ColumnMapping>) =>
    setValues((current) => ({ ...current, mapping: { ...current.mapping, ...change } }));

  const save = useMutation({
    mutationFn: () =>
      channel === null
        ? createChannel(client, values.kind, bodyOf(values))
        : updateChannel(client, channel.id, channel.version, bodyOf(values)),
    onSuccess: async (saved) => {
      queryClient.setQueryData(reconciliationKeys.channel(saved.id), saved);
      await queryClient.invalidateQueries({ queryKey: reconciliationKeys.channels });
      void navigate('/administration/channels');
    },
  });

  const toggle = useMutation({
    mutationFn: (active: boolean) => setChannelActive(client, channel!, active),
    onSuccess: async (saved) => {
      queryClient.setQueryData(reconciliationKeys.channel(saved.id), saved);
      await queryClient.invalidateQueries({ queryKey: reconciliationKeys.channels });
    },
  });

  return (
    <form
      className={forms.form}
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate();
      }}
    >
      <div className={forms.grid}>
        {channel === null ? (
          <SelectField
            id={ids.kind}
            label="Kind"
            value={values.kind}
            onChange={(event) => set({ kind: event.target.value as ChannelKind })}
          >
            <option value="Bank">{channelKindLabels.Bank}</option>
            <option value="Gateway">{channelKindLabels.Gateway} (Razorpay)</option>
          </SelectField>
        ) : (
          <TextField
            id={ids.kind}
            label="Kind"
            value={channelKindLabels[channel.kind]}
            readOnly
            hint="The kind of a channel never changes."
          />
        )}
        <TextField
          id={ids.name}
          label="Name"
          requiredMark
          value={values.name}
          maxLength={100}
          hint="How people name the account, such as HDFC current."
          onChange={(event) => set({ name: event.target.value })}
        />
        <TextField
          id={ids.label}
          label="Last digits of the account"
          value={values.accountLabel}
          inputMode="numeric"
          maxLength={4}
          hint="At most the last four digits. Never the full account number."
          onChange={(event) => set({ accountLabel: event.target.value })}
        />
        <TextField
          id={ids.window}
          label="Date window (days)"
          type="number"
          min={0}
          max={10}
          value={values.dateWindowDays}
          hint="How many days after a payment its money may reach this account: 3 for most banks; more for cheques."
          onChange={(event) => set({ dateWindowDays: event.target.value })}
        />
        {values.kind === 'Gateway' && (
          <SelectField
            id={ids.bank}
            label="Settles into"
            requiredMark
            value={values.bankChannelId}
            hint="The bank account the gateway pays the day's money into."
            onChange={(event) => set({ bankChannelId: event.target.value })}
          >
            <option value="">Choose a bank account</option>
            {banks.map((bank) => (
              <option key={bank.id} value={bank.id}>
                {bank.name}
              </option>
            ))}
          </SelectField>
        )}
      </div>

      {values.kind === 'Bank' && (
        <fieldset className={styles.mapping}>
          <legend>How the bank&apos;s statement is read</legend>
          <p className={forms.note}>
            Download a statement from the bank as CSV and choose it here: the page offers its column names below. The
            file stays in this page; nothing is imported from here.
          </p>
          <div className={forms.grid}>
            <TextField
              id={ids.sample}
              type="file"
              accept=".csv,text/csv"
              label="A sample of the statement (optional)"
              hint={
                headers.length > 0
                  ? `Found ${String(headers.length)} columns on line ${String(mapping.headerRow)}.`
                  : 'A CSV saved as UTF-8.'
              }
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file === undefined) {
                  setSample(null);
                  return;
                }

                void file.text().then(setSample, () => setSample(null));
              }}
            />
            <TextField
              id={ids.header}
              label="Line of the header"
              type="number"
              min={1}
              max={50}
              requiredMark
              value={String(mapping.headerRow)}
              hint="Banks write a few lines about the account above the header."
              onChange={(event) => setMapping({ headerRow: Number(event.target.value) })}
            />
            <ColumnField
              label="Date column"
              required
              listId={listId}
              value={mapping.dateColumn}
              onChange={(dateColumn) => setMapping({ dateColumn })}
            />
            <SelectField
              id={ids.format}
              label="Date format"
              requiredMark
              value={mapping.dateFormat ?? dateFormats[0]}
              onChange={(event) => setMapping({ dateFormat: event.target.value })}
            >
              {dateFormats.map((format) => (
                <option key={format} value={format}>
                  {format}
                </option>
              ))}
            </SelectField>
            <ColumnField
              label="Value date column"
              hint="Optional."
              listId={listId}
              value={mapping.valueDateColumn}
              onChange={(valueDateColumn) => setMapping({ valueDateColumn })}
            />
            <ColumnField
              label="Description column"
              required
              listId={listId}
              value={mapping.descriptionColumn}
              onChange={(descriptionColumn) => setMapping({ descriptionColumn })}
            />
            <ColumnField
              label="Reference column"
              hint="Optional: the UTR or cheque number."
              listId={listId}
              value={mapping.referenceColumn}
              onChange={(referenceColumn) => setMapping({ referenceColumn })}
            />
            <ColumnField
              label="Balance column"
              hint="Optional."
              listId={listId}
              value={mapping.balanceColumn}
              onChange={(balanceColumn) => setMapping({ balanceColumn })}
            />
            <SelectField
              id={ids.layout}
              label="How the money is written"
              value={mapping.amountLayout}
              onChange={(event) => setMapping({ amountLayout: event.target.value as ColumnMapping['amountLayout'] })}
            >
              <option value="TwoColumns">A debit and a credit column</option>
              <option value="OneColumn">One amount, below zero for money out</option>
            </SelectField>
            {mapping.amountLayout === 'OneColumn' ? (
              <ColumnField
                label="Amount column"
                required
                listId={listId}
                value={mapping.amountColumn}
                onChange={(amountColumn) => setMapping({ amountColumn })}
              />
            ) : (
              <>
                <ColumnField
                  label="Debit column (money out)"
                  required
                  listId={listId}
                  value={mapping.debitColumn}
                  onChange={(debitColumn) => setMapping({ debitColumn })}
                />
                <ColumnField
                  label="Credit column (money in)"
                  required
                  listId={listId}
                  value={mapping.creditColumn}
                  onChange={(creditColumn) => setMapping({ creditColumn })}
                />
              </>
            )}
            <SelectField
              id={ids.style}
              label="Decimals"
              value={mapping.decimalStyle}
              onChange={(event) => setMapping({ decimalStyle: event.target.value as ColumnMapping['decimalStyle'] })}
            >
              <option value="Point">1,23,456.78</option>
              <option value="Comma">1.23.456,78</option>
            </SelectField>
          </div>
          <datalist id={listId}>
            {headers.map((header) => (
              <option key={header} value={header} />
            ))}
          </datalist>
        </fieldset>
      )}

      {save.error !== null && <ProblemAlert problem={problemOf(save.error)} />}
      {toggle.error !== null && <ProblemAlert problem={problemOf(toggle.error)} />}
      <div className={forms.actions}>
        <button type="submit" className={buttons.primary} disabled={save.isPending}>
          {save.isPending ? 'Saving…' : channel === null ? 'Set up the channel' : 'Save'}
        </button>
        {channel !== null && (
          <button
            type="button"
            className={buttons.secondary}
            disabled={toggle.isPending}
            onClick={() => toggle.mutate(channel.status !== 'Active')}
          >
            {channel.status === 'Active' ? 'Deactivate' : 'Reactivate'}
          </button>
        )}
      </div>
    </form>
  );
}

export function NewChannelPage() {
  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New settlement channel" back={{ to: '/administration/channels', label: 'Channels' }} />
      <AdministrationTabs />
      <ChannelForm channel={null} />
    </section>
  );
}

export function ChannelPage() {
  const { id = '' } = useParams();
  const channel = useChannel(id);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={channel.data?.name ?? 'Settlement channel'}
        back={{ to: '/administration/channels', label: 'Channels' }}
        subtitle={
          channel.data !== undefined &&
          (channel.data.status === 'Active' ? (
            <Badge tone="success">Active</Badge>
          ) : (
            <Badge tone="muted">Inactive: takes no import</Badge>
          ))
        }
      />
      <AdministrationTabs />
      {channel.data === undefined ? (
        <LoadState error={channel.error} what="the channel" />
      ) : (
        <>
          {channel.data.kind === 'Bank' && (
            <Banner tone="accent" title="Lines already imported keep what was read">
              A change of the mapping applies to the next statement only.
            </Banner>
          )}
          <ChannelForm key={channel.data.version} channel={channel.data} />
        </>
      )}
    </section>
  );
}
