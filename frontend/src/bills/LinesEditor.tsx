import { useState } from 'react';
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form';
import buttons from '../components/buttons.module.css';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { checkDecimal } from '../decimals';
import { formatDecimal } from '../format';
import type { ServiceItem } from '../serviceItems/api';
import type { BillLineValues, BillValues } from './api';
import styles from './Bills.module.css';
import { billTotals, lineAmounts, rupees, type LineAmounts } from './lineMath';
import { ServiceItemPicker } from './pickers';

const mostLines = 200;

const emptyLine: BillLineValues = {
  serviceItemId: null,
  itemCode: '',
  description: '',
  quantity: '1',
  unitPrice: '',
  discountAmount: '',
  taxRatePercent: '0',
};

function lineFromItem(item: ServiceItem): BillLineValues {
  return {
    serviceItemId: item.id,
    itemCode: item.code,
    description: item.name,
    quantity: '1',
    unitPrice: String(item.defaultUnitPrice),
    discountAmount: '',
    taxRatePercent: String(item.taxRatePercent),
  };
}

const quantity = (value: string): true | string => {
  const shape = checkDecimal(value, 3, 'quantity');
  if (shape !== true) {
    return shape;
  }

  const number = Number(value);
  return number <= 0
    ? 'The quantity is more than 0.'
    : number > 999999.999
      ? 'The quantity is at most 999999.999.'
      : true;
};

const taxRate = (value: string): true | string => {
  const shape = checkDecimal(value, 2, 'tax rate');
  return shape !== true ? shape : Number(value) <= 100 || 'The tax rate is at most 100.';
};

/**
 * The lines of a draft (US-20, BR-201): from the catalogue, which fills the description, the price and the tax rate,
 * all of which stay editable, or in free text. Each line shows its total, and the draft its totals, by the server's
 * arithmetic (BR-202, BR-203, ADR 0040 point 3), as an estimate until saved.
 */
export function LinesEditor({ form }: { form: UseFormReturn<BillValues> }) {
  const {
    register,
    control,
    getValues,
    formState: { errors },
  } = form;
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' });
  const lines = useWatch({ control, name: 'lines' });
  const [picked, setPicked] = useState<ServiceItem | null>(null);

  const amounts = lines.map((line) => lineAmounts(line));
  const complete = amounts.filter((line): line is LineAmounts => line !== null);
  const totals = billTotals(complete);

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

  return (
    <section className={forms.section} aria-labelledby="lines-title">
      <h2 id="lines-title">Lines</h2>
      {fields.length === 0 ? (
        <p className={styles.lineSource}>No lines yet. A draft may have none; it needs one to be posted.</p>
      ) : (
        <ol className={styles.lines} aria-label="Lines of the bill">
          {fields.map((field, index) => {
            const line = amounts[index];
            const lineErrors = errors.lines?.[index];
            const source =
              lines[index]?.serviceItemId === null ? 'Free text' : `Catalogue ${lines[index]?.itemCode ?? ''}`;
            return (
              <li key={field.id} className={styles.lineCard}>
                <div>
                  <TextField
                    id={`line-${index}-description`}
                    label={`Line ${index + 1}: description`}
                    hint={source.trim()}
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
                  {...register(`lines.${index}.quantity`, { validate: quantity })}
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
          <ServiceItemPicker
            selected={picked}
            onSelect={(item) => {
              if (item !== null) {
                append(lineFromItem(item));
                setPicked(null);
              }
            }}
          />
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
        An estimate until the draft is saved; the bill then shows the server&apos;s figures.
        {complete.length < amounts.length && ' Lines whose numbers are not complete are left out.'}
      </p>
    </section>
  );
}
