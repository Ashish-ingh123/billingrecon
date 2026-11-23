import { useId, useState, type FormEvent } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, todayInIndia } from '../format';
import { readDailyCollection, useReport } from './api';
import { collectionColumns, collectionTotals, collectionIsWhole } from './columns';
import { reportCsv } from './csv';
import { ExportChangedError } from './exportReport';
import { ExportButton, ReportTable } from './ReportParts';
import { useReportQuery } from './view';
import styles from './Reports.module.css';

/**
 * The daily collection (US-42, BR-901): the money of one business date in India by method, received less reversed and
 * refunded; the totals' net is the movement of the Cash account that day.
 */
export function DailyCollectionPage() {
  const client = useApiClient();
  const dateId = useId();
  const { values, change } = useReportQuery(['date'] as const);
  const [date, setDate] = useState(values.date === '' ? todayInIndia() : values.date);
  const report = useReport('daily-collection', values.date, (api) => readDailyCollection(api, values.date));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    change({ date });
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Daily collection"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The money of one day by method: received, less what was reversed and refunded that day. A payment dated that day counts on it, whenever it was recorded."
      />
      <div className={styles.toolbar}>
        <form className={listStyles.filters} aria-label="Day" onSubmit={submit}>
          <div className={listStyles.field}>
            <label htmlFor={dateId}>Date</label>
            <input
              id={dateId}
              className={listStyles.input}
              type="date"
              value={date}
              onChange={(event) => setDate(event.target.value)}
            />
          </div>
          <button type="submit" className={buttons.secondary}>
            Show
          </button>
          {report.data !== undefined && <p className={styles.period}>{formatDate(report.data.date)}</p>}
        </form>
        <ExportButton
          fileName={`daily-collection-${report.data?.date ?? values.date}.csv`}
          build={async () => {
            const collection = await readDailyCollection(client, values.date);
            if (!collectionIsWhole(collection)) {
              throw new ExportChangedError();
            }

            return {
              rows: collection.methods.length,
              text: reportCsv({
                title: `Daily collection of ${collection.date}`,
                period: { from: collection.date, to: collection.date },
                columns: collectionColumns,
                rows: collection.methods,
                totals: collectionTotals(collection),
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the collection" />
      ) : (
        <ReportTable
          label="Collection by method"
          columns={collectionColumns}
          rows={report.data.methods}
          keyOf={(row) => row.method}
          count="Total"
          totals={collectionTotals(report.data)}
        />
      )}
    </section>
  );
}
