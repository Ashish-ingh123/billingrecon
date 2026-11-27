import {
  columnFilteringFeature,
  columnOrderingFeature,
  columnVisibilityFeature,
  createColumnHelper,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
  useTable,
  type ColumnOrderState,
  type ColumnVisibilityState,
  type RowData,
  type SortingState,
} from '@tanstack/react-table';
import { useState, type ReactNode } from 'react';
import { useSession } from '../../auth/AuthContext';
import { Icon } from '../../brand/Icon';
import table from '../DataTable.module.css';
import { ColumnsMenu } from './ColumnsMenu';
import { orderOf, prefsKey, readPrefs, writePrefs, type ColumnPrefs } from './columnPrefs';
import styles from './Table.module.css';

/** A column of a table: what its header says, what a cell shows, and how it may be sorted and hidden. */
export interface TableColumn<T> {
  /** Unique in the table; kept with the user's choice of columns. */
  id: string;
  header: string;
  cell: (row: T) => ReactNode;
  /** Right-aligned in tabular figures: an amount or a count. */
  numeric?: boolean;
  /** The API's name of this column's order (`total`); none when it cannot be sorted. */
  sort?: string;
  /** Never hidden: the record's number or name, and its status (ADR 0063). */
  fixed?: boolean;
}

/**
 * Only what the table holds of its own: the columns shown and their order, and which column the list is sorted by.
 * The rows are filtered, sorted and paged by the API (`manual*` on every table); the browser never reorders or drops
 * a row of the page it holds.
 */
const features = tableFeatures({
  columnVisibilityFeature,
  columnOrderingFeature,
  rowSortingFeature,
  columnFilteringFeature,
  rowPaginationFeature,
});

type Direction = 'ascending' | 'descending';

/** The column and direction an API order names: `-total` is the total, descending. */
function parseSort(sort: string): { name: string; desc: boolean } | null {
  if (sort === '') {
    return null;
  }

  return sort.startsWith('-') ? { name: sort.slice(1), desc: true } : { name: sort, desc: false };
}

/**
 * The tables of the lists (Prompt 12.2; ADR 0063, amending ADR 0048): TanStack Table, headless, inside our own markup,
 * tokens and CSS; a header that sorts is a button with `aria-sort`, one column at a time, its direction shown by an icon
 * and said; a "Columns" menu shows, hides and moves the columns, kept in this browser for this user and table. The first
 * column and the status are never hidden. On a narrow window the table scrolls inside its frame, its first column in
 * view, as before.
 */
export function DataTable<T extends RowData>({
  tableId,
  label,
  columns,
  rows,
  keyOf,
  sort = '',
  onSort,
  busy = false,
}: {
  tableId: string;
  label: string;
  columns: readonly TableColumn<T>[];
  rows: readonly T[];
  keyOf: (row: T) => string;
  /** The order the list was asked for, as the address holds it. */
  sort?: string;
  /** A new order; `''` for the list's own. */
  onSort?: (sort: string) => void;
  /** A newer answer is on its way: the rows on screen stay until it arrives. */
  busy?: boolean;
}) {
  const session = useSession();
  const key = prefsKey(session.hospital, session.userId, tableId);
  const [prefs, setPrefs] = useState<ColumnPrefs>(() => readPrefs(key));
  const [said, setSaid] = useState('');
  const byId = new Map(columns.map((column) => [column.id, column]));
  const ids = columns.map((column) => column.id);
  const fixed = new Set(columns.filter((column) => column.fixed === true).map((column) => column.id));

  const save = (next: ColumnPrefs) => {
    setPrefs(next);
    writePrefs(key, next);
  };

  const asked = parseSort(sort);
  const sortedColumn = asked === null ? undefined : columns.find((column) => column.sort === asked.name);
  const sorting: SortingState =
    sortedColumn === undefined || asked === null ? [] : [{ id: sortedColumn.id, desc: asked.desc }];
  const columnVisibility: ColumnVisibilityState = Object.fromEntries(
    prefs.hidden.filter((id) => !fixed.has(id) && byId.has(id)).map((id) => [id, false]),
  );
  const columnOrder: ColumnOrderState = orderOf(ids, prefs.order);

  const helper = createColumnHelper<typeof features, T>();
  const definitions = columns.map((column) =>
    helper.display({
      id: column.id,
      header: column.header,
      cell: (info) => column.cell(info.row.original),
      enableHiding: column.fixed !== true,
      enableSorting: column.sort !== undefined && onSort !== undefined,
    }),
  );

  const instance = useTable<typeof features, T>({
    features,
    columns: definitions,
    data: rows,
    getRowId: keyOf,
    manualFiltering: true,
    manualSorting: true,
    manualPagination: true,
    state: { sorting, columnVisibility, columnOrder },
    onSortingChange: () => undefined,
    onColumnVisibilityChange: () => undefined,
    onColumnOrderChange: () => undefined,
  });

  // One column at a time: ascending, then descending, then the list's own order again.
  const toggle = (column: TableColumn<T>) => {
    if (column.sort === undefined || onSort === undefined) {
      return;
    }

    const current = sortedColumn?.id === column.id ? (asked?.desc === true ? 'descending' : 'ascending') : null;
    const next: Direction | null = current === null ? 'ascending' : current === 'ascending' ? 'descending' : null;
    onSort(next === null ? '' : next === 'ascending' ? column.sort : `-${column.sort}`);
    setSaid(
      next === null
        ? `Sorted as the list is by default.`
        : `Sorted by ${column.header}, ${next === 'ascending' ? 'ascending' : 'descending'}.`,
    );
  };

  const visible = instance.getVisibleLeafColumns().map((column) => column.id);

  return (
    <div className={styles.frame}>
      <div className={styles.tools}>
        <ColumnsMenu
          tableLabel={label}
          columns={columnOrder.map((id) => ({ id, header: byId.get(id)?.header ?? id, fixed: fixed.has(id) }))}
          hidden={new Set(Object.keys(columnVisibility))}
          onToggle={(id) =>
            save({
              ...prefs,
              hidden: prefs.hidden.includes(id) ? prefs.hidden.filter((name) => name !== id) : [...prefs.hidden, id],
            })
          }
          onMove={(id, by) => {
            const order = [...columnOrder];
            const from = order.indexOf(id);
            const to = from + by;
            if (from < 0 || to < 0 || to >= order.length) {
              return;
            }

            order.splice(to, 0, ...order.splice(from, 1));
            save({ ...prefs, order });
          }}
          onReset={() => save({ hidden: [], order: [] })}
        />
      </div>
      <div className={table.wrap}>
        <table className={table.table} aria-label={label} aria-busy={busy}>
          <thead>
            {instance.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {group.headers.map((header) => {
                  const column = byId.get(header.column.id);
                  if (column === undefined) {
                    return null;
                  }

                  // A display column has no accessor, so TanStack's own getCanSort is false for every column: the
                  // API sorts, and a column can be sorted when the API names an order for it.
                  const canSort = column.sort !== undefined && onSort !== undefined;
                  const direction =
                    sortedColumn?.id === column.id && asked !== null ? (asked.desc ? 'desc' : 'asc') : false;
                  const ariaSort = direction === 'asc' ? 'ascending' : direction === 'desc' ? 'descending' : 'none';
                  return (
                    <th
                      key={header.id}
                      scope="col"
                      className={column.numeric === true ? table.number : undefined}
                      aria-sort={canSort ? ariaSort : undefined}
                    >
                      {canSort ? (
                        <button type="button" className={styles.sort} onClick={() => toggle(column)}>
                          {column.header}
                          <Icon
                            name={direction === 'asc' ? 'sort-up' : direction === 'desc' ? 'sort-down' : 'sort'}
                            className={direction === false ? styles.sortIdle : styles.sortIcon}
                          />
                        </button>
                      ) : (
                        column.header
                      )}
                    </th>
                  );
                })}
              </tr>
            ))}
          </thead>
          <tbody>
            {instance.getRowModel().rows.map((row) => (
              <tr key={row.id}>
                {row.getVisibleCells().map((cell) => (
                  <td key={cell.id} className={byId.get(cell.column.id)?.numeric === true ? table.number : undefined}>
                    {byId.get(cell.column.id)?.cell(row.original)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className={styles.said} role="status" aria-live="polite">
        {said}
      </p>
      {/* For a test and for print: the columns shown, in their order. */}
      <span hidden data-columns={visible.join(',')} />
    </div>
  );
}
