import { useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { ApplyToBills } from '../bills/ApplyToBills';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { usePayer } from '../payers/api';
import { applyCredit, creditNoteKeys, creditNotesChanged, useCreditNote, type CreditNote } from './api';

/**
 * Applies a posted credit note's available credit to the payer's bills (BR-213, ADR 0042 point 5): the same form as a
 * payment's allocation, the bills that owe something oldest first, "Apply oldest first", the caps checked before sending.
 */
export function ApplyCreditPage() {
  const { id = '' } = useParams();
  const creditNote = useCreditNote(id);

  if (creditNote.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Apply credit" back={{ to: `/credit-notes/${id}`, label: 'Credit note' }} />
        <LoadState error={creditNote.error} what="the credit note" />
      </section>
    );
  }

  return creditNote.data.status === 'Posted' ? (
    <ApplyCreditForm key={creditNote.data.id} creditNote={creditNote.data} />
  ) : (
    <section aria-labelledby="page-title">
      <PageHeader title="Apply credit" back={{ to: `/credit-notes/${id}`, label: 'Credit note' }} />
      <p className={forms.note}>Only a posted credit note has credit to apply.</p>
    </section>
  );
}

function ApplyCreditForm({ creditNote }: { creditNote: CreditNote }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const payer = usePayer(creditNote.payerId);
  const number = creditNote.number ?? 'the credit note';

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Apply ${number}`}
        back={{ to: `/credit-notes/${creditNote.id}`, label: number }}
        subtitle={payer.data === undefined ? undefined : `To the bills of ${payer.data.name}`}
      />
      <ApplyToBills
        payerId={creditNote.payerId}
        available={creditNote.availableAmount}
        availableLabel="Available credit"
        availableWord="available"
        words={{ submit: 'Apply credit', submitting: 'Applying…', applied: 'Applied here' }}
        linesField="applications"
        identity={`POST /credit-notes/${creditNote.id}/applications ${creditNote.version}`}
        send={(lines, key) => applyCredit(client, creditNote, lines, key)}
        onDone={(data) => {
          queryClient.setQueryData(creditNoteKeys.one(creditNote.id), data);
          creditNotesChanged(queryClient);
          void navigate(`/credit-notes/${creditNote.id}`);
        }}
        onConflict={() => {
          creditNotesChanged(queryClient);
          void queryClient.invalidateQueries({ queryKey: creditNoteKeys.one(creditNote.id) });
        }}
        conflictMessage="Someone else changed this credit note or one of these bills first. Nothing was applied; the credit note and the bills are shown as they are now. Check the amounts and apply again."
        cancelTo={`/credit-notes/${creditNote.id}`}
      />
    </section>
  );
}
