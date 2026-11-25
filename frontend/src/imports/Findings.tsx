import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { saveCsv } from '../reports/csv';
import type { ImportFinding } from './api';
import { findingsCsv, placeOf } from './findingsCsv';
import styles from './Imports.module.css';

/**
 * Every row the files cannot bring in (Prompt 10.3b, ADR 0053): its file, its line and the reason in the API's own
 * words, in a table that scrolls inside its frame, with the same rows as a file to work from.
 */
export function Findings({ findings, fileName }: { findings: readonly ImportFinding[]; fileName: string }) {
  return (
    <section className={styles.findings} aria-labelledby="findings-title">
      <div className={styles.findingsHead}>
        <h2 id="findings-title">
          {findings.length === 1
            ? '1 row cannot be brought in'
            : `${String(findings.length)} rows cannot be brought in`}
        </h2>
        <button type="button" className={buttons.secondary} onClick={() => saveCsv(fileName, findingsCsv(findings))}>
          Download the findings
        </button>
      </div>
      <p className={styles.note}>
        Correct the files, not the records here, and check them again. Nothing is written while one row is rejected.
      </p>
      <div className={table.wrap}>
        <table className={table.table}>
          <thead>
            <tr>
              <th scope="col">File</th>
              <th scope="col">Where</th>
              <th scope="col">Why</th>
            </tr>
          </thead>
          <tbody>
            {findings.map((finding) => (
              <tr key={`${finding.file}:${String(finding.line)}:${finding.reason}`}>
                <td>{finding.file}</td>
                <td>{placeOf(finding.line)}</td>
                <td>{finding.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
