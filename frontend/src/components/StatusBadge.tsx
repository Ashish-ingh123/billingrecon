import { DocumentStatus } from './DocumentStatus';

/** The status of a patient, a payer or a service item, from the one mapping of statuses (ADR 0049). */
export function StatusBadge({ status }: { status: 'Active' | 'Inactive' }) {
  return <DocumentStatus kind="record" status={status} />;
}
