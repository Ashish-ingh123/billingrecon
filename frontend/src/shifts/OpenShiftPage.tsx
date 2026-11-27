import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, todayInIndia } from '../format';
import { asRupees, paiseOf } from '../money';
import { openShift, shiftKeys, shiftsChanged } from './api';

/** The message for a typed float, or null when it is one: rupees of zero or more with at most two decimals. */
function checkFloat(text: string): string | null {
  if (text.trim() === '') {
    return 'Enter the float; 0 when the drawer is empty.';
  }

  const paise = paiseOf(text);
  if (paise === null) {
    return 'Write the float as rupees with at most 2 decimals.';
  }

  return paise > 999999999999n ? 'The float is at most ₹9,999,999,999.99.' : null;
}

/**
 * Opens a shift for the signed-in user (US-45, BR-310): the cash the drawer holds now. The shift takes today's business
 * date and keeps it; a user has one shift at a time, which the API answers for.
 */
export function OpenShiftPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [float, setFloat] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

  const opening = useMutation({
    mutationFn: (openingFloat: number) => openShift(client, openingFloat),
    onSuccess: (shift) => {
      queryClient.setQueryData(shiftKeys.one(shift.id), shift);
      shiftsChanged(queryClient);
      void navigate(`/shifts/${shift.id}`);
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const message = checkFloat(float);
    setError(message ?? undefined);
    const paise = paiseOf(float);
    if (message === null && paise !== null) {
      opening.mutate(Number(asRupees(paise)));
    }
  };

  const problem = opening.error === null ? null : problemOf(opening.error);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Open a shift"
        back={{ to: '/shifts', label: 'Shifts' }}
        subtitle={`The shift takes today's business date, ${formatDate(todayInIndia())}, and the cash you take in it is dated that day.`}
      />
      <form className={forms.form} noValidate onSubmit={submit}>
        {problem !== null && <ProblemAlert problem={problem} placedFields={['openingFloat']} />}
        <div className={forms.grid}>
          <TextField
            id="shift-float"
            label="Opening float (₹)"
            leading="₹"
            requiredMark
            inputMode="decimal"
            hint="The cash in your drawer before you take any. 0 when it is empty."
            value={float}
            onChange={(event) => setFloat(event.target.value)}
            error={error ?? problem?.fieldErrors.openingFloat?.[0]}
          />
        </div>
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={opening.isPending} aria-busy={opening.isPending}>
            {opening.isPending ? 'Opening…' : 'Open shift'}
          </button>
          <Link to="/shifts">Cancel</Link>
        </div>
      </form>
    </section>
  );
}
