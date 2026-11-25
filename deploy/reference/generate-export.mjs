// The export of the reference hospital (docs/reference-hospital.md, ADR 0058): the files a hospital would hand over on
// the day it starts here, in the layout of docs/runbook.md section 18. Every record is made up, from a fixed seed, so
// the same command always writes the same files; nothing is copied from anywhere, and no person, phone number or
// address in them is anyone's. Run from the repository root:
//
//   node deploy/reference/generate-export.mjs            writes deploy/reference/export/*.csv
//   node deploy/reference/generate-export.mjs --check    exits 1 if the committed files are not what it would write
//
// No package: Node's own modules only.

import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const cutOverDate = '2027-04-01';
const seed = 20270401;
const patientCount = 2000;
const guarantorCount = 12;
const billCount = 300;
const advanceCount = 40;
const creditCount = 15;

const givenNames = [
  'Aarav', 'Aditi', 'Akash', 'Ananya', 'Arjun', 'Bhavna', 'Chetan', 'Deepa', 'Dev', 'Divya', 'Farhan', 'Gauri', 'Harish',
  'Isha', 'Jatin', 'Kavya', 'Kiran', 'Lakshmi', 'Manish', 'Meera', 'Mohan', 'Neha', 'Nikhil', 'Pooja', 'Pranav', 'Priya',
  'Rahul', 'Rekha', 'Rohan', 'Sana', 'Sanjay', 'Shreya', 'Suresh', 'Tanvi', 'Uma', 'Varun', 'Vidya', 'Yash', 'Zoya', 'Nitin',
];
const familyNames = [
  'Agarwal', 'Bose', 'Chopra', 'Das', 'Fernandes', 'Ghosh', 'Hegde', 'Iyer', 'Joshi', 'Kulkarni', 'Mehta', 'Nair', 'Pillai',
  'Qureshi', 'Rao', 'Shah', 'Thomas', 'Verma', 'Yadav', 'Banerjee', 'Desai', 'Gupta', 'Kapoor', 'Menon', 'Patil',
];
const cities = [
  ['Pune', 'Maharashtra'], ['Nashik', 'Maharashtra'], ['Satara', 'Maharashtra'], ['Solapur', 'Maharashtra'],
  ['Belagavi', 'Karnataka'], ['Panaji', 'Goa'],
];
const methods = ['Cash', 'Card', 'UPI', 'BankTransfer', 'Cheque'];

// mulberry32: a small generator with a state of 32 bits, enough for made-up records and the same on every machine.
function generator(start) {
  let state = start >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let mixed = state;
    mixed = Math.imul(mixed ^ (mixed >>> 15), mixed | 1);
    mixed ^= mixed + Math.imul(mixed ^ (mixed >>> 7), mixed | 61);
    return ((mixed ^ (mixed >>> 14)) >>> 0) / 4294967296;
  };
}

const pad = (value, width) => String(value).padStart(width, '0');
const field = (value) => (/[",\r\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value);
const csv = (header, rows) => [header, ...rows].map((row) => row.map((value) => field(String(value ?? ''))).join(',')).join('\n') + '\n';
const paise = (amount) => `${Math.floor(amount / 100)}.${pad(amount % 100, 2)}`;

function isoDate(year, month, day) {
  return `${year}-${pad(month, 2)}-${pad(day, 2)}`;
}

// A day of the twelve months before the cut-over: fiscal year 2026-27, 1 April 2026 to 31 March 2027.
function dayBeforeCutOver(random, latestDaysBack) {
  const cutOver = Date.UTC(2027, 3, 1);
  const back = 1 + Math.floor(random() * latestDaysBack);
  const day = new Date(cutOver - back * 86_400_000);
  return isoDate(day.getUTCFullYear(), day.getUTCMonth() + 1, day.getUTCDate());
}

function addDays(date, days) {
  const [year, month, day] = date.split('-').map(Number);
  const moved = new Date(Date.UTC(year, month - 1, day) + days * 86_400_000);
  return isoDate(moved.getUTCFullYear(), moved.getUTCMonth() + 1, moved.getUTCDate());
}

export function generate() {
  const random = generator(seed);
  const pick = (list) => list[Math.floor(random() * list.length)];

  const patients = [];
  for (let index = 1; index <= patientCount; index++) {
    const [city, state] = pick(cities);
    patients.push({
      mrn: `NF-${pad(index, 6)}`,
      name: `${pick(givenNames)} ${pick(familyNames)}`,
      dateOfBirth: isoDate(1940 + Math.floor(random() * 80), 1 + Math.floor(random() * 12), 1 + Math.floor(random() * 28)),
      sex: random() < 0.5 ? 'Female' : 'Male',
      // No such subscriber: the digits after the country code begin with zeros.
      phone: `+91 00000 ${pad(index, 5)}`,
      addressLine1: `${1 + Math.floor(random() * 200)} Example Road`,
      city,
      state,
    });
  }

  const guarantors = [];
  for (let index = 1; index <= guarantorCount; index++) {
    guarantors.push({ reference: `G-${pad(index, 4)}`, name: `${pick(givenNames)} ${pick(familyNames)}`, phone: `+91 00000 9${pad(index, 4)}` });
  }

  let receivable = 0;
  const bills = [];
  for (let index = 1; index <= billCount; index++) {
    const patient = pick(patients);
    const billDate = dayBeforeCutOver(random, 240);
    const amount = 5_000 + Math.floor(random() * 4_500_000);
    receivable += amount;
    bills.push({
      number: `OLD/26-27/${pad(index, 5)}`,
      mrn: patient.mrn,
      // One bill in twenty is owed by a guarantor and not by the patient.
      guarantor: random() < 0.05 ? pick(guarantors).reference : '',
      billDate,
      dueDate: addDays(billDate, 15),
      amount,
    });
  }

  let advance = 0;
  let credit = 0;
  const advances = [];
  for (let index = 1; index <= advanceCount + creditCount; index++) {
    const isCredit = index > advanceCount;
    const amount = 10_000 + Math.floor(random() * 2_000_000);
    const byGuarantor = !isCredit && random() < 0.1;
    if (isCredit) {
      credit += amount;
    } else {
      advance += amount;
    }

    advances.push({
      reference: `${isCredit ? 'CR' : 'ADV'}-${pad(index, 5)}`,
      mrn: byGuarantor ? '' : pick(patients).mrn,
      guarantor: byGuarantor ? pick(guarantors).reference : '',
      date: dayBeforeCutOver(random, 90),
      amount,
      method: pick(methods),
      kind: isCredit ? 'Credit' : 'Advance',
    });
  }

  return {
    'patients.csv': csv(
      ['mrn', 'full_name', 'date_of_birth', 'sex', 'phone', 'address_line1', 'city', 'state'],
      patients.map((row) => [row.mrn, row.name, row.dateOfBirth, row.sex, row.phone, row.addressLine1, row.city, row.state]),
    ),
    'guarantors.csv': csv(['guarantor_ref', 'name', 'phone'], guarantors.map((row) => [row.reference, row.name, row.phone])),
    'bills.csv': csv(
      ['legacy_number', 'mrn', 'guarantor_ref', 'bill_date', 'due_date', 'outstanding_amount'],
      bills.map((row) => [row.number, row.mrn, row.guarantor, row.billDate, row.dueDate, paise(row.amount)]),
    ),
    'advances.csv': csv(
      ['legacy_reference', 'mrn', 'guarantor_ref', 'received_date', 'amount', 'method', 'kind'],
      advances.map((row) => [row.reference, row.mrn, row.guarantor, row.date, paise(row.amount), row.method, row.kind]),
    ),
    'trial_balance.csv': csv(
      ['account', 'amount'],
      [['Receivable', paise(receivable)], ['Advance', paise(advance)], ['Credit', paise(credit)]],
    ),
  };
}

const here = dirname(fileURLToPath(import.meta.url));
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const folder = join(here, 'export');
  const files = generate();
  if (process.argv.includes('--check')) {
    const different = Object.entries(files).filter(([name, text]) => !existsSync(join(folder, name)) || readFileSync(join(folder, name), 'utf8') !== text);
    if (different.length > 0) {
      console.error(`Not what the generator writes: ${different.map(([name]) => name).join(', ')}. Run it without --check and commit the files.`);
      process.exit(1);
    }

    console.log('The committed export is what the generator writes.');
  } else {
    mkdirSync(folder, { recursive: true });
    for (const [name, text] of Object.entries(files)) {
      writeFileSync(join(folder, name), text, 'utf8');
    }

    console.log(`Wrote ${Object.keys(files).length} files to ${folder}.`);
  }
}
