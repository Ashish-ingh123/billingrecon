import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { answer, ifMatch } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { SelectField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { useLetterhead, type Letterhead } from '../printing/api';
import { PrintPreview } from '../printing/PrintPreview';
import {
  documentLabels,
  layoutLimits,
  paperLabels,
  papersOf,
  printSettingsKey,
  settingsOf,
  usePrintSettings,
  type DocumentSettings,
  type PrintedDocumentType,
  type PrintLayout,
  type PrintSettings,
} from '../printing/settings';
import { AdministrationTabs } from './AdministrationTabs';
import styles from './Administration.module.css';

const documentTypes: readonly PrintedDocumentType[] = ['Bill', 'Payment', 'Refund', 'ShiftClosing'];

/** The settings as a form holds them: a measure as the text that was typed, a text that is none as an empty one. */
interface PrintValues {
  paper: PrintLayout['paper'];
  marginTopMm: string;
  marginRightMm: string;
  marginBottomMm: string;
  marginLeftMm: string;
  logoPosition: PrintLayout['logoPosition'];
  logoSize: PrintLayout['logoSize'];
  prePrintedStationery: boolean;
  stationeryTopMm: string;
  showPatientContact: boolean;
  showPayer: boolean;
  showSacColumn: boolean;
  showTaxSplit: boolean;
  showAmountInWords: boolean;
  showSignatureBlock: boolean;
  showCashierName: boolean;
  footerLine: string;
  terms: string;
}

const fields = [
  'paper',
  'marginTopMm',
  'marginRightMm',
  'marginBottomMm',
  'marginLeftMm',
  'logoPosition',
  'logoSize',
  'prePrintedStationery',
  'stationeryTopMm',
  'showPatientContact',
  'showPayer',
  'showSacColumn',
  'showTaxSplit',
  'showAmountInWords',
  'showSignatureBlock',
  'showCashierName',
  'footerLine',
  'terms',
] as const satisfies readonly (keyof PrintValues)[];

function valuesOf(settings: DocumentSettings): PrintValues {
  const { layout } = settings;
  return {
    ...layout,
    marginTopMm: String(layout.marginTopMm),
    marginRightMm: String(layout.marginRightMm),
    marginBottomMm: String(layout.marginBottomMm),
    marginLeftMm: String(layout.marginLeftMm),
    stationeryTopMm: String(layout.stationeryTopMm),
    footerLine: layout.footerLine ?? '',
    terms: layout.terms ?? '',
  };
}

/** A whole number of millimetres from what was typed, or null when it is none. */
function millimetres(text: string): number | null {
  return /^\d{1,3}$/.test(text.trim()) ? Number(text.trim()) : null;
}

/**
 * The layout the form holds now, for the preview and for a save. A measure that is not yet a number within its limits
 * (a field half typed) keeps the value the settings had, so the preview never jumps to nonsense; the form's own
 * validation is what stops such a value from being saved.
 */
function layoutFrom(values: PrintValues, base: PrintLayout, documentType: PrintedDocumentType): PrintLayout {
  const margin = (text: string, fallback: number) => {
    const mm = millimetres(text);
    return mm !== null && mm >= layoutLimits.marginMinMm && mm <= layoutLimits.marginMaxMm ? mm : fallback;
  };
  const space = millimetres(values.stationeryTopMm);
  const text = (value: string) => (value.trim() === '' ? null : value.trim());

  return {
    paper: values.paper,
    marginTopMm: margin(values.marginTopMm, base.marginTopMm),
    marginRightMm: margin(values.marginRightMm, base.marginRightMm),
    marginBottomMm: margin(values.marginBottomMm, base.marginBottomMm),
    marginLeftMm: margin(values.marginLeftMm, base.marginLeftMm),
    logoPosition: values.logoPosition,
    logoSize: values.logoSize,
    prePrintedStationery: values.prePrintedStationery,
    stationeryTopMm: space !== null && space <= layoutLimits.stationeryTopMaxMm ? space : base.stationeryTopMm,
    showPatientContact: values.showPatientContact,
    showPayer: values.showPayer,
    // What a bill must show is never switched off, whatever the form holds (ADR 0054, ADR 0059).
    showSacColumn: documentType === 'Bill' ? true : values.showSacColumn,
    showTaxSplit: documentType === 'Bill' ? true : values.showTaxSplit,
    showAmountInWords: values.showAmountInWords,
    showSignatureBlock: values.showSignatureBlock,
    showCashierName: values.showCashierName,
    footerLine: text(values.footerLine),
    terms: text(values.terms),
  };
}

const marginRule = {
  validate: (value: string) => {
    const mm = millimetres(value);
    return (
      (mm !== null && mm >= layoutLimits.marginMinMm && mm <= layoutLimits.marginMaxMm) ||
      `A margin is between ${String(layoutLimits.marginMinMm)} and ${String(layoutLimits.marginMaxMm)} mm.`
    );
  },
};

/**
 * How the hospital prints its bills, receipts and refund vouchers (Prompt 10.4c, ADR 0059): for each, the paper and
 * its margins, where the logo stands and how large, or pre-printed stationery, what is shown, and a footer line and
 * terms; beside the form, a sample drawn by the components that print, which follows the form as it is typed in.
 */
export function PrintSettingsPage() {
  const settings = usePrintSettings();
  const letterhead = useLetterhead();
  const [documentType, setDocumentType] = useState<PrintedDocumentType>('Bill');
  const groupId = useId();

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Users and settings"
        subtitle="Who signs in to this hospital, what each may do, and how the hospital's documents are numbered."
      />
      <AdministrationTabs />
      {settings.data === undefined || letterhead.data === undefined ? (
        <LoadState error={settings.error ?? letterhead.error} what="the print settings" />
      ) : (
        <>
          <fieldset className={styles.documents}>
            <legend id={groupId}>Document</legend>
            {documentTypes.map((type) => (
              <label key={type}>
                <input
                  type="radio"
                  name="print-document"
                  value={type}
                  checked={documentType === type}
                  onChange={() => setDocumentType(type)}
                />
                {documentLabels[type]}
              </label>
            ))}
          </fieldset>
          <PrintSettingsForm
            // A form of its own for each kind. It is not made anew when it saves: it takes the version its save
            // answered itself, and so keeps saying that it saved.
            key={documentType}
            initial={settingsOf(settings.data, documentType)}
            letterhead={letterhead.data}
          />
        </>
      )}
    </section>
  );
}

function PrintSettingsForm({ initial, letterhead }: { initial: DocumentSettings; letterhead: Letterhead }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const { documentType } = initial;
  const name = documentLabels[documentType].toLowerCase();
  const [saved, setSaved] = useState(false);
  const form = useForm<PrintValues>({ defaultValues: valuesOf(initial), mode: 'onChange' });
  const outcome = useRecordForm<DocumentSettings, PrintValues>({
    form,
    fields,
    initial,
    compared: [
      { label: 'Paper', value: (value) => paperLabels[value.layout.paper] },
      {
        label: 'Margins',
        value: ({ layout }) =>
          `${String(layout.marginTopMm)}, ${String(layout.marginRightMm)}, ${String(layout.marginBottomMm)}, ${String(layout.marginLeftMm)} mm`,
      },
      { label: 'Logo', value: ({ layout }) => `${layout.logoPosition}, ${layout.logoSize}` },
      { label: 'Footer line', value: ({ layout }) => layout.footerLine ?? 'None' },
    ],
    valuesOf,
    reload: async () => settingsOf(await answer(client.GET('/api/v1/settings/print')), documentType),
  });
  const {
    register,
    control,
    formState: { errors },
  } = form;
  const base = outcome.base ?? initial;
  const values = useWatch({ control }) as PrintValues;
  const layout = layoutFrom(values, base.layout, documentType);

  const saving = useMutation({
    mutationFn: (toSave: PrintLayout) =>
      answer(
        client.PUT('/api/v1/settings/print/{documentType}', {
          params: { path: { documentType } },
          body: toSave,
          headers: ifMatch(base.version),
        }),
      ),
    onMutate: () => {
      setSaved(false);
      outcome.clear();
    },
    onSuccess: (next: PrintSettings) => {
      const all = queryClient.getQueryData<PrintSettings[]>(printSettingsKey) ?? [];
      queryClient.setQueryData(
        printSettingsKey,
        all.map((one) => (one.documentType === next.documentType ? next : one)),
      );
      const mine = settingsOf([next], documentType);
      outcome.rebase(mine);
      form.reset(valuesOf(mine));
      setSaved(true);
    },
    onError: outcome.failed,
  });

  const locked = 'A tax invoice must show this: it cannot be switched off for a bill.';
  const switchOf = (field: keyof PrintValues, label: string, hint?: string) => (
    <label className={styles.switch}>
      <input type="checkbox" {...register(field)} />
      <span>
        {label}
        {hint !== undefined && <small>{hint}</small>}
      </span>
    </label>
  );

  return (
    <div className={styles.printSettings}>
      <form
        className={forms.form}
        noValidate
        aria-label={`How a ${name} is printed`}
        onSubmit={(event) =>
          void form.handleSubmit((submitted) => saving.mutate(layoutFrom(submitted, base.layout, documentType)))(event)
        }
      >
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
            Every {name} printed from now on is laid out this way.
          </Banner>
        )}

        <section className={forms.section} aria-labelledby="print-paper-title">
          <h2 id="print-paper-title">Paper</h2>
          <div className={forms.grid}>
            <SelectField id="print-paper" label="Paper size" error={errors.paper?.message} {...register('paper')}>
              {papersOf(documentType).map((paper) => (
                <option key={paper} value={paper}>
                  {paperLabels[paper]}
                </option>
              ))}
            </SelectField>
            {(
              [
                ['marginTopMm', 'Top margin, mm'],
                ['marginRightMm', 'Right margin, mm'],
                ['marginBottomMm', 'Bottom margin, mm'],
                ['marginLeftMm', 'Left margin, mm'],
              ] as const
            ).map(([field, label]) => (
              <TextField
                key={field}
                id={`print-${field}`}
                label={label}
                trailing="mm"
                inputMode="numeric"
                maxLength={2}
                error={errors[field]?.message}
                {...register(field, marginRule)}
              />
            ))}
          </div>
          {documentType === 'Bill' && (
            <p className={forms.note}>
              A bill is printed on A4 or A5: its lines do not fit a roll. On A5, keep the side margins at 12 mm or less,
              or the nine columns of a tax invoice may not fit the sheet.
            </p>
          )}
        </section>

        <section className={forms.section} aria-labelledby="print-head-title">
          <h2 id="print-head-title">Head of the sheet</h2>
          {switchOf(
            'prePrintedStationery',
            'Pre-printed stationery',
            'The paper carries the hospital’s head already: the sheet prints no logo, name or address of its own.',
          )}
          <div className={forms.grid}>
            {values.prePrintedStationery ? (
              <TextField
                id="print-stationery-top"
                label="Space for the paper's own head, mm"
                trailing="mm"
                inputMode="numeric"
                maxLength={2}
                hint={`0 to ${String(layoutLimits.stationeryTopMaxMm)} mm, left empty at the top of the sheet.`}
                error={errors.stationeryTopMm?.message}
                {...register('stationeryTopMm', {
                  validate: (value) => {
                    const mm = millimetres(value);
                    return (
                      (mm !== null && mm <= layoutLimits.stationeryTopMaxMm) ||
                      `The space is between 0 and ${String(layoutLimits.stationeryTopMaxMm)} mm.`
                    );
                  },
                })}
              />
            ) : (
              <>
                <SelectField id="print-logo-position" label="Logo" {...register('logoPosition')}>
                  <option value="Left">At the left</option>
                  <option value="Centre">In the centre</option>
                  <option value="Right">At the right</option>
                </SelectField>
                <SelectField id="print-logo-size" label="Logo size" {...register('logoSize')}>
                  <option value="Small">Small</option>
                  <option value="Medium">Medium</option>
                  <option value="Large">Large</option>
                </SelectField>
              </>
            )}
          </div>
        </section>

        <fieldset className={styles.switches}>
          <legend>What is shown</legend>
          {documentType !== 'ShiftClosing' && switchOf('showPayer', 'The payer')}
          {documentType !== 'ShiftClosing' && switchOf('showPatientContact', 'The patient’s phone and address')}
          {switchOf('showAmountInWords', 'The amount in words')}
          {switchOf('showSignatureBlock', 'The signature block')}
          {documentType !== 'Bill' &&
            documentType !== 'ShiftClosing' &&
            switchOf('showCashierName', 'The cashier’s name')}
          {documentType === 'Bill' && (
            <>
              <label className={styles.switch}>
                <input type="checkbox" checked disabled aria-describedby="print-locked-sac" readOnly />
                <span>
                  The SAC column<small id="print-locked-sac">{locked}</small>
                </span>
              </label>
              <label className={styles.switch}>
                <input type="checkbox" checked disabled aria-describedby="print-locked-tax" readOnly />
                <span>
                  The tax as CGST and SGST<small id="print-locked-tax">{locked}</small>
                </span>
              </label>
            </>
          )}
        </fieldset>

        <section className={forms.section} aria-labelledby="print-foot-title">
          <h2 id="print-foot-title">Foot of the sheet</h2>
          <TextField
            id="print-footer-line"
            label="Footer line"
            hint="One line at the very foot, for example the hospital's hours or a word of thanks."
            maxLength={layoutLimits.footerLine}
            error={errors.footerLine?.message}
            {...register('footerLine')}
          />
          <div className={styles.terms}>
            <label htmlFor="print-terms">Terms</label>
            <textarea
              id="print-terms"
              rows={4}
              maxLength={layoutLimits.terms}
              aria-describedby="print-terms-hint"
              aria-invalid={errors.terms !== undefined}
              {...register('terms')}
            />
            <p id="print-terms-hint">Plain text above the signature, printed as it is typed, line by line.</p>
            {errors.terms?.message !== undefined && <p role="alert">{errors.terms.message}</p>}
          </div>
        </section>

        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
            {saving.isPending ? 'Saving…' : `Save how a ${name} is printed`}
          </button>
        </div>
      </form>

      <section className={styles.previewPane} aria-labelledby="print-preview-title">
        <h2 id="print-preview-title">Preview</h2>
        <p className={forms.note}>
          A sample {name} as it will print, on {paperLabels[layout.paper]}. It follows the form before it is saved.
        </p>
        <div className={styles.previewScroll} tabIndex={0} role="region" aria-label="The sample, as it will print">
          <PrintPreview documentType={documentType} layout={layout} letterhead={letterhead} />
        </div>
      </section>
    </div>
  );
}
