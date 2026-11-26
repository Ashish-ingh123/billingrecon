import { useId, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useCan } from '../auth/permissions';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { DocumentSearchField } from '../components/DocumentSearchField';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal } from '../format';
import { usePayer } from '../payers/api';
import {
  methodLabels,
  paymentMethods,
  paymentStatuses,
  paymentStatusLabels,
  usePayments,
  type PaymentListParams,
  type PaymentMethod,
  type PaymentStatus,
} from './api';

function usePaymentListParams(): PaymentListParams & {
  change: (next: Partial<PaymentListParams>, options?: { replace?: boolean }) => void;
} {
  const [params, setParams] = useSearchParams();
  const status = params.get('status') as PaymentStatus | null;
  const method = params.get('method') as PaymentMethod | null;
  const page = Number.parseInt(params.get('page') ?? '1', 10);
  const current: PaymentListParams = {
    search: params.get('search') ?? '',
    status: status !== null && paymentStatuses.includes(status) ? status : undefined,
    method: method !== null && paymentMethods.includes(method) ? method : undefined,
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    payerId: params.get('payerId') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };

  const change = (next: Partial<PaymentListParams>, options?: { replace?: boolean }) => {
    const merged = { ...current, ...next, page: next.page ?? 1 };
    const query = new URLSearchParams();
    for (const [name, value] of [
      ['search', merged.search],
      ['payerId', merged.payerId],
      ['status', merged.status ?? ''],
      ['method', merged.method ?? ''],
      ['from', merged.from],
      ['to', merged.to],
    ] as const) {
      if (value !== '') {
        query.set(name, value);
      }
    }

    if (merged.page > 1) {
      query.set('page', String(merged.page));
    }

    setParams(query, options);
  };

  return { ...current, change };
}

function PaymentFilters({ params, busy }: { params: ReturnType<typeof usePaymentListParams>; busy: boolean }) {
  const [from, setFrom] = useState(params.from);
  const [to, setTo] = useState(params.to);
  const ids = { status: useId(), method: useId(), from: useId(), to: useId() };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    params.change({ from, to });
  };

  return (
    <form className={listStyles.filters} role="search" onSubmit={submit}>
      <DocumentSearchField
        sent={params.search}
        onSend={(text) => params.change({ search: text }, { replace: true })}
        busy={busy}
      />
      <div className={listStyles.field}>
        <label htmlFor={ids.status}>Status</label>
        <select
          id={ids.status}
          className={listStyles.input}
          value={params.status ?? ''}
          onChange={(event) =>
            params.change({ status: (event.target.value || undefined) as PaymentStatus | undefined })
          }
        >
          <option value="">All</option>
          {paymentStatuses.map((status) => (
            <option key={status} value={status}>
              {paymentStatusLabels[status].label}
            </option>
          ))}
        </select>
      </div>
      <div className={listStyles.field}>
        <label htmlFor={ids.method}>Method</label>
        <select
          id={ids.method}
          className={listStyles.input}
          value={params.method ?? ''}
          onChange={(event) =>
            params.change({ method: (event.target.value || undefined) as PaymentMethod | undefined })
          }
        >
          <option value="">All</option>
          {paymentMethods.map((method) => (
            <option key={method} value={method}>
              {methodLabels[method]}
            </option>
          ))}
        </select>
      </div>
      <div className={listStyles.field}>
        <label htmlFor={ids.from}>Payment date from</label>
        <input
          id={ids.from}
          className={listStyles.input}
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
        />
      </div>
      <div className={listStyles.field}>
        <label htmlFor={ids.to}>to</label>
        <input
          id={ids.to}
          className={listStyles.input}
          type="date"
          value={to}
          onChange={(event) => setTo(event.target.value)}
        />
      </div>
      <button type="submit" className={buttons.secondary}>
        Show
      </button>
    </form>
  );
}

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

/** The payments received (US-30): newest first, by status, method and date, or those of one payer. */
export function PaymentsPage() {
  const canRecord = useCan('RecordPayments');
  const params = usePaymentListParams();
  const payments = usePayments(params);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Payments"
        subtitle={
          params.payerId !== '' ? (
            <>
              The payments of <PayerName id={params.payerId} />.{' '}
              <button type="button" className={buttons.link} onClick={() => params.change({ payerId: '' })}>
                Show every payment
              </button>
            </>
          ) : (
            'Money received, what is left of it to apply, and payments waiting for or past their reversal.'
          )
        }
        actions={
          canRecord && (
            <Link
              className={buttons.primary}
              to={params.payerId === '' ? '/payments/new' : `/payments/new?payerId=${params.payerId}`}
            >
              Record payment
            </Link>
          )
        }
      />
      <PaymentFilters key={`${params.from}|${params.to}`} params={params} busy={payments.isFetching} />
      {payments.data === undefined ? (
        <LoadState error={payments.error} what="payments" />
      ) : payments.data.items.length === 0 ? (
        <EmptyState icon="wallet">No payment matches.</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Number</th>
                  <th scope="col">Date</th>
                  <th scope="col">Payer</th>
                  <th scope="col">Method</th>
                  <th scope="col">Reference</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Unapplied (₹)
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {payments.data.items.map((payment) => (
                  <tr key={payment.id}>
                    <td>
                      <Link to={`/payments/${payment.id}`}>{payment.number}</Link>
                    </td>
                    <td>{formatDate(payment.paymentDate)}</td>
                    <td>
                      <PayerName id={payment.payerId} />
                    </td>
                    <td>{methodLabels[payment.method]}</td>
                    <td>{payment.reference}</td>
                    <td className={table.number}>{formatDecimal(payment.amount)}</td>
                    <td className={table.number}>{formatDecimal(payment.unappliedAmount)}</td>
                    <td>
                      <Badge
                        tone={paymentStatusLabels[payment.status].tone}
                        icon={paymentStatusLabels[payment.status].icon}
                      >
                        {paymentStatusLabels[payment.status].label}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(payments.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
