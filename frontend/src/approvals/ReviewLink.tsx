import { Link } from 'react-router';
import { useCan } from '../auth/permissions';
import type { components } from '../api/schema';

/**
 * Where a checker decides a document that waits (ADR 0042 point 3): the inbox, filtered to its kind. Shown to those who may
 * decide; the maker of the request finds it there too, without an action of their own.
 */
export function ReviewLink({ documentType }: { documentType: components['schemas']['ApprovalDocumentType'] }) {
  return useCan('Decide') ? <Link to={`/approvals?documentType=${documentType}`}>Review it in Approvals</Link> : null;
}
