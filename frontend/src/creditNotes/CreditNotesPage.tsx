import { useId } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useBill } from '../bills/api';
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
  creditNoteStatuses,
  creditNoteStatusLabels,
  useCreditNotes,
  type CreditNoteListParams,
  type CreditNoteStatus,
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
 * The credit notes (US-24): drafts, those waiting for approval, posted and voided, by status, for one bill or one payer.
 * A credit note is drafted from the page of the posted bill it corrects (ADR 0042), so this list offers no new one.
 */
export function CreditNotesPage() {
  const [query, setQuery] = useSearchParams();
  const statusId = useId();
  const status = query.get('status') as CreditNoteStatus | null;
  const page = Number.parseInt(query.get('page') ?? '1', 10);
  const params: CreditNoteListParams = {
    search: query.get('search') ?? '',
    status: status !== null && creditNoteStatuses.includes(status) ? status : undefined,
    billId: query.get('billId') ?? '',
    payerId: query.get('payerId') ?? '',
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
  const creditNotes = useCreditNotes(params);

  const change = (next: Partial<CreditNoteListParams>, options?: { replace?: boolean }) => {
    const merged = { ...params, ...next, page: next.page ?? 1 };
    const search = new URLSearchParams();
    if (merged.search !== '') {
      search.set('search', merged.search);
    }

    if (merged.billId !== '') {
      search.set('billId', merged.billId);
    }

    if (merged.payerId !== '') {
      search.set('payerId', merged.payerId);
    }

    if (merged.status !== undefined) {
      search.set('status', merged.status);
    }

    if (merged.page > 1) {
      search.set('page', String(merged.page));
    }

    setQuery(search, options);
  };

  const showAll = (
    <button type="button" className={buttons.link} onClick={() => change({ billId: '', payerId: '' })}>
      Show every credit note
    </button>
  );

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Credit notes"
        subtitle={
          params.billId !== '' ? (
            <>
              The credit notes of bill <BillNumber id={params.billId} link={false} />. {showAll}
            </>
          ) : params.payerId !== '' ? (
            <>
              The credit notes of <PayerName id={params.payerId} />. {showAll}
            </>
          ) : (
            'Corrections of posted bills. A credit note is drafted from the page of the bill it corrects.'
          )
        }
      />
      <div className={listStyles.filters} role="search">
        <DocumentSearchField
          sent={params.search}
          onSend={(text) => change({ search: text }, { replace: true })}
          busy={creditNotes.isFetching}
        />
        <div className={listStyles.field}>
          <label htmlFor={statusId}>Status</label>
          <select
            id={statusId}
            className={listStyles.input}
            value={params.status ?? ''}
            onChange={(event) => change({ status: (event.target.value || undefined) as CreditNoteStatus | undefined })}
          >
            <option value="">All</option>
            {creditNoteStatuses.map((value) => (
              <option key={value} value={value}>
                {creditNoteStatusLabels[value].label}
              </option>
            ))}
          </select>
        </div>
      </div>
      {creditNotes.data === undefined ? (
        <LoadState error={creditNotes.error} what="credit notes" />
      ) : creditNotes.data.items.length === 0 ? (
        <EmptyState icon="receipt-minus">No credit note matches.</EmptyState>
      ) : (
        <>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Number</th>
                  <th scope="col">Date</th>
                  <th scope="col">Bill</th>
                  <th scope="col">Payer</th>
                  <th scope="col" className={table.number}>
                    Total (₹)
                  </th>
                  <th scope="col" className={table.number}>
                    Available (₹)
                  </th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {creditNotes.data.items.map((creditNote) => (
                  <tr key={creditNote.id}>
                    <td>
                      <Link to={`/credit-notes/${creditNote.id}`}>{creditNote.number ?? 'No number'}</Link>
                    </td>
                    <td>{formatDate(creditNote.creditNoteDate)}</td>
                    <td>
                      <BillNumber id={creditNote.billId} />
                    </td>
                    <td>
                      <PayerName id={creditNote.payerId} />
                    </td>
                    <td className={table.number}>{formatDecimal(creditNote.total)}</td>
                    <td className={table.number}>
                      {creditNote.status === 'Posted' ? formatDecimal(creditNote.availableAmount) : '—'}
                    </td>
                    <td>
                      <Badge
                        tone={creditNoteStatusLabels[creditNote.status].tone}
                        icon={creditNoteStatusLabels[creditNote.status].icon}
                      >
                        {creditNoteStatusLabels[creditNote.status].label}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(creditNotes.data.totalCount)}
            onChange={(next) => change({ page: next })}
          />
        </>
      )}
    </section>
  );
}
