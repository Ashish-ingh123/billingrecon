import { Badge } from './Badge';

/** The status of a patient, a payer or a service item: Active in the colour of success, Inactive muted (ADR 0048). */
export function StatusBadge({ status }: { status: 'Active' | 'Inactive' }) {
  return <Badge tone={status === 'Active' ? 'success' : 'muted'}>{status}</Badge>;
}
