import { DocumentStatus } from '../components/DocumentStatus';
import type { BillStatus } from './api';

/** The status of a bill, from the one mapping of statuses (ADR 0049). */
export function BillStatusBadge({ status }: { status: BillStatus }) {
  return <DocumentStatus kind="bill" status={status} />;
}
