import { formatDate, formatInstant, formatMoney } from '../format';
import { paiseOfServer } from '../money';
import { differenceOfServer, type CashierShift } from '../shifts/api';
import styles from './Printing.module.css';
import type { PrintLayout } from './settings';
import { amountInWords } from './words';

/**
 * What the closing slip of a shift says between its head and its foot (Prompt 11.2b, ADR 0060): whose shift and of
 * which day, the float, the cash taken and paid out and what the drawer should hold, the count by denomination, what it
 * came to, and the difference with its reason. The figures are the stored ones; the settings choose the words and the
 * signature block, and nothing of the figures.
 */
export function ShiftSlipBody({ shift, layout }: { shift: CashierShift; layout: PrintLayout }) {
  const counted = (shift.count ?? []).filter((line) => Number(line.pieces) > 0);

  return (
    <>
      <dl className={styles.facts}>
        <div>
          <dt>Cashier</dt>
          <dd>{shift.cashierName ?? '—'}</dd>
        </div>
        <div>
          <dt>Business date</dt>
          <dd>{formatDate(shift.businessDate)}</dd>
        </div>
        <div>
          <dt>Opened</dt>
          <dd>{formatInstant(shift.openedAtUtc)}</dd>
        </div>
        {shift.countedAtUtc !== null && (
          <div>
            <dt>Counted</dt>
            <dd>{formatInstant(shift.countedAtUtc)}</dd>
          </div>
        )}
      </dl>

      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Cash of the shift</th>
              <th scope="col" className={styles.number}>
                Amount
              </th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Opening float</td>
              <td className={styles.number}>{formatMoney(shift.openingFloat)}</td>
            </tr>
            <tr>
              <td>Cash taken</td>
              <td className={styles.number}>{formatMoney(shift.cashReceived)}</td>
            </tr>
            <tr>
              <td>Cash paid out</td>
              <td className={styles.number}>{formatMoney(shift.cashPaidOut)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div className={styles.tableWrap}>
        <table className={styles.table}>
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
            {counted.map((line) => (
              <tr key={String(line.denomination)}>
                <td>₹{String(line.denomination)}</td>
                <td className={styles.number}>{String(line.pieces)}</td>
                <td className={styles.number}>{formatMoney(Number(line.denomination) * Number(line.pieces))}</td>
              </tr>
            ))}
            <tr>
              <td>Loose change</td>
              <td className={styles.number} />
              <td className={styles.number}>{formatMoney(shift.looseChange ?? 0)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <dl className={styles.totals}>
        <div>
          <dt>Expected cash</dt>
          <dd>{formatMoney(shift.expectedCash)}</dd>
        </div>
        <div className={styles.grand}>
          <dt>Counted cash</dt>
          <dd>{shift.countedCash === null ? '—' : formatMoney(shift.countedCash)}</dd>
        </div>
        <div>
          <dt>Difference</dt>
          <dd>{shift.difference === null ? '—' : differenceOfServer(shift.difference)}</dd>
        </div>
      </dl>
      {layout.showAmountInWords && shift.countedCash !== null && (
        <p className={styles.words}>
          <strong>Counted, in words:</strong> {amountInWords(paiseOfServer(shift.countedCash))}
        </p>
      )}
      {shift.differenceReason !== null && (
        <p className={styles.small}>
          <strong>Reason for the difference:</strong> {shift.differenceReason}
        </p>
      )}
      <p className={styles.small}>
        The expected cash is the float and the cash entries of this shift in the ledger. A difference is recorded on the
        shift and is not posted.
      </p>
    </>
  );
}
