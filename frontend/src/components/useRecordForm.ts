import { useState } from 'react';
import type { FieldValues, Path, UseFormReturn } from 'react-hook-form';
import { isConflict, problemOf } from '../api/call';
import type { ApiProblem } from '../api/problem';
import { changesBetween, type Change, type ComparedField } from './conflict';

/**
 * What a record's form does with the API's answers (ADR 0039 points 2, 7 and 8): it keeps the record it started from,
 * whose version every save sends; it puts the API's messages for its fields on the fields and the rest in the error
 * component; and on a conflict it reads the record again, says what changed, and lets the user load the latest version or
 * keep their changes on top of it. A create has no record to start from and meets no conflict.
 */
export function useRecordForm<T, V extends FieldValues>({
  form,
  fields,
  initial,
  compared = [],
  valuesOf,
  reload,
  mapField,
}: {
  form: UseFormReturn<V>;
  /** The form's fields, named as the API names them, so its messages land on them. */
  fields: readonly Path<V>[];
  initial?: T;
  compared?: readonly ComparedField<T>[];
  valuesOf?: (record: T) => V;
  reload?: () => Promise<T>;
  /** For a field the API names otherwise than the form (`lines[1].quantity`): the form's field, or null when none. */
  mapField?: (apiField: string) => Path<V> | null;
}) {
  const [base, setBase] = useState<T | undefined>(initial);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [conflict, setConflict] = useState<{ latest: T; changes: Change[] } | null>(null);
  const [kept, setKept] = useState(false);
  const [placed, setPlaced] = useState<string[]>([]);

  const clear = () => {
    setProblem(null);
    setConflict(null);
  };

  const failed = async (error: unknown) => {
    if (isConflict(error) && base !== undefined && reload !== undefined) {
      try {
        const latest = await reload();
        setProblem(null);
        setKept(false);
        setConflict({ latest, changes: changesBetween(base, latest, compared) });
        return;
      } catch (reloadError) {
        setProblem(problemOf(reloadError));
        return;
      }
    }

    const refused = problemOf(error);
    const known = new Set<string>(fields);
    const placedNow: string[] = [];
    for (const [apiField, messages] of Object.entries(refused.fieldErrors)) {
      const field = mapField?.(apiField) ?? (known.has(apiField) ? (apiField as Path<V>) : null);
      const message = messages[0];
      if (field !== null && message !== undefined) {
        form.setError(field, { type: 'server', message });
        placedNow.push(apiField);
      }
    }

    setPlaced(placedNow);
    setProblem(refused);
  };

  /** The record is now this one (a part of a save went through): the next save sends its version. */
  const rebase = (record: T) => setBase(record);

  const loadLatest = () => {
    if (conflict !== null && valuesOf !== undefined) {
      form.reset(valuesOf(conflict.latest));
      setBase(conflict.latest);
      setConflict(null);
    }
  };

  const keepMine = () => {
    if (conflict !== null) {
      setBase(conflict.latest);
      setConflict(null);
      setKept(true);
    }
  };

  return { base, problem, conflict, kept, placed, failed, clear, rebase, loadLatest, keepMine };
}
