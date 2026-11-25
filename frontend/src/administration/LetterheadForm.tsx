import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import {
  letterheadFields,
  letterheadValues,
  updateLetterhead,
  userKeys,
  type LetterheadValues,
  type TenantSettings,
} from './api';

const shown = (value: string | null) => value ?? 'None';

/**
 * What the hospital prints at the head and foot of its documents (Prompt 10.4, ADR 0054): its legal name, address,
 * GSTIN and the line under a signature. Every part is optional; the state is the place of supply of every document.
 * It shares the version of the settings above it, so a save here is told to the page, which gives that form the new
 * version.
 */
export function LetterheadForm({ initial, onSaved }: { initial: TenantSettings; onSaved: () => void }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState(false);
  const form = useForm<LetterheadValues>({ defaultValues: letterheadValues(initial) });
  const outcome = useRecordForm<TenantSettings, LetterheadValues>({
    form,
    fields: letterheadFields,
    initial,
    compared: [
      { label: 'Legal name', value: (value) => shown(value.letterhead.legalName) },
      { label: 'Address', value: (value) => shown(value.letterhead.addressLine1) },
      { label: 'City', value: (value) => shown(value.letterhead.city) },
      { label: 'State', value: (value) => shown(value.letterhead.state) },
      { label: 'GSTIN', value: (value) => shown(value.letterhead.gstin) },
      { label: 'Signatory line', value: (value) => shown(value.letterhead.signatory) },
    ],
    valuesOf: letterheadValues,
    reload: () => answer(client.GET('/api/v1/settings')),
  });
  const {
    register,
    formState: { errors },
  } = form;
  const base = outcome.base ?? initial;

  const saving = useMutation({
    mutationFn: (values: LetterheadValues) => updateLetterhead(client, base.version, values),
    onMutate: () => {
      setSaved(false);
      outcome.clear();
    },
    onSuccess: (next) => {
      queryClient.setQueryData(userKeys.settings, next);
      outcome.rebase(next);
      form.reset(letterheadValues(next));
      setSaved(true);
      onSaved();
    },
    onError: outcome.failed,
  });

  return (
    <section className={forms.section} aria-labelledby="letterhead-title">
      <h2 id="letterhead-title">Letterhead</h2>
      <p className={forms.note}>
        Printed at the head and foot of every bill, receipt and refund voucher of {initial.letterhead.name}. Leave a
        part empty and it is not printed.
      </p>
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => saving.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={[...letterheadFields]} />}
        {outcome.conflict !== null && (
          <ConflictNotice
            changes={outcome.conflict.changes}
            onLoadLatest={outcome.loadLatest}
            onKeepMine={outcome.keepMine}
          />
        )}
        {saved && (
          <Banner tone="success" title="Saved" live>
            Documents printed from now on carry this letterhead.
          </Banner>
        )}
        <div className={forms.grid}>
          <TextField
            id="letterhead-legal-name"
            label="Legal name"
            hint="When it differs from the hospital's name, for example with Private Limited."
            maxLength={200}
            error={errors.legalName?.message}
            {...register('legalName')}
          />
          <TextField
            id="letterhead-gstin"
            label="GSTIN"
            hint="Fifteen letters and digits, if the hospital is registered."
            maxLength={15}
            error={errors.gstin?.message}
            {...register('gstin', {
              validate: (value) =>
                value.trim() === '' ||
                /^[0-9A-Za-z]{15}$/.test(value.trim()) ||
                'The GSTIN has 15 characters, each a letter or a digit.',
            })}
          />
          <TextField
            id="letterhead-address-1"
            label="Address, first line"
            maxLength={200}
            error={errors.addressLine1?.message}
            {...register('addressLine1')}
          />
          <TextField
            id="letterhead-address-2"
            label="Address, second line"
            maxLength={200}
            error={errors.addressLine2?.message}
            {...register('addressLine2')}
          />
          <TextField
            id="letterhead-city"
            label="City"
            maxLength={100}
            error={errors.city?.message}
            {...register('city')}
          />
          <TextField
            id="letterhead-state"
            label="State"
            hint="The place of supply of every document."
            maxLength={100}
            error={errors.state?.message}
            {...register('state')}
          />
          <TextField
            id="letterhead-pin"
            label="PIN code"
            inputMode="numeric"
            maxLength={6}
            error={errors.pinCode?.message}
            {...register('pinCode', {
              validate: (value) =>
                value.trim() === '' || /^\d{6}$/.test(value.trim()) || 'The PIN code has six digits.',
            })}
          />
          <TextField
            id="letterhead-phone"
            label="Phone"
            inputMode="tel"
            maxLength={20}
            error={errors.phone?.message}
            {...register('phone')}
          />
          <TextField
            id="letterhead-email"
            label="E-mail address"
            inputMode="email"
            maxLength={256}
            error={errors.email?.message}
            {...register('email', {
              validate: (value) =>
                value.trim() === '' || /^[^@\s]+@[^@\s]+$/.test(value.trim()) || 'Write the address as name@domain.',
            })}
          />
          <TextField
            id="letterhead-signatory"
            label="Signatory line"
            hint="Printed under the place of a signature, for example Authorised signatory."
            maxLength={200}
            error={errors.signatory?.message}
            {...register('signatory')}
          />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
            {saving.isPending ? 'Saving…' : 'Save letterhead'}
          </button>
        </div>
      </form>
    </section>
  );
}
