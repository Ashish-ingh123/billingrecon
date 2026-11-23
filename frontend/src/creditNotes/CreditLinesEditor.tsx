import { useId, useState } from 'react';
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form';
import type { Bill } from '../bills/api';
import styles from '../bills/Bills.module.css';
import { billTotals, lineAmounts, rupees, type LineAmounts } from '../bills/lineMath';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import { checkDecimal } from '../decimals';
import { formatDecimal } from '../format';
import { paiseOfServer } from '../money';
import { emptyLine, lineFromBill, type CreditNoteValues } from './api';

const mostLines = 200;

const positiveQuantity = (value: string): true | string => {
  const shape = checkDecimal(value, 3, 'quantity');
  if (shape !== true) {
    return shape;
  }

  return Number(value) > 0 || 'The quantity is more than 0.';
};

const taxRate = (value: string): true | string => {
  const shape = checkDecimal(value, 2, 'tax rate');
  return shape !== true ? shape : Number(value) <= 100 || 'The tax rate is at most 100.';
};

/**
 * The lines of a draft credit note (US-24, ADR 0042 point 4): lines of the bill, each crediting a quantity of it at the
 * bill's price and tax rate, and free-text lines. A line of the bill is not credited above what was billed, in quantity or
 * in amount; the server checks again against the credit notes already posted (BR-210, BR-211). Each line shows its total
 * and the draft its totals, by the server's arithmetic, as an estimate until saved.
 */
export function CreditLinesEditor({ form, bill }: { form: UseFormReturn<CreditNoteValues>; bill: Bill }) {
  const {
    register,
    control,
    getValues,
    formState: { errors },
  } = form;
  const pickId = useId();
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });
  const lines = useWatch({ control, name: 'lines' });
  const [picked, setPicked] = useState('');

  const amounts = lines.map((line) => lineAmounts(line));
  const complete = amounts.filter((line): line is LineAmounts => line !== null);
  const totals = billTotals(complete);
  const named = new Set(lines.map((line) => line.billLineId));
  const left = bill.lines.filter((line) => !named.has(line.id));
  const billLine = (id: string | null) => bill.lines.find((line) => line.id === id);

  const quantity = (index: number) => (value: string) => {
    const shape = positiveQuantity(value);
    if (shape !== true) {
      return shape;
    }

    const of = billLine(getValues(`lines.${index}.billLineId`));
    return (
      of === undefined || Number(value) <= Number(of.quantity) || `Not more than the ${String(of.quantity)} billed.`
    );
  };

  const discount = (index: number) => (value: string) => {
    if (value.trim() === '') {
      return true;
    }

    const shape = checkDecimal(value, 2, 'discount');
    if (shape !== true) {
      return shape;
    }

    const line = lineAmounts({ ...getValues(`lines.${index}`), discountAmount: '0' });
    return (
      line === null ||
      Number(value) <= Number(rupees(line.lineAmount)) ||
      'The discount is not more than the line amount.'
    );
  };

  // A line of the bill credits at most what that line came to (BR-211), checked here for this credit note alone.
  const withinBillLine = (index: number) => () => {
    const of = billLine(getValues(`lines.${index}.billLineId`));
    const line = lineAmounts(getValues(`lines.${index}`));
    return (
      of === undefined ||
      line === null ||
      line.lineTotal <= paiseOfServer(of.lineTotal) ||
      `The line comes to more than the bill's line, ₹${formatDecimal(of.lineTotal)}.`
    );
  };

  return (
    <section className={forms.section} aria-labelledby="lines-title">
      <h2 id="lines-title">Lines</h2>
      {fields.length === 0 ? (
        <p className={styles.lineSource}>No lines yet. A draft may have none; it needs one to be submitted.</p>
      ) : (
        <ol className={styles.lines} aria-label="Lines of the credit note">
          {fields.map((field, index) => {
            const line = amounts[index];
            const lineErrors = errors.lines?.[index];
            const of = billLine(lines[index]?.billLineId ?? null);
            return (
              <li key={field.id} className={styles.lineCard}>
                {of !== undefined ? (
                  <>
                    <div>
                      <p className={fieldStyles.label}>{`Line ${index + 1}: ${of.description}`}</p>
                      <p className={styles.lineSource}>
                        From the bill: {String(of.quantity)} × ₹{formatDecimal(of.unitPrice, 4)}, tax{' '}
                        {String(of.taxRatePercent)}%, line total ₹{formatDecimal(of.lineTotal)}
                      </p>
                      {lineErrors?.billLineId?.message !== undefined && (
                        <p className={fieldStyles.error}>{lineErrors.billLineId.message}</p>
                      )}
                    </div>
                    <TextField
                      id={`line-${index}-quantity`}
                      label="Quantity to credit"
                      inputMode="decimal"
                      error={lineErrors?.quantity?.message}
                      {...register(`lines.${index}.quantity`, {
                        validate: { shape: quantity(index), cap: withinBillLine(index) },
                      })}
                    />
                    <TextField
                      id={`line-${index}-discount`}
                      label="Discount (₹)"
                      inputMode="decimal"
                      error={lineErrors?.discountAmount?.message}
                      {...register(`lines.${index}.discountAmount`, { validate: discount(index) })}
                    />
                    <span aria-hidden="true" />
                    <span aria-hidden="true" />
                  </>
                ) : (
                  <>
                    <div>
                      <TextField
                        id={`line-${index}-description`}
                        label={`Line ${index + 1}: description`}
                        hint="Free text"
                        error={lineErrors?.description?.message}
                        {...register(`lines.${index}.description`, {
                          validate: (value) =>
                            value.trim() === ''
                              ? 'Enter the description.'
                              : value.length <= 300 || 'The description is at most 300 characters.',
                        })}
                      />
                    </div>
                    <TextField
                      id={`line-${index}-quantity`}
                      label="Quantity"
                      inputMode="decimal"
                      error={lineErrors?.quantity?.message}
                      {...register(`lines.${index}.quantity`, { validate: positiveQuantity })}
                    />
                    <TextField
                      id={`line-${index}-unit-price`}
                      label="Unit price (₹)"
                      inputMode="decimal"
                      error={lineErrors?.unitPrice?.message}
                      {...register(`lines.${index}.unitPrice`, {
                        validate: (value) => checkDecimal(value, 4, 'unit price'),
                      })}
                    />
                    <TextField
                      id={`line-${index}-discount`}
                      label="Discount (₹)"
                      inputMode="decimal"
                      error={lineErrors?.discountAmount?.message}
                      {...register(`lines.${index}.discountAmount`, { validate: discount(index) })}
                    />
                    <TextField
                      id={`line-${index}-tax-rate`}
                      label="Tax rate (%)"
                      inputMode="decimal"
                      error={lineErrors?.taxRatePercent?.message}
                      {...register(`lines.${index}.taxRatePercent`, { validate: taxRate })}
                    />
                  </>
                )}
                <p className={styles.lineTotal} aria-label={`Line ${index + 1} total`}>
                  {line === null || line === undefined ? '—' : `₹${formatDecimal(rupees(line.lineTotal))}`}
                </p>
                <button type="button" className={`${buttons.link} ${styles.remove}`} onClick={() => remove(index)}>
                  Remove
                </button>
              </li>
            );
          })}
        </ol>
      )}

      {fields.length < mostLines && (
        <div className={styles.addLines}>
          <div className={fieldStyles.field}>
            <label className={fieldStyles.label} htmlFor={pickId}>
              A line of the bill
            </label>
            <div className={forms.actions}>
              <select
                id={pickId}
                className={fieldStyles.input}
                value={picked}
                onChange={(event) => setPicked(event.target.value)}
                disabled={left.length === 0}
              >
                <option value="">{left.length === 0 ? 'Every line of the bill is here' : 'Choose…'}</option>
                {left.map((line) => (
                  <option key={line.id} value={line.id}>
                    {`${String(line.lineNo)}. ${line.description} (₹${formatDecimal(line.lineTotal)})`}
                  </option>
                ))}
              </select>
              <button
                type="button"
                className={buttons.secondary}
                disabled={picked === ''}
                onClick={() => {
                  const chosen = billLine(picked);
                  if (chosen !== undefined) {
                    append(lineFromBill(chosen));
                    setPicked('');
                  }
                }}
              >
                Credit this line
              </button>
            </div>
          </div>
          <button type="button" className={buttons.secondary} onClick={() => append(emptyLine)}>
            Add a free-text line
          </button>
        </div>
      )}

      <dl className={styles.totals} aria-label="Estimated totals">
        <div>
          <dt>Subtotal</dt>
          <dd>₹{formatDecimal(rupees(totals.subTotal))}</dd>
        </div>
        <div>
          <dt>Discounts</dt>
          <dd>₹{formatDecimal(rupees(totals.discountTotal))}</dd>
        </div>
        <div>
          <dt>Tax</dt>
          <dd>₹{formatDecimal(rupees(totals.taxTotal))}</dd>
        </div>
        <div className={styles.grand}>
          <dt>Total</dt>
          <dd>₹{formatDecimal(rupees(totals.total))}</dd>
        </div>
      </dl>
      <p className={styles.estimate}>
        An estimate until the draft is saved; the credit note then shows the server&apos;s figures. The bill came to ₹
        {formatDecimal(bill.total)}.
        {complete.length < amounts.length && ' Lines whose numbers are not complete are left out.'}
      </p>
    </section>
  );
}
