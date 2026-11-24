// The budgets of the build (Prompt 9.4b, ADR 0050 amended; backlog B21): what a first visit to the sign-in page loads,
// and what any other route adds, compressed with gzip as a server sends it. Run after `npm run build`:
// `npm run budgets`. A budget that no longer holds fails here, with the sizes; change a budget only with a reason.
import { readFileSync, readdirSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const dist = new URL('../dist/', import.meta.url);
const kB = 1024;

/** The budgets, in compressed bytes. */
export const budgets = {
  /** The JavaScript of a first visit to the sign-in page: the entry and what it preloads (186 kB before routes were split). */
  firstVisitScript: 145 * kB,
  firstVisitStyle: 15 * kB,
  /** Any chunk a route loads on demand, but the dashboard's. */
  routeChunk: 20 * kB,
  /** The dashboard's chunk, which carries the chart library (ADR 0044 point 5). */
  dashboardChunk: 130 * kB,
};

const compressed = (name) => gzipSync(readFileSync(new URL(`assets/${name}`, dist))).length;

function firstVisit() {
  const html = readFileSync(new URL('index.html', dist), 'utf8');
  const assets = [...html.matchAll(/(?:src|href)="\/assets\/([^"]+)"/g)].map((match) => match[1]);
  return {
    scripts: assets.filter((name) => name.endsWith('.js')),
    styles: assets.filter((name) => name.endsWith('.css')),
  };
}

const sum = (names) => names.reduce((total, name) => total + compressed(name), 0);
const shown = (bytes) => `${(bytes / kB).toFixed(1)} kB`;

test('a first visit to the sign-in page loads its JavaScript within the budget', () => {
  const { scripts } = firstVisit();
  assert.ok(scripts.length > 0, 'index.html names its scripts');
  const total = sum(scripts);
  assert.ok(
    total <= budgets.firstVisitScript,
    `${shown(total)} > ${shown(budgets.firstVisitScript)}: ${scripts.join(', ')}`,
  );
});

test('a first visit to the sign-in page loads its CSS within the budget', () => {
  const { styles } = firstVisit();
  const total = sum(styles);
  assert.ok(total <= budgets.firstVisitStyle, `${shown(total)} > ${shown(budgets.firstVisitStyle)}`);
});

test('every route loads on demand within its budget', () => {
  const { scripts } = firstVisit();
  const chunks = readdirSync(new URL('assets/', dist)).filter(
    (name) => name.endsWith('.js') && !scripts.includes(name),
  );
  assert.ok(chunks.length >= 30, `the routes are split (${String(chunks.length)} chunks)`);
  for (const chunk of chunks) {
    const budget = chunk.startsWith('DashboardPage-') ? budgets.dashboardChunk : budgets.routeChunk;
    const size = compressed(chunk);
    assert.ok(size <= budget, `${chunk}: ${shown(size)} > ${shown(budget)}`);
  }
});
