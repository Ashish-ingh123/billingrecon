import { useId } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal } from '../format';
import { methodLabels } from '../payments/api';
import { usePayer } from '../payers/api';
import { refundStatuses, refundStatusLabels, useRefunds, type RefundListParams, type RefundStatus } from './api';

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

/**
 * The refunds (US-34): drafts, those waiting for approval, posted and voided, by status or for one payer. A refund is
 * drafted from its source, a payment or a credit note (ADR 0041 point 2), so this list offers no new refund.
 */
export function RefundsPage() {
  const [query, setQuery] = useSearchParams();
  const statusId = useId();
  const status = query.get('status') as RefundStatus | null;
  const page = Number.parseInt(query.get('page') ?? '1', 10);
  const params: RefundListParams = {
    status: status !== null && refundStatuses.includes(status) ? status : undefined,
    payerId: query.get('payerId') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
  const refunds = useRefunds(params);

  const change = (next: Partial<RefundListParams>) => {
    const merged = { ...params, ...next, page: next.page ?? 1 };
    const search = new URLSearchParams();
    if (merged.payerId !== '') {
      search.set('payerId', merged.payerId);
    }

    if (merged.status !== undefined) {
      search.set('status', merged.status);
    }

    if (merged.page > 1) {
      search.set('page', String(merged.page));
    }

    setQuery(search);
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Refunds"
        subtitle={
          params.payerId !== '' ? (
            <>
              The refunds of <PayerName id={params.payerId} />.{' '}
              <button type="button" className={buttons.link} onClick={() => change({ payerId: '' })}>
                Show every refund
              </button>
            </>
          ) : (
            "Money paid back from a payment's unapplied cash or a credit note's available credit. A refund is drafted from its payment, or from the payer's statement."
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
            onChange={(event) => change({ status: (event.target.value || undefined) as RefundStatus | undefined })}
          >
            <option value="">All</option>
            {refundStatuses.map((value) => (
              <option key={value} value={value}>
                {refundStatusLabels[value].label}
              </option>
            ))}
          </select>
        </div>
      </div>
      {refunds.data === undefined ? (
        <LoadState error={refunds.error} what="refunds" />
      ) : refunds.data.items.length === 0 ? (
        <EmptyState icon="undo">No refund matches.</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Number</th>
                  <th scope="col">Refund date</th>
                  <th scope="col">Payer</th>
                  <th scope="col">From</th>
                  <th scope="col">Method</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {refunds.data.items.map((refund) => (
                  <tr key={refund.id}>
                    <td>
                      <Link to={`/refunds/${refund.id}`}>{refund.number ?? 'No number'}</Link>
                    </td>
                    <td>{formatDate(refund.refundDate)}</td>
                    <td>
                      <PayerName id={refund.payerId} />
                    </td>
                    <td>{refund.sourceKind === 'Payment' ? 'A payment' : 'A credit note'}</td>
                    <td>{methodLabels[refund.method]}</td>
                    <td className={table.number}>{formatDecimal(refund.amount)}</td>
                    <td>
                      <Badge
                        tone={refundStatusLabels[refund.status].tone}
                        icon={refundStatusLabels[refund.status].icon}
                      >
                        {refundStatusLabels[refund.status].label}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(refunds.data.totalCount)}
            onChange={(next) => change({ page: next })}
          />
        </>
      )}
    </section>
  );
}
