import { useApiClient } from '../api/ApiClientContext';
import { answer } from '../api/call';
import { RecordPicker } from '../components/RecordPicker';
import { formatDecimal, formatPercent } from '../format';
import type { Patient } from '../patients/api';
import { kindLabel, type Payer } from '../payers/api';
import type { ServiceItem } from '../serviceItems/api';

/** How many records a picker offers for one search. */
const offered = 10;

interface PickerProps<T> {
  selected: T | null;
  onSelect: (record: T | null) => void;
  error?: string | undefined;
  disabled?: boolean;
}

/** Chooses an active patient by MRN or name: a bill is raised only for an active patient (BR-102). */
export function PatientPicker(props: PickerProps<Patient>) {
  const client = useApiClient();

  return (
    <RecordPicker
      kind="patient"
      label="Patient"
      hint={props.disabled ? 'The patient of a bill never changes.' : 'Find an active patient by MRN or name.'}
      find={async (text) =>
        (
          await answer(
            client.GET('/api/v1/patients', {
              params: { query: { Search: text, Status: 'Active', Page: 1, PageSize: offered } },
            }),
          )
        ).items
      }
      describe={(patient) => `${patient.fullName} (MRN ${patient.mrn})`}
      keyOf={(patient) => patient.id}
      {...props}
    />
  );
}

/** Chooses an active payer by name: the patient's own payer, a guarantor, or another patient's payer (BR-105, BR-106). */
export function PayerPicker(props: PickerProps<Payer>) {
  const client = useApiClient();

  return (
    <RecordPicker
      kind="payer"
      label="Payer"
      hint={
        props.disabled ? 'The payer of a bill never changes.' : "Who owes the bill: the patient's own payer by default."
      }
      find={async (text) =>
        (
          await answer(
            client.GET('/api/v1/payers', {
              params: { query: { Search: text, Status: 'Active', Page: 1, PageSize: offered } },
            }),
          )
        ).items
      }
      describe={(payer) =>
        `${payer.name} (${kindLabel(payer.kind)}, ${payer.patientId === null ? 'guarantor' : "a patient's own payer"})`
      }
      keyOf={(payer) => payer.id}
      {...props}
    />
  );
}

/** Chooses an active service item by code or name, for a line priced from the catalogue (BR-201). */
export function ServiceItemPicker(props: PickerProps<ServiceItem>) {
  const client = useApiClient();

  return (
    <RecordPicker
      kind="service item"
      label="Add a line from the catalogue"
      find={async (text) =>
        (
          await answer(
            client.GET('/api/v1/service-items', {
              params: { query: { Search: text, Status: 'Active', Page: 1, PageSize: offered } },
            }),
          )
        ).items
      }
      describe={(item) =>
        `${item.code}: ${item.name} (₹${formatDecimal(item.defaultUnitPrice, 4)}, tax ${formatPercent(item.taxRatePercent)})`
      }
      keyOf={(item) => item.id}
      {...props}
    />
  );
}
