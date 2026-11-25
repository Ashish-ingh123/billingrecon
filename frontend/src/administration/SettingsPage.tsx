import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { answer, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import table from '../components/DataTable.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { formatMoney } from '../format';
import { AdministrationTabs } from './AdministrationTabs';
import { LetterheadForm } from './LetterheadForm';
import {
  documentTypes,
  requestPrefixChange,
  settingsValues,
  updateSettings,
  userKeys,
  useSettings,
  type DocumentType,
  type SettingsValues,
  type TenantSettings,
} from './api';
import styles from './Administration.module.css';

const settingsFields = ['postingApprovalThreshold', 'maxBackdateDays', 'defaultDueDays'] as const;

/**
 * The hospital's settings (ADR 0033, ADR 0051), for its administrator: the approval threshold of a bill, how far back a
 * document may be dated, the default due days; the letterhead of its printed documents (ADR 0054); and the prefix of
 * each numbered document, whose change is a request a second person approves in the inbox.
 */
export function SettingsPage() {
  const settings = useSettings();
  // The two forms save the same row under one version (ADR 0054): when one saves, the other is given the new version
  // by being made anew.
  const [made, setMade] = useState({ settings: 0, letterhead: 0 });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="Who signs in to this hospital, what each may do, and how the hospital's documents are numbered."
      />
      <AdministrationTabs />
      {settings.data === undefined ? (
        <LoadState error={settings.error} what="the settings" />
      ) : (
        <>
          <SettingsForm
            key={`settings-${String(made.settings)}`}
            initial={settings.data}
            onSaved={() => setMade((now) => ({ ...now, letterhead: now.letterhead + 1 }))}
          />
          <LetterheadForm
            key={`letterhead-${String(made.letterhead)}`}
            initial={settings.data}
            onSaved={() => setMade((now) => ({ ...now, settings: now.settings + 1 }))}
          />
          <Prefixes settings={settings.data} />
        </>
      )}
    </section>
  );
}

function SettingsForm({ initial, onSaved }: { initial: TenantSettings; onSaved: () => void }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(false);
  const form = useForm<SettingsValues>({ defaultValues: settingsValues(initial) });
  const outcome = useRecordForm<TenantSettings, SettingsValues>({
    form,
    fields: settingsFields,
    initial,
    compared: [
      {
        label: 'Approval threshold',
        value: (value) =>
          value.postingApprovalThreshold === null ? 'None' : formatMoney(value.postingApprovalThreshold),
      },
      { label: 'Back-date window', value: (value) => `${String(value.maxBackdateDays)} days` },
      { label: 'Default due days', value: (value) => `${String(value.defaultDueDays)} days` },
    ],
    valuesOf: settingsValues,
    reload: () => answer(client.GET('/api/v1/settings')),
  });
  const {
    register,
    formState: { errors },
  } = form;
  const base = outcome.base ?? initial;

  const saving = useMutation({
    mutationFn: (values: SettingsValues) => updateSettings(client, base.version, values),
    onMutate: () => {
      setSaved(false);
      outcome.clear();
    },
    onSuccess: (next) => {
      queryClient.setQueryData(userKeys.settings, next);
      outcome.rebase(next);
      form.reset(settingsValues(next));
      setSaved(true);
      onSaved();
    },
    onError: outcome.failed,
  });

  const wholeDays = (what: string, most: number) => (value: string) =>
    (/^\d+$/.test(value.trim()) && Number(value) <= most) ||
    `The ${what} is a whole number of days, at most ${String(most)}.`;

  return (
    <section className={forms.section} aria-labelledby="settings-title">
      <h2 id="settings-title">Bills and dates</h2>
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => saving.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={[...settingsFields]} />}
        {outcome.conflict !== null && (
          <ConflictNotice
            changes={outcome.conflict.changes}
            onLoadLatest={outcome.loadLatest}
            onKeepMine={outcome.keepMine}
          />
        )}
        {saved && (
          <Banner tone="success" title="Saved" live>
            The settings apply to every document from now on.
          </Banner>
        )}
        <div className={forms.grid}>
          <TextField
            id="setting-threshold"
            label="Approval threshold (₹)"
            leading="₹"
            inputMode="decimal"
            hint="A bill of this total or more waits for a second person before it posts. Leave empty for none."
            error={errors.postingApprovalThreshold?.message}
            {...register('postingApprovalThreshold', {
              validate: (value) =>
                value.trim() === '' ||
                /^\d+(\.\d{1,2})?$/.test(value.trim()) ||
                'Write rupees with at most 2 decimals.',
            })}
          />
          <TextField
            id="setting-backdate"
            label="Back-date window (days)"
            trailing="days"
            inputMode="numeric"
            requiredMark
            hint="How many days back a bill or a payment may be dated."
            error={errors.maxBackdateDays?.message}
            {...register('maxBackdateDays', { validate: wholeDays('back-date window', 365) })}
          />
          <TextField
            id="setting-due-days"
            label="Default due days"
            trailing="days"
            inputMode="numeric"
            requiredMark
            hint="When a new bill falls due, counted from its date."
            error={errors.defaultDueDays?.message}
            {...register('defaultDueDays', { validate: wholeDays('default due days', 365) })}
          />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
            {saving.isPending ? 'Saving…' : 'Save settings'}
          </button>
        </div>
      </form>
    </section>
  );
}

/** The prefix of each numbered document (BR-801, ADR 0033): a change is a request that a second person approves. */
function Prefixes({ settings }: { settings: TenantSettings }) {
  const [changing, setChanging] = useState<DocumentType | null>(null);
  const [requested, setRequested] = useState<string | null>(null);

  return (
    <section className={forms.section} aria-labelledby="prefixes-title">
      <h2 id="prefixes-title">Document numbers</h2>
      <p className={forms.note}>
        Each document is numbered PREFIX/YY-YY/NNNNNN, without a gap, from 1 April each year. A new prefix takes effect
        when a Supervisor or another administrator approves it in the inbox.
      </p>
      {requested !== null && (
        <Banner tone="warning" title="Waiting for approval" live>
          {requested}
        </Banner>
      )}
      <div className={table.wrap}>
        <table className={table.table}>
          <thead>
            <tr>
              <th scope="col">Document</th>
              <th scope="col">Prefix</th>
              <th scope="col">
                <span className={styles.visuallyHidden}>Change</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {documentTypes.map(({ type, label, key }) => (
              <tr key={type}>
                <td>{label}</td>
                <td>
                  <code>{settings.prefixes[key]}</code>
                </td>
                <td>
                  {changing === type ? null : (
                    <button
                      type="button"
                      className={buttons.link}
                      aria-label={`Request a change of the prefix of ${label.toLowerCase()}`}
                      onClick={() => {
                        setRequested(null);
                        setChanging(type);
                      }}
                    >
                      Request a change
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {changing !== null && (
        <PrefixRequest
          documentType={changing}
          current={settings.prefixes[documentTypes.find((each) => each.type === changing)?.key ?? 'bill']}
          onClose={() => setChanging(null)}
          onRequested={(message) => {
            setChanging(null);
            setRequested(message);
          }}
        />
      )}
    </section>
  );
}

function PrefixRequest({
  documentType,
  current,
  onClose,
  onRequested,
}: {
  documentType: DocumentType;
  current: string;
  onClose: () => void;
  onRequested: (message: string) => void;
}) {
  const client = useApiClient();
  const [prefix, setPrefix] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);
  const label = documentTypes.find((each) => each.type === documentType)?.label ?? documentType;

  const requesting = useMutation({
    mutationFn: () => requestPrefixChange(client, documentType, prefix, comment),
    onSuccess: (change) =>
      onRequested(`The prefix of ${label.toLowerCase()} becomes ${change.toPrefix} once a second person approves it.`),
  });
  const refused = requesting.error === null ? null : problemOf(requesting.error);

  return (
    <form
      className={`${forms.form} ${styles.prefixForm}`}
      noValidate
      aria-label={`A new prefix for ${label.toLowerCase()}`}
      onSubmit={(event) => {
        event.preventDefault();
        if (!/^[A-Za-z0-9]{1,3}$/.test(prefix.trim())) {
          setError('A prefix has 1 to 3 characters, each a letter or a digit.');
          return;
        }

        setError(undefined);
        requesting.mutate();
      }}
    >
      {refused !== null && <ProblemAlert problem={refused} placedFields={['prefix']} />}
      <TextField
        id="prefix-new"
        label={`New prefix for ${label.toLowerCase()} (now ${current})`}
        requiredMark
        autoComplete="off"
        error={error ?? refused?.fieldErrors.prefix?.[0]}
        value={prefix}
        onChange={(event) => setPrefix(event.target.value.toUpperCase())}
      />
      <TextField
        id="prefix-comment"
        label="Why (for the person who approves)"
        autoComplete="off"
        value={comment}
        onChange={(event) => setComment(event.target.value)}
      />
      <div className={forms.actions}>
        <button
          type="submit"
          className={buttons.primary}
          disabled={requesting.isPending}
          aria-busy={requesting.isPending}
        >
          {requesting.isPending ? 'Sending…' : 'Send for approval'}
        </button>
        <button type="button" className={buttons.link} onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  );
}
