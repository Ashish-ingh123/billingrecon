import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatDecimal } from '../format';
import { asRupees, paiseOf, paiseOfServer } from '../money';
import { mostOpenBills, usePayerOpenBills } from './api';

/** The most bills one request names (the API's limit, for allocations and applications of credit alike). */
const mostLines = 50;

/** One bill and the amount applied to it, in rupees as text. */
export interface ApplyLine {
  billId: string;
  amount: string;
}

/** The amount typed for each bill, by bill id. */
type Amounts = Record<string, string>;

export interface ApplyToBillsProps<T> {
  payerId: string;
  /** What there is to apply, as the server answered it: a payment's unapplied cash or a credit note's available credit. */
  available: number | string;
  /** How the summary names it: "Unapplied cash", "Available credit". */
  availableLabel: string;
  /** How the refusal of a total names it: "unapplied", "available". */
  availableWord: string;
  /** The words of the button: "Allocate" and "Allocating…", and of the summary: "Allocated here". */
  words: { submit: string; submitting: string; applied: string };
  /** The list the API names a line in: "allocations" or "applications" (`allocations[1].amount`). */
  linesField: string;
  /** The operation, for its key: the method, the path and the version it starts from (ADR 0040 point 2). */
  identity: string;
  send: (lines: readonly ApplyLine[], key: string) => Promise<T>;
  onDone: (data: T) => void;
  /** Reads the document and the bills again after a conflict. */
  onConflict: () => void;
  conflictMessage: string;
  cancelTo: string;
}

/**
 * Applies money to a payer's bills (ADR 0041 point 1, ADR 0042 point 5): the bills that owe something, oldest first, each
 * with its balance due; "Apply oldest first" fills them from what is available. The form refuses an amount above a
 * bill's balance, a total above what is available and an empty request before sending; the server checks again under
 * its lock. A payment's allocation and a credit note's application of credit are the same form.
 */
export function ApplyToBills<T>(props: ApplyToBillsProps<T>) {
  const { payerId, available, availableLabel, availableWord, words, linesField } = props;
  const send = useIdempotentRequest();
  const open = usePayerOpenBills(payerId);
  const [amounts, setAmounts] = useState<Amounts>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [problem, setProblem] = useState<{ problem: ApiProblem; retryable: boolean; placed: string[] } | null>(null);

  const cap = paiseOfServer(available);
  const bills = open.data?.bills ?? [];
  const total = bills.reduce((sum, bill) => sum + (paiseOf(amounts[bill.id] ?? '') ?? 0n), 0n);
  const left = cap - total;

  const applying = useMutation({
    mutationFn: (lines: ApplyLine[]) =>
      send(`${props.identity} ${JSON.stringify(lines)}`, (key) => props.send(lines, key)),
    onMutate: () => setProblem(null),
    onSuccess: (data) => props.onDone(data),
    onError: (error, lines) => {
      if (isConflict(error)) {
        // The document or a bill changed meanwhile: both are read again, and the user decides once more.
        props.onConflict();
        setFormError(props.conflictMessage);
      }

      // The API names a line by its position in the request: its message goes on the bill of that line, and only there.
      const refused = problemOf(error);
      const atBills: Record<string, string> = {};
      const placed: string[] = [];
      const lineField = new RegExp(`^${linesField}\\[(\\d+)\\]\\.amount$`, 'i');
      for (const [field, messages] of Object.entries(refused.fieldErrors)) {
        const match = lineField.exec(field);
        const line = match === null ? undefined : lines[Number(match[1])];
        if (line !== undefined && messages[0] !== undefined) {
          atBills[line.billId] = messages[0];
          placed.push(field);
        }
      }

      setErrors(atBills);
      setProblem({ problem: refused, retryable: isRetryable(error), placed });
    },
  });

  const applyOldestFirst = () => {
    let remaining = cap;
    const filled: Amounts = {};
    for (const bill of bills) {
      if (remaining <= 0n) {
        break;
      }

      const due = paiseOfServer(bill.balanceDue);
      const amount = due < remaining ? due : remaining;
      filled[bill.id] = asRupees(amount);
      remaining -= amount;
    }

    setAmounts(filled);
    setErrors({});
    setFormError(null);
  };

  const submit = () => {
    const found: Record<string, string> = {};
    const lines: ApplyLine[] = [];
    for (const bill of bills) {
      const text = (amounts[bill.id] ?? '').trim();
      if (text === '') {
        continue;
      }

      const paise = paiseOf(text);
      if (paise === null) {
        found[bill.id] = 'Write the amount as rupees with at most 2 decimals.';
      } else if (paise > paiseOfServer(bill.balanceDue)) {
        found[bill.id] = `Not more than the bill's balance due, ₹${formatDecimal(bill.balanceDue)}.`;
      } else if (paise > 0n) {
        lines.push({ billId: bill.id, amount: asRupees(paise) });
      }
    }

    setErrors(found);
    const whole =
      lines.length === 0
        ? 'Enter an amount for at least one bill.'
        : lines.length > mostLines
          ? `One request names at most ${mostLines} bills.`
          : total > cap
            ? `The amounts come to ₹${formatDecimal(asRupees(total))}, more than the ₹${formatDecimal(available)} ${availableWord}.`
            : null;
    setFormError(whole);

    if (Object.keys(found).length === 0 && whole === null) {
      applying.mutate(lines);
    }
  };

  return (
    <>
      {problem !== null && <ProblemAlert problem={problem.problem} placedFields={problem.placed} />}
      {problem?.retryable === true && (
        <p className={forms.note}>
          Nothing is done twice: sending again with nothing changed finishes the same request.
        </p>
      )}
      {formError !== null && (
        <p className={fieldStyles.error} role="alert">
          {formError}
        </p>
      )}
      <p className={forms.note} aria-live="polite">
        {availableLabel}: ₹{formatDecimal(available)} · {words.applied}: ₹{formatDecimal(asRupees(total))} · Left:{' '}
        <strong>₹{formatDecimal(asRupees(left))}</strong>
      </p>

      {open.data === undefined ? (
        <LoadState error={open.error} what="the payer's bills" />
      ) : bills.length === 0 ? (
        <p className={table.empty}>This payer owes nothing on a posted bill.</p>
      ) : (
        <>
          {open.data.truncated && (
            <p className={forms.note}>The {mostOpenBills} oldest bills that owe something are shown.</p>
          )}
          <div className={forms.actions}>
            <button type="button" className={buttons.secondary} onClick={applyOldestFirst}>
              Apply oldest first
            </button>
          </div>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Bill</th>
                  <th scope="col">Bill date</th>
                  <th scope="col">Due date</th>
                  <th scope="col" className={table.number}>
                    Total (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Balance due (₹)
                  </th>
                  <th scope="col">Amount to apply (₹)</th>
                </tr>
              </thead>
              <tbody>
                {bills.map((bill) => (
                  <tr key={bill.id}>
                    <td>
                      <Link to={`/bills/${bill.id}`}>{bill.number}</Link>
                    </td>
                    <td>{formatDate(bill.billDate)}</td>
                    <td>{formatDate(bill.dueDate)}</td>
                    <td className={table.number}>{formatDecimal(bill.total)}</td>
                    <td className={table.number}>{formatDecimal(bill.balanceDue)}</td>
                    <td>
                      <input
                        className={fieldStyles.input}
                        inputMode="decimal"
                        aria-label={`Amount to apply to ${bill.number ?? 'the bill'}`}
                        aria-invalid={errors[bill.id] !== undefined}
                        value={amounts[bill.id] ?? ''}
                        onChange={(event) => setAmounts((current) => ({ ...current, [bill.id]: event.target.value }))}
                      />
                      {errors[bill.id] !== undefined && <p className={fieldStyles.error}>{errors[bill.id]}</p>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className={forms.actions}>
            <button
              type="button"
              className={buttons.primary}
              disabled={applying.isPending}
              aria-busy={applying.isPending}
              onClick={submit}
            >
              {applying.isPending ? words.submitting : words.submit}
            </button>
            <Link to={props.cancelTo}>Cancel</Link>
          </div>
        </>
      )}
    </>
  );
}
