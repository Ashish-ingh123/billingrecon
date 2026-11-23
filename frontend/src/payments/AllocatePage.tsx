import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import { isRetryable, useIdempotentRequest } from '../api/useIdempotentRequest';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatDecimal } from '../format';
import { asRupees, paiseOf, paiseOfServer } from '../money';
import { usePayer } from '../payers/api';
import {
  allocate,
  mostOpenBills,
  paymentKeys,
  paymentsChanged,
  usePayerOpenBills,
  usePaymentWithAllocations,
  type BillSummary,
  type PaymentWithAllocations,
} from './api';

/** The most bills one allocation names (the API's limit). */
const mostLines = 50;

/** The amount typed for each bill, by bill id. */
type Amounts = Record<string, string>;

/**
 * Applies a payment to the payer's bills (US-31, BR-303, ADR 0041 point 1): the bills that owe something, oldest first,
 * each with its balance due; "Apply oldest first" fills them from the unapplied cash. The form refuses an amount above a
 * bill's balance, a total above the unapplied cash and an empty allocation before sending; the server checks again.
 */
export function AllocatePage() {
  const { id = '' } = useParams();
  const payment = usePaymentWithAllocations(id);

  if (payment.data === undefined) {
    return (
      <section aria-labelledby="page-title">
        <PageHeader title="Allocate" back={{ to: `/payments/${id}`, label: 'Payment' }} />
        <LoadState error={payment.error} what="the payment" />
      </section>
    );
  }

  return <AllocateForm key={payment.data.payment.id} record={payment.data} />;
}

function AllocateForm({ record }: { record: PaymentWithAllocations }) {
  const { payment } = record;
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const send = useIdempotentRequest();
  const payer = usePayer(payment.payerId);
  const open = usePayerOpenBills(payment.payerId);
  const [amounts, setAmounts] = useState<Amounts>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [problem, setProblem] = useState<{ problem: ApiProblem; retryable: boolean; placed: string[] } | null>(null);

  const unapplied = paiseOfServer(payment.unappliedAmount);
  const bills = open.data?.bills ?? [];
  const typed = bills.map((bill) => paiseOf(amounts[bill.id] ?? '') ?? 0n);
  const total = typed.reduce((sum, amount) => sum + amount, 0n);
  const left = unapplied - total;

  const allocating = useMutation({
    mutationFn: (lines: { billId: string; amount: string }[]) =>
      send(`POST /payments/${payment.id}/allocations ${payment.version} ${JSON.stringify(lines)}`, (key) =>
        allocate(client, payment, lines, key),
      ),
    onMutate: () => setProblem(null),
    onSuccess: ({ data }) => {
      queryClient.setQueryData(paymentKeys.one(payment.id), data);
      paymentsChanged(queryClient);
      void navigate(`/payments/${payment.id}`);
    },
    onError: (error, lines) => {
      if (isConflict(error)) {
        // The payment or a bill changed meanwhile: read both again, and the user decides once more.
        paymentsChanged(queryClient);
        void queryClient.invalidateQueries({ queryKey: paymentKeys.one(payment.id) });
        setFormError(
          'Someone else changed this payment or one of these bills first. Nothing was allocated; the payment and the bills are shown as they are now. Check the amounts and allocate again.',
        );
      }

      // The API names a line by its position in the request: its message goes on the bill of that line, and only there.
      const refused = problemOf(error);
      const atBills: Record<string, string> = {};
      const placed: string[] = [];
      for (const [field, messages] of Object.entries(refused.fieldErrors)) {
        const match = /^allocations\[(\d+)\]\.amount$/i.exec(field);
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
    let remaining = unapplied;
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
    const lines: { billId: string; amount: string }[] = [];
    bills.forEach((bill: BillSummary) => {
      const text = (amounts[bill.id] ?? '').trim();
      if (text === '') {
        return;
      }

      const paise = paiseOf(text);
      if (paise === null) {
        found[bill.id] = 'Write the amount as rupees with at most 2 decimals.';
      } else if (paise > paiseOfServer(bill.balanceDue)) {
        found[bill.id] = `Not more than the bill's balance due, ₹${formatDecimal(bill.balanceDue)}.`;
      } else if (paise > 0n) {
        lines.push({ billId: bill.id, amount: asRupees(paise) });
      }
    });

    setErrors(found);
    const whole =
      lines.length === 0
        ? 'Enter an amount for at least one bill.'
        : lines.length > mostLines
          ? `One allocation names at most ${mostLines} bills.`
          : total > unapplied
            ? `The amounts come to ₹${formatDecimal(asRupees(total))}, more than the ₹${formatDecimal(payment.unappliedAmount)} unapplied.`
            : null;
    setFormError(whole);

    if (Object.keys(found).length === 0 && whole === null) {
      allocating.mutate(lines);
    }
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Allocate ${payment.number}`}
        back={{ to: `/payments/${payment.id}`, label: payment.number }}
        subtitle={payer.data === undefined ? undefined : `To the bills of ${payer.data.name}`}
      />
      {problem !== null && <ProblemAlert problem={problem.problem} placedFields={problem.placed} />}
      {problem?.retryable === true && (
        <p className={forms.note}>
          Nothing is done twice: allocating again with nothing changed finishes the same request.
        </p>
      )}
      {formError !== null && (
        <p className={fieldStyles.error} role="alert">
          {formError}
        </p>
      )}
      <p className={forms.note} aria-live="polite">
        Unapplied cash: ₹{formatDecimal(payment.unappliedAmount)} · Allocated here: ₹{formatDecimal(asRupees(total))} ·
        Left: <strong>₹{formatDecimal(asRupees(left))}</strong>
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
            <button type="button" className={buttons.primary} disabled={allocating.isPending} onClick={submit}>
              {allocating.isPending ? 'Allocating…' : 'Allocate'}
            </button>
            <Link to={`/payments/${payment.id}`}>Cancel</Link>
          </div>
        </>
      )}
    </section>
  );
}
