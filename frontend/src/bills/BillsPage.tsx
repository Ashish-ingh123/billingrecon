import { useId, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useCan } from '../auth/permissions';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { EmptyState } from '../components/EmptyState';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatDecimal } from '../format';
import { usePatient } from '../patients/api';
import { usePayer } from '../payers/api';
import { billStatuses, billStatusLabels, useBills, type BillListParams, type BillStatus } from './api';
import { BillStatusBadge } from './BillStatusBadge';

/** The bill list's filters and page, kept in the address (ADR 0039 point 1). */
function useBillListParams(): BillListParams & { change: (next: Partial<BillListParams>) => void } {
  const [params, setParams] = useSearchParams();
  const status = params.get('status');
  const page = Number.parseInt(params.get('page') ?? '1', 10);
  const current: BillListParams = {
    status: billStatuses.includes(status as BillStatus) ? (status as BillStatus) : undefined,
    from: params.get('from') ?? '',
    to: params.get('to') ?? '',
    patientId: params.get('patientId') ?? '',
    payerId: params.get('payerId') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };

  const change = (next: Partial<BillListParams>) => {
    const merged = { ...current, ...next, page: next.page ?? 1 };
    const query = new URLSearchParams();
    for (const [name, value] of [
      ['patientId', merged.patientId],
      ['payerId', merged.payerId],
      ['status', merged.status ?? ''],
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

    setParams(query);
  };

  return { ...current, change };
}

function BillFilters({ params }: { params: ReturnType<typeof useBillListParams> }) {
  const [from, setFrom] = useState(params.from);
  const [to, setTo] = useState(params.to);
  const statusId = useId();
  const fromId = useId();
  const toId = useId();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    params.change({ from, to });
  };

  return (
    <form className={listStyles.filters} role="search" onSubmit={submit}>
      <div className={listStyles.field}>
        <label htmlFor={statusId}>Status</label>
        <select
          id={statusId}
          className={listStyles.input}
          value={params.status ?? ''}
          onChange={(event) => params.change({ status: (event.target.value || undefined) as BillStatus | undefined })}
        >
          <option value="">All</option>
          {billStatuses.map((status) => (
            <option key={status} value={status}>
              {billStatusLabels[status]}
            </option>
          ))}
        </select>
      </div>
      <div className={listStyles.field}>
        <label htmlFor={fromId}>Bill date from</label>
        <input
          id={fromId}
          className={listStyles.input}
          type="date"
          value={from}
          onChange={(event) => setFrom(event.target.value)}
        />
      </div>
      <div className={listStyles.field}>
        <label htmlFor={toId}>to</label>
        <input
          id={toId}
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

function PatientName({ id }: { id: string }) {
  const patient = usePatient(id);
  return <>{patient.data?.fullName ?? '…'}</>;
}

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

/** The bills (US-20, US-23): newest bill date first, by status and date, or those of one patient or payer. */
export function BillsPage() {
  const canDraft = useCan('WriteBills');
  const params = useBillListParams();
  const bills = useBills(params);
  const narrowed = params.patientId !== '' || params.payerId !== '';

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Bills"
        subtitle={
          narrowed ? (
            <>
              The bills of{' '}
              {params.patientId !== '' ? <PatientName id={params.patientId} /> : <PayerName id={params.payerId} />}.{' '}
              <button
                type="button"
                className={buttons.link}
                onClick={() => params.change({ patientId: '', payerId: '' })}
              >
                Show every bill
              </button>
            </>
          ) : (
            'Drafts, bills waiting for approval, and posted bills with what is still owed.'
          )
        }
        actions={
          canDraft && (
            <Link className={buttons.primary} to="/bills/new">
              New bill
            </Link>
          )
        }
      />
      <BillFilters key={`${params.from}|${params.to}`} params={params} />
      {bills.data === undefined ? (
        <LoadState error={bills.error} what="bills" />
      ) : bills.data.items.length === 0 ? (
        <EmptyState icon="receipt">No bill matches.</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Number</th>
                  <th scope="col">Bill date</th>
                  <th scope="col">Patient</th>
                  <th scope="col">Payer</th>
                  <th scope="col" className={table.number}>
                    Total (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Balance due (₹)
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {bills.data.items.map((bill) => (
                  <tr key={bill.id}>
                    <td>
                      <Link to={`/bills/${bill.id}`}>{bill.number ?? 'No number'}</Link>
                    </td>
                    <td>{formatDate(bill.billDate)}</td>
                    <td>
                      <PatientName id={bill.patientId} />
                    </td>
                    <td>
                      <PayerName id={bill.payerId} />
                    </td>
                    <td className={table.number}>{formatDecimal(bill.total)}</td>
                    <td className={table.number}>{formatDecimal(bill.balanceDue)}</td>
                    <td>
                      <BillStatusBadge status={bill.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(bills.data.totalCount)}
            onChange={(page) => params.change({ page })}
          />
        </>
      )}
    </section>
  );
}
