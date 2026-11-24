import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { ReadOnlyField, TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { checkDecimal } from '../decimals';
import {
  comparedServiceItemFields,
  correctServiceItem,
  createServiceItem,
  emptyServiceItem,
  getServiceItem,
  serviceItemFields,
  serviceItemKeys,
  serviceItemValues,
  useServiceItem,
  type ServiceItem,
  type ServiceItemValues,
} from './api';

const required = (what: string) => (value: string) => value.trim() !== '' || `Enter the ${what}.`;

const taxRate = (value: string): true | string => {
  const shape = checkDecimal(value, 2, 'tax rate');
  return shape !== true ? shape : Number(value) <= 100 || 'The tax rate is at most 100.';
};

/** The fields of a service item (BR-201); the code only when creating, since it never changes. */
function ServiceItemFields({ form, fixedCode }: { form: UseFormReturn<ServiceItemValues>; fixedCode?: string }) {
  const {
    register,
    formState: { errors },
  } = form;

  return (
    <div className={forms.grid}>
      {fixedCode === undefined ? (
        <TextField
          id="item-code"
          label="Code"
          hint="Letters, digits and . - _ /, such as LAB/CBC. It never changes."
          autoComplete="off"
          error={errors.code?.message}
          {...register('code', { validate: required('code') })}
        />
      ) : (
        <ReadOnlyField label="Code" value={fixedCode} hint="The code never changes." />
      )}
      <TextField
        id="item-name"
        label="Name"
        autoComplete="off"
        error={errors.name?.message}
        {...register('name', { validate: required('name') })}
      />
      <TextField
        id="item-category"
        label="Category"
        hint="Optional: for grouping in lists and reports."
        error={errors.category?.message}
        {...register('category')}
      />
      <TextField
        id="item-price"
        label="Default unit price (₹)"
        inputMode="decimal"
        hint="0 or more, at most 4 decimals. 0 for a service that is free."
        error={errors.defaultUnitPrice?.message}
        {...register('defaultUnitPrice', { validate: (value) => checkDecimal(value, 4, 'default unit price') })}
      />
      <TextField
        id="item-tax-rate"
        label="Tax rate (%)"
        inputMode="decimal"
        hint="0 to 100, at most 2 decimals. 0 for a service that is not taxed."
        error={errors.taxRatePercent?.message}
        {...register('taxRatePercent', { validate: taxRate })}
      />
    </div>
  );
}

/** Adds a service item to the catalogue; a code already taken is refused by the API, an inactive item's included. */
export function NewServiceItemPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<ServiceItemValues>({ defaultValues: emptyServiceItem });
  const outcome = useRecordForm<ServiceItem, ServiceItemValues>({ form, fields: serviceItemFields });

  const creating = useMutation({
    mutationFn: (values: ServiceItemValues) => createServiceItem(client, values),
    onMutate: outcome.clear,
    onSuccess: (item) => {
      queryClient.setQueryData(serviceItemKeys.one(item.id), item);
      void queryClient.invalidateQueries({ queryKey: [...serviceItemKeys.all, 'list'] });
      void navigate(`/service-items/${item.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New service item" back={{ to: '/service-items', label: 'Service items' }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => creating.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={serviceItemFields} />}
        <ServiceItemFields form={form} />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={creating.isPending}
            aria-busy={creating.isPending}
          >
            {creating.isPending ? 'Creating…' : 'Create service item'}
          </button>
          <Link to="/service-items">Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a service item: every field but the code, sent with the version the form started from (ADR 0039). */
export function EditServiceItemPage() {
  const { id = '' } = useParams();
  const item = useServiceItem(id);

  return item.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit service item" back={{ to: `/service-items/${id}`, label: 'Service item' }} />
      <LoadState error={item.error} what="the service item" />
    </section>
  ) : (
    <EditServiceItemForm key={item.data.id} initial={item.data} />
  );
}

function EditServiceItemForm({ initial }: { initial: ServiceItem }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<ServiceItemValues>({ defaultValues: serviceItemValues(initial) });
  const outcome = useRecordForm<ServiceItem, ServiceItemValues>({
    form,
    fields: serviceItemFields,
    initial,
    compared: comparedServiceItemFields,
    valuesOf: serviceItemValues,
    reload: () => getServiceItem(client, initial.id),
  });

  const correcting = useMutation({
    mutationFn: ({ values, version }: { values: ServiceItemValues; version: string }) =>
      correctServiceItem(client, initial.id, version, values),
    onMutate: outcome.clear,
    onSuccess: (item) => {
      queryClient.setQueryData(serviceItemKeys.one(item.id), item);
      void queryClient.invalidateQueries({ queryKey: [...serviceItemKeys.all, 'list'] });
      void navigate(`/service-items/${item.id}`);
    },
    onError: outcome.failed,
  });

  const base = outcome.base ?? initial;

  return (
    <section aria-labelledby="page-title">
      <PageHeader title={`Edit ${initial.name}`} back={{ to: `/service-items/${initial.id}`, label: initial.name }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) =>
          void form.handleSubmit((values) => correcting.mutate({ values, version: base.version }))(event)
        }
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={serviceItemFields} />}
        {outcome.conflict !== null && (
          <ConflictNotice
            changes={outcome.conflict.changes}
            onLoadLatest={outcome.loadLatest}
            onKeepMine={outcome.keepMine}
          />
        )}
        {outcome.kept && (
          <p className={forms.note} role="status">
            Your changes are kept. Saving now replaces the latest version.
          </p>
        )}
        <ServiceItemFields form={form} fixedCode={initial.code} />
        <p className={forms.note}>
          A changed price or tax rate applies to new bill lines; lines already written keep theirs.
        </p>
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={correcting.isPending}
            aria-busy={correcting.isPending}
          >
            {correcting.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/service-items/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
