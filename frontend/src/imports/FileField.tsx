import { useId, useState } from 'react';
import { TextField } from '../components/Field';
import type { ChosenFile } from './api';

/**
 * Chooses one CSV file and reads it as text (Prompt 10.3b). The text is handed to the page and kept nowhere else: not in
 * the browser's storage, not in the address. A file that cannot be read says so at the field.
 */
export function FileField({
  label,
  hint,
  required = false,
  onChange,
}: {
  label: string;
  hint: string;
  required?: boolean;
  onChange: (file: ChosenFile | null) => void;
}) {
  const id = useId();
  const [error, setError] = useState<string | undefined>(undefined);

  return (
    <TextField
      id={id}
      type="file"
      accept=".csv,text/csv"
      label={label}
      hint={hint}
      requiredMark={required}
      {...(error === undefined ? {} : { error })}
      onChange={(event) => {
        const file = event.target.files?.[0];
        if (file === undefined) {
          setError(undefined);
          onChange(null);
          return;
        }

        file
          .text()
          .then((text) => {
            setError(undefined);
            onChange({ name: file.name, text });
          })
          .catch(() => {
            setError('The file could not be read. Choose it again.');
            onChange(null);
          });
      }}
    />
  );
}
