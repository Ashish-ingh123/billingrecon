import { useId } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useBill } from '../bills/api';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal } from '../format';
import { usePayer } from '../payers/api';
import {
  adjustmentKinds,
  adjustmentStatuses,
  adjustmentStatusLabels,
  kindLabels,
  useAdjustments,
  type AdjustmentKind,
  type AdjustmentListParams,
  type AdjustmentStatus,
} from './api';

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

function BillNumber({ id, link = true }: { id: string; link?: boolean }) {
  const bill = useBill(id);
  const number = bill.data?.number ?? '…';
  return link ? <Link to={`/bills/${id}`}>{number}</Link> : <>{number}</>;
}

/**
 * The adjustments (BR-501 to BR-503): discounts, write-offs and roundings, by status and kind, for one bill or one payer.
 * An adjustment is drafted from the page of the posted bill it lowers (ADR 0042), so this list offers no new one.
 */
export function AdjustmentsPage() {
  const [query, setQuery] = useSearchParams();
  const statusId = useId();
  const kindId = useId();
  const status = query.get('status') as AdjustmentStatus | null;
  const kind = query.get('kind') as AdjustmentKind | null;
  const page = Number.parseInt(query.get('page') ?? '1', 10);
  const params: AdjustmentListParams = {
    status: status !== null && adjustmentStatuses.includes(status) ? status : undefined,
    kind: kind !== null && adjustmentKinds.includes(kind) ? kind : undefined,
    billId: query.get('billId') ?? '',
    payerId: query.get('payerId') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
  const adjustments = useAdjustments(params);

  const change = (next: Partial<AdjustmentListParams>) => {
    const merged = { ...params, ...next, page: next.page ?? 1 };
    const search = new URLSearchParams();
    for (const [name, value] of [
      ['billId', merged.billId],
      ['payerId', merged.payerId],
      ['status', merged.status ?? ''],
      ['kind', merged.kind ?? ''],
    ] as const) {
      if (value !== '') {
        search.set(name, value);
      }
    }

    if (merged.page > 1) {
      search.set('page', String(merged.page));
    }

    setQuery(search);
  };

  const showAll = (
    <button type="button" className={buttons.link} onClick={() => change({ billId: '', payerId: '' })}>
      Show every adjustment
    </button>
  );

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Adjustments"
        subtitle={
          params.billId !== '' ? (
            <>
              The adjustments of bill <BillNumber id={params.billId} link={false} />. {showAll}
            </>
          ) : params.payerId !== '' ? (
            <>
              The adjustments of <PayerName id={params.payerId} />. {showAll}
            </>
          ) : (
            'Discounts, write-offs and roundings of posted bills. An adjustment is drafted from the page of the bill it lowers.'
          )
        }
      />
      <div className={listStyles.filters}>
        <div className={listStyles.field}>
          <label htmlFor={statusId}>Status</label>
          <select
            id={statusId}
            className={listStyles.input}
            value={params.status ?? ''}
            onChange={(event) => change({ status: (event.target.value || undefined) as AdjustmentStatus | undefined })}
          >
            <option value="">All</option>
            {adjustmentStatuses.map((value) => (
              <option key={value} value={value}>
                {adjustmentStatusLabels[value].label}
              </option>
            ))}
          </select>
        </div>
        <div className={listStyles.field}>
          <label htmlFor={kindId}>Kind</label>
          <select
            id={kindId}
            className={listStyles.input}
            value={params.kind ?? ''}
            onChange={(event) => change({ kind: (event.target.value || undefined) as AdjustmentKind | undefined })}
          >
            <option value="">All</option>
            {adjustmentKinds.map((value) => (
              <option key={value} value={value}>
                {kindLabels[value]}
              </option>
            ))}
          </select>
        </div>
      </div>
      {adjustments.data === undefined ? (
        <LoadState error={adjustments.error} what="adjustments" />
      ) : adjustments.data.items.length === 0 ? (
        <p className={table.empty}>No adjustment matches.</p>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Number</th>
                  <th scope="col">Date</th>
                  <th scope="col">Kind</th>
                  <th scope="col">Bill</th>
                  <th scope="col">Payer</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {adjustments.data.items.map((adjustment) => (
                  <tr key={adjustment.id}>
                    <td>
                      <Link to={`/adjustments/${adjustment.id}`}>{adjustment.number ?? 'No number'}</Link>
                    </td>
                    <td>{formatDate(adjustment.adjustmentDate)}</td>
                    <td>{kindLabels[adjustment.kind]}</td>
                    <td>
                      <BillNumber id={adjustment.billId} />
                    </td>
                    <td>
                      <PayerName id={adjustment.payerId} />
                    </td>
                    <td className={table.number}>{formatDecimal(adjustment.amount)}</td>
                    <td>
                      <Badge tone={adjustmentStatusLabels[adjustment.status].tone}>
                        {adjustmentStatusLabels[adjustment.status].label}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(adjustments.data.totalCount)}
            onChange={(next) => change({ page: next })}
          />
        </>
      )}
    </section>
  );
}
