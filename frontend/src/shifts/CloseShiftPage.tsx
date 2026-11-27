import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { isConflict, problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useSession } from '../auth/AuthContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import fieldStyles from '../components/Field.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatMoney } from '../format';
import { asRupees, paiseOf, paiseOfServer } from '../money';
import {
  closeShift,
  countedPaise,
  countLines,
  denominations,
  differenceWords,
  emptyCount,
  maxPieces,
  piecesOf,
  shiftKeys,
  shiftsChanged,
  useShift,
  type CashierShift,
  type CountText,
} from './api';
import styles from './Shifts.module.css';

/**
 * The cashier counts the drawer and closes the shift (US-46, BR-312 to BR-314). The count is typed by denomination and
 * added up here in exact paise against the expected cash the API read from the ledger; the API adds it up again. With
 * no difference the shift is closed; with one, a reason is asked for and the closing waits for a second person.
 */
export function CloseShiftPage() {
  const { id = '' } = useParams();
  const shift = useShift(id);
  const { userId } = useSession();

  if (shift.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Close the shift" back={{ to: `/shifts/${id}`, label: 'the shift' }} />
        <LoadState error={shift.error} what="the shift" />
      </section>
    );
  }

  if (shift.data.status !== 'Open' || shift.data.cashierUserId !== userId) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Close the shift" back={{ to: `/shifts/${id}`, label: 'the shift' }} />
        <Banner tone="warning" title="This shift cannot be counted here">
          {shift.data.status === 'Open'
            ? 'A shift is counted and closed by its own cashier.'
            : 'The shift is counted already. A closed shift does not change.'}
        </Banner>
      </section>
    );
  }

  // The form starts again from the shift as it is now when cash joined it in between (its version changed).
  return <CloseShiftForm key={shift.data.version} shift={shift.data} />;
}

function CloseShiftForm({ shift }: { shift: CashierShift }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [count, setCount] = useState<CountText>(emptyCount);
  const [loose, setLoose] = useState('');
  const [reason, setReason] = useState('');
  const [checked, setChecked] = useState(false);

  const expected = paiseOfServer(shift.expectedCash);
  const loosePaise = loose.trim() === '' ? 0n : paiseOf(loose);
  const looseError =
    loosePaise === null || loosePaise >= 100n ? 'The loose change is what is below one rupee: 0 to 0.99.' : undefined;
  const counted = loosePaise === null || looseError !== undefined ? null : countedPaise(count, loosePaise);
  const difference = counted === null ? null : counted - expected;
  const reasonError =
    difference !== null && difference !== 0n && reason.trim() === ''
      ? 'Say why the count differs from the expected cash.'
      : reason.length > 500
        ? 'The reason is at most 500 characters.'
        : undefined;

  const closing = useMutation({
    mutationFn: () => closeShift(client, shift, countLines(count), Number(asRupees(loosePaise ?? 0n)), reason),
    onSuccess: ({ shift: closed }) => {
      queryClient.setQueryData(shiftKeys.one(closed.id), closed);
      shiftsChanged(queryClient);
      void navigate(`/shifts/${closed.id}`);
    },
    onError: (error) => {
      // Cash joined the shift while it was counted: the shift is read again, and the form starts from it.
      if (isConflict(error)) {
        void queryClient.invalidateQueries({ queryKey: shiftKeys.one(shift.id) });
      }
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    setChecked(true);
    if (counted !== null && reasonError === undefined) {
      closing.mutate();
    }
  };

  const problem = closing.error === null ? null : problemOf(closing.error);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Close the shift"
        back={{ to: `/shifts/${shift.id}`, label: 'the shift' }}
        subtitle={`Your shift of ${formatDate(shift.businessDate)}. Count every note and coin in the drawer, the float included.`}
      />
      <form className={forms.form} noValidate onSubmit={submit}>
        {problem !== null && <ProblemAlert problem={problem} placedFields={['reason']} />}
        <div className={styles.wrap}>
          <table className={styles.count}>
            <caption className={fieldStyles.label}>The count of the drawer</caption>
            <thead>
              <tr>
                <th scope="col">Note or coin</th>
                <th scope="col" className={styles.number}>
                  Pieces
                </th>
                <th scope="col" className={styles.number}>
                  Amount
                </th>
              </tr>
            </thead>
            <tbody>
              {denominations.map((denomination) => {
                const text = count[denomination] ?? '';
                const pieces = piecesOf(text);
                return (
                  <tr key={denomination}>
                    <th scope="row">
                      <label htmlFor={`count-${String(denomination)}`}>₹{denomination}</label>
                    </th>
                    <td className={styles.number}>
                      <input
                        id={`count-${String(denomination)}`}
                        className={styles.pieces}
                        inputMode="numeric"
                        autoComplete="off"
                        placeholder="0"
                        value={text}
                        aria-invalid={pieces === null}
                        aria-describedby={pieces === null ? `count-${String(denomination)}-error` : undefined}
                        onChange={(event) => setCount({ ...count, [denomination]: event.target.value })}
                      />
                      {pieces === null && (
                        <p id={`count-${String(denomination)}-error`} className={fieldStyles.error}>
                          A whole number from 0 to {maxPieces.toLocaleString('en-IN')}.
                        </p>
                      )}
                    </td>
                    <td className={styles.number}>
                      {pieces === null ? '—' : formatMoney(asRupees(BigInt(denomination) * 100n * BigInt(pieces)))}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <div className={forms.grid}>
          <TextField
            id="count-loose"
            label="Loose change (₹)"
            leading="₹"
            inputMode="decimal"
            hint="The coins below one rupee, as an amount: 0.50 for a 50 paise coin."
            value={loose}
            onChange={(event) => setLoose(event.target.value)}
            error={looseError}
          />
        </div>

        <dl className={styles.figures} aria-live="polite">
          <div className={styles.figure}>
            <dt>Expected cash</dt>
            <dd>{formatMoney(shift.expectedCash)}</dd>
          </div>
          <div className={styles.figure}>
            <dt>Counted cash</dt>
            <dd>{counted === null ? '—' : formatMoney(asRupees(counted))}</dd>
          </div>
          <div className={styles.figure}>
            <dt>Difference</dt>
            <dd>{difference === null ? '—' : differenceWords(difference)}</dd>
          </div>
        </dl>
        <p className={forms.note}>
          Expected is the float of {formatMoney(shift.openingFloat)}, {formatMoney(shift.cashReceived)} taken and{' '}
          {formatMoney(shift.cashPaidOut)} paid out in this shift, as the ledger has them.
        </p>

        {difference !== null && difference !== 0n && (
          <div className={`${fieldStyles.field} ${forms.wide}`}>
            <label className={fieldStyles.label} htmlFor="count-reason">
              Why the count differs
            </label>
            <textarea
              id="count-reason"
              className={fieldStyles.input}
              rows={2}
              value={reason}
              aria-invalid={checked && reasonError !== undefined}
              onChange={(event) => setReason(event.target.value)}
            />
            <p className={fieldStyles.hint}>
              A closing with a difference waits for a supervisor, who reads this. Nothing is posted to the ledger.
            </p>
            {checked && reasonError !== undefined && <p className={fieldStyles.error}>{reasonError}</p>}
            {problem?.fieldErrors.reason?.[0] !== undefined && (
              <p className={fieldStyles.error}>{problem.fieldErrors.reason[0]}</p>
            )}
          </div>
        )}

        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={closing.isPending || counted === null}
            aria-busy={closing.isPending}
          >
            {closing.isPending
              ? 'Closing…'
              : difference !== null && difference !== 0n
                ? 'Send the closing for approval'
                : 'Close the shift'}
          </button>
          <Link to={`/shifts/${shift.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
