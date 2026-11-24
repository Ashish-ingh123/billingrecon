import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { useForm, type Path, type UseFormReturn } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { answer } from '../api/call';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import fieldStyles from '../components/Field.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { todayInIndia } from '../format';
import { usePatient, type Patient } from '../patients/api';
import { usePayer, type Payer } from '../payers/api';
import {
  billKeys,
  billsChanged,
  billValues,
  comparedBillFields,
  correctHeader,
  createBill,
  getBill,
  headerChanged,
  headerFields,
  lineField,
  linesChanged,
  newBillRequest,
  replaceLines,
  useBill,
  type Bill,
  type BillValues,
} from './api';
import { LinesEditor } from './LinesEditor';
import { PatientPicker, PayerPicker } from './pickers';

const retryNote = 'Nothing is done twice: saving again with nothing changed finishes the same request.';

/** The header of a draft: its dates, the visit it belongs to, and notes (US-20, BR-205). */
function HeaderFields({ form, dueDateRequired }: { form: UseFormReturn<BillValues>; dueDateRequired: boolean }) {
  const {
    register,
    formState: { errors },
  } = form;
  const today = todayInIndia();

  return (
    <>
      <TextField
        id="bill-date"
        label="Bill date"
        requiredMark
        type="date"
        max={today}
        hint="Today in India, or a few days back within the hospital's limit."
        error={errors.billDate?.message}
        {...register('billDate', { validate: (value) => value !== '' || 'Enter the bill date.' })}
      />
      <TextField
        id="bill-due-date"
        label="Due date"
        type="date"
        hint={dueDateRequired ? undefined : "Leave empty for the hospital's default."}
        error={errors.dueDate?.message}
        {...register('dueDate', {
          validate: (value) => !dueDateRequired || value !== '' || 'Enter the due date.',
        })}
      />
      <TextField
        id="bill-encounter"
        label="Encounter reference"
        hint="The visit or admission in the hospital's own system."
        error={errors.encounterReference?.message}
        {...register('encounterReference')}
      />
      <div className={`${fieldStyles.field} ${forms.wide}`}>
        <label className={fieldStyles.label} htmlFor="bill-notes">
          Notes
        </label>
        <textarea
          id="bill-notes"
          className={fieldStyles.input}
          rows={2}
          aria-invalid={errors.notes !== undefined}
          {...register('notes')}
        />
        {errors.notes?.message !== undefined && <p className={fieldStyles.error}>{errors.notes.message}</p>}
      </div>
    </>
  );
}

/** The editor's names for the fields of the API's answer: the header's as they are, a line's by its position. */
function editorField(apiField: string): Path<BillValues> | null {
  return (headerFields as readonly string[]).includes(apiField) ? (apiField as Path<BillValues>) : lineField(apiField);
}

/**
 * A new draft (US-20): the patient, the payer (the patient's own by default), the header and the lines, created in one
 * request with an `Idempotency-Key` that a retry of the same request reuses (ADR 0040 points 1 and 2).
 */
export function NewBillPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const [patient, setPatient] = useState<Patient | null>(null);
  const [payer, setPayer] = useState<Payer | null>(null);
  const [partyErrors, setPartyErrors] = useState<{ patientId?: string; payerId?: string }>({});
  const [retryable, setRetryable] = useState(false);
  const form = useForm<BillValues>({
    defaultValues: { billDate: todayInIndia(), dueDate: '', encounterReference: '', notes: '', lines: [] },
  });
  const outcome = useRecordForm<Bill, BillValues>({ form, fields: [...headerFields], mapField: editorField });

  const choosePatient = async (chosen: Patient | null) => {
    setPatient(chosen);
    setPartyErrors((errors) => ({ ...errors, patientId: undefined }));
    if (chosen === null || payer !== null) {
      return;
    }

    // The patient's own payer, when they have an active one, owes the bill unless the user chooses another.
    const own = await answer(
      client.GET('/api/v1/payers', { params: { query: { PatientId: chosen.id, PageSize: 1 } } }),
    ).catch(() => null);
    const first = own?.items[0];
    if (first !== undefined && first.status === 'Active') {
      setPayer((current) => current ?? first);
    }
  };

  const creating = useMutation({
    mutationFn: (body: ReturnType<typeof newBillRequest>) =>
      send(`POST /bills ${JSON.stringify(body)}`, (key) => createBill(client, body, key)),
    onMutate: () => {
      outcome.clear();
      setRetryable(false);
    },
    onSuccess: (bill) => {
      queryClient.setQueryData(billKeys.one(bill.id), bill);
      billsChanged(queryClient);
      void navigate(`/bills/${bill.id}`);
    },
    onError: async (error) => {
      setRetryable(isRetryable(error));
      await outcome.failed(error);
    },
  });

  // The parties are asked for alongside the form's own fields, so every missing thing shows at once.
  const submit = (event: FormEvent) => {
    setPartyErrors({
      ...(patient === null ? { patientId: 'Choose the patient.' } : {}),
      ...(payer === null ? { payerId: 'Choose the payer.' } : {}),
    });

    return form.handleSubmit((values) => {
      if (patient !== null && payer !== null) {
        creating.mutate(newBillRequest(patient.id, payer.id, values));
      }
    })(event);
  };

  const problemFields = outcome.problem?.fieldErrors ?? {};

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New bill" back={{ to: '/bills', label: 'Bills' }} />
      <form className={forms.form} noValidate onSubmit={(event) => void submit(event)}>
        {outcome.problem !== null && (
          <ProblemAlert problem={outcome.problem} placedFields={[...outcome.placed, 'patientId', 'payerId']} />
        )}
        {retryable && <p className={forms.note}>{retryNote}</p>}
        <div className={forms.grid}>
          <div className={forms.wide}>
            <PatientPicker
              selected={patient}
              onSelect={(chosen) => void choosePatient(chosen)}
              error={partyErrors.patientId ?? problemFields.patientId?.[0]}
            />
          </div>
          <div className={forms.wide}>
            <PayerPicker
              selected={payer}
              onSelect={(chosen) => {
                setPayer(chosen);
                setPartyErrors((errors) => ({ ...errors, payerId: undefined }));
              }}
              error={partyErrors.payerId ?? problemFields.payerId?.[0]}
            />
          </div>
          <HeaderFields form={form} dueDateRequired={false} />
        </div>
        <LinesEditor form={form} />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={creating.isPending}
            aria-busy={creating.isPending}
          >
            {creating.isPending ? 'Saving…' : 'Save draft'}
          </button>
          <Link to="/bills">Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a draft: its header and its lines, each sent only when it changed, with the version it started from. */
export function EditBillPage() {
  const { id = '' } = useParams();
  const bill = useBill(id);

  if (bill.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Edit bill" back={{ to: `/bills/${id}`, label: 'Bill' }} />
        <LoadState error={bill.error} what="the bill" />
      </section>
    );
  }

  return bill.data.status === 'Draft' ? (
    <EditBillForm key={bill.data.id} initial={bill.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit bill" back={{ to: `/bills/${id}`, label: 'Bill' }} />
      <p className={forms.note}>
        Only a draft can be changed; a posted bill is corrected by a credit note or an adjustment.
      </p>
    </section>
  );
}

function EditBillForm({ initial }: { initial: Bill }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const patient = usePatient(initial.patientId);
  const payer = usePayer(initial.payerId);
  const form = useForm<BillValues>({ defaultValues: billValues(initial) });
  const outcome = useRecordForm<Bill, BillValues>({
    form,
    fields: [...headerFields],
    initial,
    compared: comparedBillFields,
    valuesOf: billValues,
    reload: () => getBill(client, initial.id),
    mapField: editorField,
  });
  const [headerSaved, setHeaderSaved] = useState(false);
  // The header saved by the attempt that is running, for the message when its lines are refused.
  const savedThisAttempt = useRef(false);

  const base = outcome.base ?? initial;

  const saving = useMutation({
    mutationFn: async (values: BillValues) => {
      savedThisAttempt.current = false;
      let current = base;
      if (headerChanged(values, current)) {
        current = await correctHeader(client, current, values);
        savedThisAttempt.current = true;
        outcome.rebase(current);
        queryClient.setQueryData(billKeys.one(current.id), current);
      }

      if (linesChanged(values, current)) {
        current = await replaceLines(client, current, values);
      }

      return current;
    },
    onMutate: () => {
      outcome.clear();
      setHeaderSaved(false);
    },
    onSuccess: (bill) => {
      queryClient.setQueryData(billKeys.one(bill.id), bill);
      billsChanged(queryClient);
      void navigate(`/bills/${bill.id}`);
    },
    onError: async (error) => {
      setHeaderSaved(savedThisAttempt.current);
      if (savedThisAttempt.current) {
        billsChanged(queryClient);
      }

      await outcome.failed(error);
    },
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit draft bill" back={{ to: `/bills/${initial.id}`, label: 'Draft bill' }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => saving.mutate(values))(event)}
      >
        {headerSaved && (
          <p className={forms.note} role="status">
            The dates, reference and notes were saved; the lines were not. Correct them and save again.
          </p>
        )}
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={outcome.placed} />}
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
        <div className={forms.grid}>
          <div className={forms.wide}>
            <PatientPicker selected={patient.data ?? null} onSelect={() => undefined} disabled />
          </div>
          <div className={forms.wide}>
            <PayerPicker selected={payer.data ?? null} onSelect={() => undefined} disabled />
          </div>
          <HeaderFields form={form} dueDateRequired />
        </div>
        <LinesEditor form={form} />
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
            {saving.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/bills/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
