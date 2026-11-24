// `node --test deploy/load/`: the arithmetic the verdict of the load test rests on (ADR 0046).

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { judge, parseServerTiming, percentile, summarise, table } from './stats.mjs';

test('the nearest-rank percentile is a value of the set that the share asked for does not exceed', () => {
  const hundred = Array.from({ length: 100 }, (_, index) => index + 1);
  assert.equal(percentile(hundred, 50), 50);
  assert.equal(percentile(hundred, 95), 95);
  assert.equal(percentile(hundred, 99), 99);
  assert.equal(percentile(hundred, 100), 100);

  // Twenty values: the 95th percentile is the 19th, so one slow request in twenty does not count, two do.
  const twenty = [...Array.from({ length: 19 }, () => 10), 900];
  assert.equal(percentile(twenty, 95), 10);
  const twoSlow = [...Array.from({ length: 18 }, () => 10), 900, 900];
  assert.equal(percentile(twoSlow, 95), 900);

  assert.equal(percentile([7], 95), 7);
  assert.equal(percentile([], 95), null);
  assert.throws(() => percentile([1], 0), RangeError);
});

test('a summary sorts what it is given and has no figures for nothing', () => {
  assert.deepEqual(summarise([30, 10, 20]), { count: 3, p50: 20, p95: 30, p99: 30, max: 30 });
  assert.deepEqual(summarise([]), { count: 0, p50: null, p95: null, p99: null, max: null });
});

test('the Server-Timing header is read as milliseconds by name', () => {
  assert.deepEqual(parseServerTiming('number-wait;dur=1.25, number-hold;dur=6.8'), {
    'number-wait': 1.25,
    'number-hold': 6.8,
  });
  assert.deepEqual(parseServerTiming('cache;desc="hit", db;dur=3'), { db: 3 });
  assert.deepEqual(parseServerTiming(null), {});
  assert.deepEqual(parseServerTiming(''), {});
});

test('the verdict fails a missed target, an error, a drift row and a figure nobody measured', () => {
  const targets = { p95Ms: 500, holdMs: 20 };
  const passing = judge(
    {
      operations: { 'post bill': summarise([100, 200, 499]), 'record payment': summarise([50]) },
      series: { Bill: { hold: summarise([5, 19]) } },
      errors: 0,
      driftRows: 0,
    },
    targets,
  );
  assert.ok(passing.every((verdict) => verdict.pass));

  const failing = judge(
    {
      operations: { 'post bill': summarise([500]), 'record payment': summarise([]) },
      series: { Bill: { hold: summarise([20]) } },
      errors: 1,
      driftRows: 2,
    },
    targets,
  );
  assert.deepEqual(
    failing.map((verdict) => verdict.pass),
    [false, false, false, false, false],
  );
  assert.equal(failing[1].measured, 'not measured');
});

test('a table pads its columns', () => {
  assert.equal(table(['a', 'n'], [['xyz', 1]]), 'a    n\n---  -\nxyz  1');
});
