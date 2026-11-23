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
}: {
  form: UseFormReturn<V>;
  /** The form's fields, named as the API names them, so its messages land on them. */
  fields: readonly Path<V>[];
  initial?: T;
  compared?: readonly ComparedField<T>[];
  valuesOf?: (record: T) => V;
  reload?: () => Promise<T>;
}) {
  const [base, setBase] = useState<T | undefined>(initial);
  const [problem, setProblem] = useState<ApiProblem | null>(null);
  const [conflict, setConflict] = useState<{ latest: T; changes: Change[] } | null>(null);
  const [kept, setKept] = useState(false);

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
    for (const field of fields) {
      const message = refused.fieldErrors[field]?.[0];
      if (message !== undefined) {
        form.setError(field, { type: 'server', message });
      }
    }

    setProblem(refused);
  };

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

  return { base, problem, conflict, kept, failed, clear, loadLatest, keepMine };
}
