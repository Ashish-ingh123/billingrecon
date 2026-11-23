/** A field of a record, as a conflict lists it: its name for people, and how its value reads. */
export interface ComparedField<T> {
  label: string;
  value: (record: T) => string;
}

/** A field whose value is no longer the one the user started from. */
export interface Change {
  label: string;
  before: string;
  now: string;
}

/** The fields that differ between the record a form started from and the record as it is now (ADR 0039 point 7). */
export function changesBetween<T>(before: T, now: T, fields: readonly ComparedField<T>[]): Change[] {
  return fields
    .map((field) => ({ label: field.label, before: field.value(before), now: field.value(now) }))
    .filter((change) => change.before !== change.now);
}
